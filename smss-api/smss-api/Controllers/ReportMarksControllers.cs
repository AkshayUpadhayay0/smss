using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // Examination / Report Card, STAGE 2: marks entry. The school and the acting user always come from the token. School Admin, Principal (read-only) and Teacher reach these
    // endpoints; what a Teacher may enter is decided per (section, subject) by the service: School Admin, OR the section's class teacher, OR timetabled to teach that subject there.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ReportMarksController : ControllerBase
    {
        private readonly IReportMarksService _service;
        public ReportMarksController(IReportMarksService service) => _service = service;

        private bool TryUser(out long userId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
        private IActionResult Reject(int code, string message) => StatusCode(code, new ApiResponse<object> { Status = false, StatusCode = code, Message = message, Data = null });

        private IActionResult? Guard(out ReportCaller caller)
        {
            caller = null!;
            if (!User.CanUseReportInputs()) return this.Forbidden("You can't use marks entry.");
            if (!TryUser(out var userId)) return Unauthorized();
            caller = User.ToReportCaller(userId);
            return null;
        }

        // GET api/ReportMarks/context[?academicYearId=]   what this user may enter or review (one flat list; the screens build the pickers from it)
        [HttpGet("context")]
        public async Task<IActionResult> Context([FromQuery] long? academicYearId, CancellationToken ct)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            return Reply(await _service.ContextAsync(User.GetSchoolId(), caller, academicYearId, ct));
        }

        [HttpGet("grid")]
        public async Task<IActionResult> Grid([FromQuery] long? examSubjectId, [FromQuery] long? sectionId, CancellationToken ct)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            if (examSubjectId is null || sectionId is null) return Reject(400, "examSubjectId and sectionId are required.");
            return Reply(await _service.GetGridAsync(User.GetSchoolId(), caller, examSubjectId.Value, sectionId.Value, ct));
        }

        // PUT api/ReportMarks/grid   Body: { examSubjectId, sectionId, version, entries: [{ studentId, skillId?, state, marks?, gradeCode? }] }   partial rows allowed
        [HttpPut("grid")]
        public async Task<IActionResult> Save([FromBody] SaveGridRequest request, CancellationToken ct)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            return Reply(await _service.SaveDraftAsync(User.GetSchoolId(), caller, request, ct));
        }

        // PUT api/ReportMarks/remarks   Body: { examSubjectId, sectionId, remarks: [{ studentId, remark }] }   a blank remark clears it; not marks, so no version token and no accept step
        [HttpPut("remarks")]
        public async Task<IActionResult> SaveRemarks([FromBody] SaveRemarksRequest request, CancellationToken ct)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            return Reply(await _service.SaveRemarksAsync(User.GetSchoolId(), caller, request, ct));
        }

        [HttpPost("submit")]
        public async Task<IActionResult> Submit([FromBody] GridActionRequest request, CancellationToken ct)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            return Reply(await _service.SubmitAsync(User.GetSchoolId(), caller, request, ct));
        }

        [HttpPost("accept")]
        public async Task<IActionResult> Accept([FromBody] GridActionRequest request, CancellationToken ct)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            return Reply(await _service.AcceptAsync(User.GetSchoolId(), caller, request, ct));
        }

        // POST api/ReportMarks/reopen   Body: { examSubjectId, sectionId, version, reason }   the reason is required and audited
        [HttpPost("reopen")]
        public async Task<IActionResult> Reopen([FromBody] GridActionRequest request, CancellationToken ct)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            return Reply(await _service.ReopenAsync(User.GetSchoolId(), caller, request, ct));
        }

        [HttpGet("template")]
        public async Task<IActionResult> Template([FromQuery] long? examSubjectId, [FromQuery] long? sectionId, CancellationToken ct)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            if (examSubjectId is null || sectionId is null) return Reject(400, "examSubjectId and sectionId are required.");
            var file = await _service.TemplateAsync(User.GetSchoolId(), caller, examSubjectId.Value, sectionId.Value, ct);
            return file.Content == null ? Reject(file.StatusCode, file.Message) : File(file.Content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.FileName);
        }

        // POST api/ReportMarks/upload?examSubjectId=&sectionId=&version=&apply=   multipart field "file" (.xlsx, 1 MB)
        // apply=false (default): a dry run, nothing is saved, the screen fills its grid from the answer. apply=true: saves into the DRAFT grid. Never submits.
        [HttpPost("upload")]
        [RequestSizeLimit(3_000_000)]
        [RequestFormLimits(MultipartBodyLengthLimit = 3_000_000)]
        public async Task<IActionResult> Upload(IFormFile? file, [FromQuery] long? examSubjectId, [FromQuery] long? sectionId, [FromQuery] int version = 0, [FromQuery] bool apply = false, CancellationToken ct = default)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            if (examSubjectId is null || sectionId is null) return Reject(400, "examSubjectId and sectionId are required.");
            if (file == null || file.Length == 0) return Reject(400, "Choose an .xlsx file to upload.");
            if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase)) return Reject(400, "Only .xlsx files are accepted. Download the template, fill it in and save it as an Excel Workbook (.xlsx).");
            if (file.Length > ExcelFileGuard.MaxFileBytes) return Reject(400, "The file is larger than 1 MB.");
            using var ms = new MemoryStream((int)file.Length);
            await file.CopyToAsync(ms, ct);
            return Reply(await _service.UploadAsync(User.GetSchoolId(), caller, examSubjectId.Value, sectionId.Value, version, apply, ms.ToArray(), ct));
        }
    }

    // The class teacher's side. The Principal reads; the School Admin and the section's class teacher edit until the term is LOCKED; only a School Admin locks and unlocks.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ReportClassController : ControllerBase
    {
        private readonly IReportClassService _service;
        public ReportClassController(IReportClassService service) => _service = service;

        private bool TryUser(out long userId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
        private IActionResult Reject(int code, string message) => StatusCode(code, new ApiResponse<object> { Status = false, StatusCode = code, Message = message, Data = null });

        private IActionResult? Guard(out ReportCaller caller)
        {
            caller = null!;
            if (!User.CanUseReportInputs()) return this.Forbidden("You can't use the class report.");
            if (!TryUser(out var userId)) return Unauthorized();
            caller = User.ToReportCaller(userId);
            return null;
        }

        [HttpGet("completeness")]
        public async Task<IActionResult> Completeness([FromQuery] long? schemeId, [FromQuery] long? sectionId, CancellationToken ct)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            if (schemeId is null || sectionId is null) return Reject(400, "schemeId and sectionId are required.");
            return Reply(await _service.CompletenessAsync(User.GetSchoolId(), caller, schemeId.Value, sectionId.Value, ct));
        }

        [HttpGet("activity-grades")]
        public async Task<IActionResult> ActivityGrades([FromQuery] long? termId, [FromQuery] long? sectionId, CancellationToken ct)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            if (termId is null || sectionId is null) return Reject(400, "termId and sectionId are required.");
            return Reply(await _service.GetActivityGradesAsync(User.GetSchoolId(), caller, termId.Value, sectionId.Value, ct));
        }

        [HttpPut("activity-grades")]
        public async Task<IActionResult> SaveActivityGrades([FromBody] SaveActivityGradesRequest request, CancellationToken ct)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            return Reply(await _service.SaveActivityGradesAsync(User.GetSchoolId(), caller, request, ct));
        }

        [HttpGet("term-students")]
        public async Task<IActionResult> TermStudents([FromQuery] long? termId, [FromQuery] long? sectionId, CancellationToken ct)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            if (termId is null || sectionId is null) return Reject(400, "termId and sectionId are required.");
            return Reply(await _service.GetTermStudentsAsync(User.GetSchoolId(), caller, termId.Value, sectionId.Value, ct));
        }

        [HttpPut("term-students")]
        public async Task<IActionResult> SaveTermStudents([FromBody] SaveTermStudentsRequest request, CancellationToken ct)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            return Reply(await _service.SaveTermStudentsAsync(User.GetSchoolId(), caller, request, ct));
        }

        [HttpGet("year-students")]
        public async Task<IActionResult> YearStudents([FromQuery] long? schemeId, [FromQuery] long? sectionId, CancellationToken ct)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            if (schemeId is null || sectionId is null) return Reject(400, "schemeId and sectionId are required.");
            return Reply(await _service.GetYearStudentsAsync(User.GetSchoolId(), caller, schemeId.Value, sectionId.Value, ct));
        }

        [HttpPut("year-students")]
        public async Task<IActionResult> SaveYearStudents([FromBody] SaveYearStudentsRequest request, CancellationToken ct)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            return Reply(await _service.SaveYearStudentsAsync(User.GetSchoolId(), caller, request, ct));
        }

        // GET api/ReportClass/attendance?termId=&sectionId=   the term's attendance: AUTO from the Attendance module (shared day rules) or the typed MANUAL values
        [HttpGet("attendance")]
        public async Task<IActionResult> Attendance([FromQuery] long? termId, [FromQuery] long? sectionId, CancellationToken ct)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            if (termId is null || sectionId is null) return Reject(400, "termId and sectionId are required.");
            return Reply(await _service.AttendanceAsync(User.GetSchoolId(), caller, termId.Value, sectionId.Value, ct));
        }

        [HttpPost("lock")]
        public async Task<IActionResult> Lock([FromBody] LockRequest request, CancellationToken ct)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            return Reply(await _service.LockAsync(User.GetSchoolId(), caller, request, ct));
        }

        [HttpPost("unlock")]
        public async Task<IActionResult> Unlock([FromBody] LockRequest request, CancellationToken ct)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            return Reply(await _service.UnlockAsync(User.GetSchoolId(), caller, request, ct));
        }
    }
}
