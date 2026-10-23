using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // Class teacher of each section, per academic year. School Admin only (IsSchoolAdmin from the token), except `check`, which a teacher may
    // also call about themselves (Attendance). The school always comes from the token; no school id is accepted.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ClassTeacherController : ControllerBase
    {
        private readonly IClassTeacherService _service;
        public ClassTeacherController(IClassTeacherService service) => _service = service;

        private IActionResult? Refuse() => User.IsSchoolAdmin() ? null : this.Forbidden("Only a School Admin can manage class teachers.");
        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
        private IActionResult BadRequestMessage(string message) =>
            StatusCode(400, new ApiResponse<object> { Status = false, StatusCode = 400, Message = message, Data = null });

        // POST api/ClassTeacher/assign   Body: { academicYearId, classId?, sectionId, employeeId }
        // Assigns, or reassigns: the previous Active row is deactivated and the new one inserted in ONE transaction.
        [HttpPost("assign")]
        public async Task<IActionResult> Assign([FromBody] AssignClassTeacherRequest request, CancellationToken ct)
        {
            if (Refuse() is { } refused) return refused;
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            return Reply(await _service.AssignAsync(User.GetSchoolId(), userId, request, ct));
        }

        // POST api/ClassTeacher/unassign   Body: { academicYearId, sectionId }   (POST, not DELETE: the row is kept as history)
        [HttpPost("unassign")]
        public async Task<IActionResult> Unassign([FromBody] UnassignClassTeacherRequest request, CancellationToken ct) =>
            Refuse() ?? Reply(await _service.UnassignAsync(User.GetSchoolId(), request, ct));

        // GET api/ClassTeacher/current?academicYearId=1&classId=2&sectionId=3   (classId optional)
        [HttpGet("current")]
        public async Task<IActionResult> GetCurrent([FromQuery] long? academicYearId, [FromQuery] long? classId, [FromQuery] long? sectionId, CancellationToken ct)
        {
            if (Refuse() is { } refused) return refused;
            if (academicYearId is null or <= 0) return BadRequestMessage("academicYearId is required.");
            if (sectionId is null or <= 0) return BadRequestMessage("sectionId is required.");
            return Reply(await _service.GetCurrentAsync(User.GetSchoolId(), academicYearId.Value, classId, sectionId.Value, ct));
        }

        // GET api/ClassTeacher?academicYearId=1   (every active section with its class teacher, or none: the admin screen)
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] long? academicYearId, CancellationToken ct)
        {
            if (Refuse() is { } refused) return refused;
            if (academicYearId is null or <= 0) return BadRequestMessage("academicYearId is required.");
            return Reply(await _service.ListAsync(User.GetSchoolId(), academicYearId.Value, ct));
        }

        // GET api/ClassTeacher/history?academicYearId=1&sectionId=3   (current AND past assignments, newest first)
        [HttpGet("history")]
        public async Task<IActionResult> History([FromQuery] long? academicYearId, [FromQuery] long? sectionId, CancellationToken ct)
        {
            if (Refuse() is { } refused) return refused;
            if (academicYearId is null or <= 0) return BadRequestMessage("academicYearId is required.");
            if (sectionId is null or <= 0) return BadRequestMessage("sectionId is required.");
            return Reply(await _service.GetHistoryAsync(User.GetSchoolId(), academicYearId.Value, sectionId.Value, ct));
        }

        // GET api/ClassTeacher/eligible-teachers   (Active, Teaching, with an active login holding the Teacher role)
        [HttpGet("eligible-teachers")]
        public async Task<IActionResult> EligibleTeachers(CancellationToken ct) =>
            Refuse() ?? Reply(await _service.GetEligibleTeachersAsync(User.GetSchoolId(), ct));

        // GET api/ClassTeacher/check?employeeId=DPS001EM001&sectionId=3&academicYearId=1   (year optional: the current year)
        // "Is this employee the Active class teacher of this section right now?" A School Admin may ask about anyone, a teacher about themselves.
        [HttpGet("check")]
        public async Task<IActionResult> Check([FromQuery] string? employeeId, [FromQuery] long? sectionId, [FromQuery] long? academicYearId, CancellationToken ct)
        {
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            if (string.IsNullOrWhiteSpace(employeeId)) return BadRequestMessage("employeeId is required.");
            if (sectionId is null or <= 0) return BadRequestMessage("sectionId is required.");
            return Reply(await _service.CheckAsync(User.GetSchoolId(), userId, User.IsSchoolAdmin(), employeeId, sectionId.Value, academicYearId, ct));
        }
    }
}
