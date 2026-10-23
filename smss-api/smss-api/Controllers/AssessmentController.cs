using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // Internal assessments / class tests. The school and the acting user always come from the token. Who may do what:
    //   create / roster:        School Admin (any section; the subject must still be mapped to the class) or a Teacher (only where the Timetable says they teach it)
    //   update / delete:        the owning teacher or a School Admin (the service refuses any other teacher, even an eligible one)
    //   GET {id}, GET my:       the owning teacher / School Admin;   GET (list), GET section/{id}: School Admin only
    //   GET student/my:         a Student: their own results only
    // Principal / Accountant / Admission Staff / Guardian have no access.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AssessmentController : ControllerBase
    {
        private readonly IAssessmentService _service;
        public AssessmentController(IAssessmentService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
        private bool TryCaller(out AssessmentCaller caller)
        {
            var ok = long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
            caller = new AssessmentCaller(userId, User.IsSchoolAdmin(), User.CanManageAssessments() && !User.IsSchoolAdmin(), User.IsStudent());
            return ok;
        }
        private IActionResult? RefuseUnlessManager() => User.CanManageAssessments() ? null : this.Forbidden("Only a School Admin or a Teacher can manage tests.");

        // GET api/Assessment/types   (the assessment types for the form's dropdown)
        [HttpGet("types")]
        public async Task<IActionResult> Types(CancellationToken ct) =>
            RefuseUnlessManager() ?? Reply(await _service.GetTypesAsync(ct));

        // GET api/Assessment/roster?academicYearId=&sectionId=&subjectId=   (the students a test for that section + subject must cover)
        [HttpGet("roster")]
        public async Task<IActionResult> Roster([FromQuery] long? academicYearId, [FromQuery] long? sectionId, [FromQuery] long? subjectId, CancellationToken ct)
        {
            if (RefuseUnlessManager() is { } refused) return refused;
            if (!TryCaller(out var caller)) return Unauthorized();
            if (academicYearId is null or <= 0 || sectionId is null or <= 0 || subjectId is null or <= 0)
                return StatusCode(400, new ApiResponse<object> { Status = false, StatusCode = 400, Message = "academicYearId, sectionId and subjectId are required.", Data = null });
            return Reply(await _service.GetRosterAsync(User.GetSchoolId(), caller, academicYearId.Value, sectionId.Value, subjectId.Value, ct));
        }

        // POST api/Assessment   Body: { academicYearId, sectionId, subjectId, assessmentTypeId, employeeId?, title, testDate, maxMarks, marks: [{ studentId, obtainedMarks | isAbsent }] }
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateAssessmentRequest request, CancellationToken ct)
        {
            if (RefuseUnlessManager() is { } refused) return refused;
            if (!TryCaller(out var caller)) return Unauthorized();
            return Reply(await _service.CreateAsync(User.GetSchoolId(), caller, request, ct));
        }

        // PUT api/Assessment/{id}   Body: { title, testDate, maxMarks, marks: [...] }   (the whole roster again)
        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] UpdateAssessmentRequest request, CancellationToken ct)
        {
            if (RefuseUnlessManager() is { } refused) return refused;
            if (!TryCaller(out var caller)) return Unauthorized();
            return Reply(await _service.UpdateAsync(User.GetSchoolId(), caller, id, request, ct));
        }

        // DELETE api/Assessment/{id}   (soft: hidden, status Inactive)
        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id, CancellationToken ct)
        {
            if (RefuseUnlessManager() is { } refused) return refused;
            if (!TryCaller(out var caller)) return Unauthorized();
            return Reply(await _service.DeleteAsync(User.GetSchoolId(), caller, id, ct));
        }

        // GET api/Assessment/{id}   (one test with all its marks: the owning teacher or a School Admin)
        [HttpGet("{id:long}")]
        public async Task<IActionResult> Get(long id, CancellationToken ct)
        {
            if (RefuseUnlessManager() is { } refused) return refused;
            if (!TryCaller(out var caller)) return Unauthorized();
            return Reply(await _service.GetAsync(User.GetSchoolId(), caller, id, ct));
        }

        // GET api/Assessment/my?academicYearId=&sectionId=&subjectId=&from=&to=   (the tests the caller recorded; dates filter on the test date)
        [HttpGet("my")]
        public async Task<IActionResult> My([FromQuery] long? academicYearId, [FromQuery] long? sectionId, [FromQuery] long? subjectId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        {
            if (RefuseUnlessManager() is { } refused) return refused;
            if (!TryCaller(out var caller)) return Unauthorized();
            return Reply(await _service.GetMyAsync(User.GetSchoolId(), caller, academicYearId, sectionId, subjectId, from, to, ct));
        }

        // GET api/Assessment?sectionId=&academicYearId=&subjectId=&from=&to=   (School Admin: the whole school)
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] long? sectionId, [FromQuery] long? academicYearId, [FromQuery] long? subjectId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
            !User.IsSchoolAdmin() ? this.Forbidden("Only a School Admin can see all tests.")
                                  : Reply(await _service.ListAsync(User.GetSchoolId(), academicYearId, sectionId, subjectId, from, to, ct));

        // GET api/Assessment/section/{sectionId}?academicYearId=&from=&to=   (School Admin: every test of the section, whoever recorded it)
        [HttpGet("section/{sectionId:long}")]
        public async Task<IActionResult> GetSection(long sectionId, [FromQuery] long? academicYearId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
            !User.IsSchoolAdmin() ? this.Forbidden("Only a School Admin can see a section's tests.")
                                  : Reply(await _service.GetSectionAsync(User.GetSchoolId(), sectionId, academicYearId, from, to, ct));

        // GET api/Assessment/student/my?from=&to=   (a Student: their own results only, most recent first)
        [HttpGet("student/my")]
        public async Task<IActionResult> StudentMy([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        {
            if (!User.IsStudent()) return this.Forbidden("Only a student can see their own results here.");
            if (!TryCaller(out var caller)) return Unauthorized();
            return Reply(await _service.GetStudentMyAsync(User.GetSchoolId(), caller, from, to, ct));
        }
    }
}
