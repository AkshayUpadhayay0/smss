using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // The weekly timetable. Writing and the section grid are School Admin only (IsSchoolAdmin); a teacher's own grid is open to that teacher
    // (checked in the service, same self-only pattern as Class-Teacher /check). The school and the acting user always come from the token.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class TimetableController : ControllerBase
    {
        private readonly ITimetableService _service;
        public TimetableController(ITimetableService service) => _service = service;

        private IActionResult? Refuse() => User.IsSchoolAdmin() ? null : this.Forbidden("Only a School Admin can manage the timetable.");
        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
        private bool TryUser(out long userId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

        // POST api/Timetable/assign   Body: { academicYearId, classId?, sectionId, dayOfWeek (1-6), periodId, subjectId, employeeId, roomNo? }
        // Assigns or reassigns the slot in ONE transaction (old entry deactivated, new one inserted). 409 when the teacher is already teaching another section then.
        [HttpPost("assign")]
        public async Task<IActionResult> Assign([FromBody] AssignTimetableRequest request, CancellationToken ct)
        {
            if (Refuse() is { } refused) return refused;
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _service.AssignAsync(User.GetSchoolId(), userId, request, ct));
        }

        // POST api/Timetable/unassign   Body: { academicYearId, sectionId, dayOfWeek, periodId }   (the entry is kept as history)
        [HttpPost("unassign")]
        public async Task<IActionResult> Unassign([FromBody] UnassignTimetableRequest request, CancellationToken ct) =>
            Refuse() ?? Reply(await _service.UnassignAsync(User.GetSchoolId(), request, ct));

        // GET api/Timetable/lookups?sectionId=3   (the subjects the section's class studies + the people who can teach, for the assign dialog)
        [HttpGet("lookups")]
        public async Task<IActionResult> Lookups([FromQuery] long? sectionId, CancellationToken ct)
        {
            if (Refuse() is { } refused) return refused;
            if (sectionId is null or <= 0) return StatusCode(400, new ApiResponse<object> { Status = false, StatusCode = 400, Message = "sectionId is required.", Data = null });
            return Reply(await _service.GetLookupsAsync(User.GetSchoolId(), sectionId.Value, ct));
        }

        // GET api/Timetable/class/{sectionId}?academicYearId=   (the full weekly grid of a section; the year defaults to the current one)
        [HttpGet("class/{sectionId:long}")]
        public async Task<IActionResult> ClassGrid(long sectionId, [FromQuery] long? academicYearId, CancellationToken ct) =>
            Refuse() ?? Reply(await _service.GetClassGridAsync(User.GetSchoolId(), sectionId, academicYearId, ct));

        // GET api/Timetable/teacher/{employeeId}?academicYearId=   (a School Admin: anyone; anyone else: themselves only)
        [HttpGet("teacher/{employeeId:length(1,20)}")]
        public async Task<IActionResult> TeacherGrid(string employeeId, [FromQuery] long? academicYearId, CancellationToken ct)
        {
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _service.GetTeacherGridAsync(User.GetSchoolId(), userId, User.IsSchoolAdmin(), employeeId, academicYearId, ct));
        }

        // GET api/Timetable/my?academicYearId=   (the caller's own weekly grid)
        [HttpGet("my")]
        public async Task<IActionResult> My([FromQuery] long? academicYearId, CancellationToken ct)
        {
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _service.GetMyGridAsync(User.GetSchoolId(), userId, academicYearId, ct));
        }
    }
}
