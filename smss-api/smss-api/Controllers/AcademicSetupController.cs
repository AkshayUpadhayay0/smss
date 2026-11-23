using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // Academic Setup: what a school offers in each academic year. Everything is scoped to the caller's own school (from the token); no school id is accepted.
    // School Admin configures; Principal views. (The class / section MASTERS stay under /api/Classes and /api/Sections.)
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AcademicSetupController : ControllerBase
    {
        private readonly IAcademicSetupService _service;
        private readonly ISubjectSetupService _subjects;
        private readonly IPeriodScheduleService _schedules;
        public AcademicSetupController(IAcademicSetupService service, ISubjectSetupService subjects, IPeriodScheduleService schedules) { _service = service; _subjects = subjects; _schedules = schedules; }

        // GET api/AcademicSetup/years/5/classes
        [HttpGet("years/{yearId:long}/classes")]
        public async Task<IActionResult> GetClasses(long yearId, CancellationToken ct)
        {
            if (!User.CanViewAcademicSetup()) return this.Forbidden("Only a School Admin or Principal can see the academic setup.");
            var response = await _service.GetOfferingAsync(User.GetSchoolId(), yearId, ct);
            return StatusCode(response.StatusCode, response);
        }

        // PUT api/AcademicSetup/years/5/classes   body { classes: [ { classId, sections: [ { sectionId, maxStrength? } ] } ] }  = the WHOLE offered set
        [HttpPut("years/{yearId:long}/classes")]
        public async Task<IActionResult> SaveClasses(long yearId, [FromBody] SaveYearOfferingRequest request, CancellationToken ct)
        {
            if (!User.IsSchoolAdmin()) return this.Forbidden("Only a School Admin can change the academic setup.");
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            var response = await _service.SaveOfferingAsync(User.GetSchoolId(), userId, yearId, request, ct);
            return StatusCode(response.StatusCode, response);
        }

        // S4: which bell schedule each class follows on each day of the year (the schedules themselves are under /api/PeriodSchedules)
        // GET api/AcademicSetup/years/5/schedules
        [HttpGet("years/{yearId:long}/schedules")]
        public async Task<IActionResult> GetSchedules(long yearId, CancellationToken ct)
        {
            if (!User.CanViewAcademicSetup()) return this.Forbidden("Only a School Admin or Principal can see the academic setup.");
            var response = await _schedules.GetYearAsync(User.GetSchoolId(), yearId, ct);
            return StatusCode(response.StatusCode, response);
        }

        // PUT api/AcademicSetup/years/5/schedules   body { rows: [ { classId?, dayOfWeek?, scheduleId } ] }  = the WHOLE set (empty = everything follows the default schedule)
        [HttpPut("years/{yearId:long}/schedules")]
        public async Task<IActionResult> SaveSchedules(long yearId, [FromBody] SaveYearSchedulesRequest request, CancellationToken ct)
        {
            if (!User.IsSchoolAdmin()) return this.Forbidden("Only a School Admin can change the academic setup.");
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            var response = await _schedules.SaveYearAsync(User.GetSchoolId(), userId, yearId, request, ct);
            return StatusCode(response.StatusCode, response);
        }

        // S3: the subjects of one class in one year, and the per-section exceptions
        // GET api/AcademicSetup/years/5/classes/7/subjects
        [HttpGet("years/{yearId:long}/classes/{classId:long}/subjects")]
        public async Task<IActionResult> GetSubjects(long yearId, long classId, CancellationToken ct)
        {
            if (!User.CanViewAcademicSetup()) return this.Forbidden("Only a School Admin or Principal can see the academic setup.");
            var response = await _subjects.GetAsync(User.GetSchoolId(), yearId, classId, ct);
            return StatusCode(response.StatusCode, response);
        }

        // PUT api/AcademicSetup/years/5/classes/7/subjects   body { subjectIds: [], sections: [ { sectionId, extraSubjectIds: [], notTaughtSubjectIds: [] } ] }  = the WHOLE set
        [HttpPut("years/{yearId:long}/classes/{classId:long}/subjects")]
        public async Task<IActionResult> SaveSubjects(long yearId, long classId, [FromBody] SaveYearClassSubjectsRequest request, CancellationToken ct)
        {
            if (!User.IsSchoolAdmin()) return this.Forbidden("Only a School Admin can change the academic setup.");
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            var response = await _subjects.SaveAsync(User.GetSchoolId(), userId, yearId, classId, request, ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
