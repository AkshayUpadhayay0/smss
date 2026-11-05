using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // Daily attendance. The school and the acting user always come from the token. Who may do what:
    //   submit / read a section:  the section's class teacher, or a School Admin (checked in the service against the Class-Teacher module)
    //   correct / missing:        School Admin only (IsSchoolAdmin)
    //   student history:          a School Admin, or the class teacher of the student's section
    // Student / Guardian screens are not built yet; the read endpoints are shaped so they can use them later.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AttendanceController : ControllerBase
    {
        private readonly IAttendanceService _service;
        public AttendanceController(IAttendanceService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
        private IActionResult BadRequestMessage(string message) =>
            StatusCode(400, new ApiResponse<object> { Status = false, StatusCode = 400, Message = message, Data = null });
        private bool TryUser(out long userId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

        // POST api/Attendance/submit   Body: { sectionId, date, records: [{ studentId, attendanceStatusId }] }
        // The WHOLE section for TODAY in one request. Resubmitting the same day overwrites. Every active student must be included.
        [HttpPost("submit")]
        public async Task<IActionResult> Submit([FromBody] SubmitAttendanceRequest request, CancellationToken ct)
        {
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _service.SubmitAsync(User.GetSchoolId(), userId, User.IsSchoolAdmin(), request, ct));
        }

        // PUT api/Attendance/correct   Body: { studentId, date, attendanceStatusId, reason }   (School Admin only; any day up to today)
        // Changes the mark if there is one; if the day has NONE it adds it (a correction with nothing to overwrite). Same reason / audit fields either way.
        [HttpPut("correct")]
        public async Task<IActionResult> Correct([FromBody] CorrectAttendanceRequest request, CancellationToken ct)
        {
            if (!User.IsSchoolAdmin()) return this.Forbidden("Only a School Admin can correct attendance.");
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _service.CorrectAsync(User.GetSchoolId(), userId, request, ct));
        }

        // GET api/Attendance/section/{sectionId}?date=2026-10-22   (date optional: today) - the roster with each student's mark, or unmarked
        [HttpGet("section/{sectionId:long}")]
        public async Task<IActionResult> GetSection(long sectionId, [FromQuery] DateOnly? date, CancellationToken ct)
        {
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _service.GetSectionAsync(User.GetSchoolId(), userId, User.IsSchoolAdmin(), sectionId, date, ct));
        }

        // GET api/Attendance/student/{studentId}?from=2026-10-01&to=2026-10-31   (at most 366 days)
        [HttpGet("student/{studentId:length(1,20)}")]
        public async Task<IActionResult> GetStudent(string studentId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        {
            if (!TryUser(out var userId)) return Unauthorized();
            if (from is null || to is null) return BadRequestMessage("from and to are required.");
            return Reply(await _service.GetStudentAsync(User.GetSchoolId(), userId, User.IsSchoolAdmin(), studentId, from.Value, to.Value, ct));
        }

        // GET api/Attendance/missing?date=2026-10-22&academicYearId=1   (School Admin: active sections with students and no attendance that day)
        [HttpGet("missing")]
        public async Task<IActionResult> GetMissing([FromQuery] DateOnly? date, [FromQuery] long? academicYearId, CancellationToken ct) =>
            !User.IsSchoolAdmin() ? this.Forbidden("Only a School Admin can see which sections have not marked attendance.")
                                  : Reply(await _service.GetMissingAsync(User.GetSchoolId(), date, academicYearId, ct));

        // GET api/Attendance/report/options?academicYearId=   (School Admin, Principal, class teacher: the report's year and section pickers)
        [HttpGet("report/options")]
        public async Task<IActionResult> GetReportOptions([FromQuery] long? academicYearId, CancellationToken ct)
        {
            if (!User.CanViewAttendanceReport()) return this.Forbidden("You can't see the attendance report.");
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _service.GetReportOptionsAsync(User.GetSchoolId(), userId, User.CanViewAllAttendanceReports(), academicYearId, ct));
        }

        // GET api/Attendance/report/monthly?sectionId=&year=&month=[&academicYearId=]
        // School Admin and Principal (read-only): any section. A Teacher: only the section they are the class teacher of (403 otherwise). Another school's section: 404.
        [HttpGet("report/monthly")]
        public async Task<IActionResult> GetMonthlyReport([FromQuery] long? sectionId, [FromQuery] int? year, [FromQuery] int? month, [FromQuery] long? academicYearId, CancellationToken ct)
        {
            if (!User.CanViewAttendanceReport()) return this.Forbidden("You can't see the attendance report.");
            if (!TryUser(out var userId)) return Unauthorized();
            if (sectionId is null || year is null || month is null) return BadRequestMessage("sectionId, year and month are required.");
            return Reply(await _service.GetMonthlyReportAsync(User.GetSchoolId(), userId, User.CanViewAllAttendanceReports(), sectionId.Value, year.Value, month.Value, academicYearId, ct));
        }

        // GET api/Attendance/report/monthly/pdf?...   (same query and the same authorization; success is the PDF itself, any failure the JSON envelope)
        [HttpGet("report/monthly/pdf")]
        public async Task<IActionResult> GetMonthlyReportPdf([FromQuery] long? sectionId, [FromQuery] int? year, [FromQuery] int? month, [FromQuery] long? academicYearId, CancellationToken ct)
        {
            if (!User.CanViewAttendanceReport()) return this.Forbidden("You can't see the attendance report.");
            if (!TryUser(out var userId)) return Unauthorized();
            if (sectionId is null || year is null || month is null) return BadRequestMessage("sectionId, year and month are required.");
            var response = await _service.GetMonthlyReportPdfAsync(User.GetSchoolId(), userId, User.CanViewAllAttendanceReports(), sectionId.Value, year.Value, month.Value, academicYearId, ct);
            if (!response.Status || response.Data == null) return Reply(response);
            return File(response.Data.Content, "application/pdf", response.Data.FileName);
        }

        // GET api/Attendance/my-sections   (the sections the caller can mark today: a teacher's own; a School Admin's: all active ones)
        [HttpGet("my-sections")]
        public async Task<IActionResult> GetMySections(CancellationToken ct)
        {
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _service.GetMySectionsAsync(User.GetSchoolId(), userId, User.IsSchoolAdmin(), ct));
        }
    }
}
