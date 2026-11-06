using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // Examination / Report Card, STAGE 3: results and publishing. School Admin publishes; the Principal reads and downloads PUBLISHED cards; the section's class teacher previews and
    // downloads for their own section. The school and the acting user come from the token; a Student has their own endpoints under api/Student/me/report-cards.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ReportResultController : ControllerBase
    {
        private readonly IReportResultService _service;
        public ReportResultController(IReportResultService service) => _service = service;

        private bool TryUser(out long userId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
        private IActionResult Reject(int code, string message) => StatusCode(code, new ApiResponse<object> { Status = false, StatusCode = code, Message = message, Data = null });
        private IActionResult PdfOrError(ReportFileResult file) => file.Content == null ? Reject(file.StatusCode, file.Message) : File(file.Content, "application/pdf", file.FileName);

        private IActionResult? Guard(out ReportCaller caller)
        {
            caller = null!;
            if (!User.CanUseReportInputs()) return this.Forbidden("You can't use report card results.");
            if (!TryUser(out var userId)) return Unauthorized();
            caller = User.ToReportCaller(userId);
            return null;
        }

        // GET api/ReportResult/preview?schemeId=&sectionId=&scope=TERM|YEAR&termId=
        [HttpGet("preview")]
        public async Task<IActionResult> Preview([FromQuery] long? schemeId, [FromQuery] long? sectionId, [FromQuery] string? scope, [FromQuery] long? termId, CancellationToken ct)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            if (schemeId is null || sectionId is null || string.IsNullOrWhiteSpace(scope)) return Reject(400, "schemeId, sectionId and scope are required.");
            return Reply(await _service.PreviewAsync(User.GetSchoolId(), caller, schemeId.Value, sectionId.Value, scope.Trim().ToUpperInvariant(), termId, ct));
        }

        // POST api/ReportResult/publish   Body: { schemeId, sectionId, scope, termId?, note?, confirmExcluded, acknowledgeAttendance }
        [HttpPost("publish")]
        public async Task<IActionResult> Publish([FromBody] PublishRequest request, CancellationToken ct)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            request.Scope = (request.Scope ?? "").Trim().ToUpperInvariant();
            return Reply(await _service.PublishAsync(User.GetSchoolId(), caller, request, ct));
        }

        // GET api/ReportResult/publications[?sectionId=&schemeId=]   every version, newest first
        [HttpGet("publications")]
        public async Task<IActionResult> List([FromQuery] long? sectionId, [FromQuery] long? schemeId, CancellationToken ct)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            return Reply(await _service.ListAsync(User.GetSchoolId(), caller, sectionId, schemeId, ct));
        }

        [HttpGet("publications/{publicationId:long}")]
        public async Task<IActionResult> Get(long publicationId, CancellationToken ct)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            return Reply(await _service.GetAsync(User.GetSchoolId(), caller, publicationId, ct));
        }

        // GET api/ReportResult/publications/{id}/cards/{studentId}/pdf   a published card, re-drawn from what was stored
        [HttpGet("publications/{publicationId:long}/cards/{studentId}/pdf")]
        public async Task<IActionResult> CardPdf(long publicationId, string studentId, CancellationToken ct)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            return PdfOrError(await _service.CardPdfAsync(User.GetSchoolId(), caller, publicationId, studentId, ct));
        }

        // GET api/ReportResult/draft-pdf?schemeId=&sectionId=&scope=&termId=&studentId=   watermarked DRAFT from live data (School Admin and the class teacher)
        [HttpGet("draft-pdf")]
        public async Task<IActionResult> DraftPdf([FromQuery] long? schemeId, [FromQuery] long? sectionId, [FromQuery] string? scope, [FromQuery] long? termId, [FromQuery] string? studentId, CancellationToken ct)
        {
            var denied = Guard(out var caller); if (denied != null) return denied;
            if (schemeId is null || sectionId is null || string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(studentId)) return Reject(400, "schemeId, sectionId, scope and studentId are required.");
            return PdfOrError(await _service.DraftPdfAsync(User.GetSchoolId(), caller, schemeId.Value, sectionId.Value, scope.Trim().ToUpperInvariant(), termId, studentId, ct));
        }
    }

    // A Student login's OWN published report cards. Nothing in the request selects the student: school and student come from the token. Drafts and superseded versions are never visible.
    [ApiController]
    [Route("api/Student/me/report-cards")]
    [Authorize]
    public class StudentReportCardController : ControllerBase
    {
        private readonly IReportResultService _service;
        public StudentReportCardController(IReportResultService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct)
        {
            if (!User.IsStudent()) return this.Forbidden("Only a Student can see their own report cards here.");
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            return Reply(await _service.MyCardsAsync(User.GetSchoolId(), userId, ct));
        }

        [HttpGet("{publicationId:long}")]
        public async Task<IActionResult> Get(long publicationId, CancellationToken ct)
        {
            if (!User.IsStudent()) return this.Forbidden("Only a Student can see their own report cards here.");
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            return Reply(await _service.MyCardAsync(User.GetSchoolId(), userId, publicationId, ct));
        }

        [HttpGet("{publicationId:long}/pdf")]
        public async Task<IActionResult> Pdf(long publicationId, CancellationToken ct)
        {
            if (!User.IsStudent()) return this.Forbidden("Only a Student can download their own report card here.");
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            var file = await _service.MyCardPdfAsync(User.GetSchoolId(), userId, publicationId, ct);
            return file.Content == null ? StatusCode(file.StatusCode, new ApiResponse<object> { Status = false, StatusCode = file.StatusCode, Message = file.Message, Data = null }) : File(file.Content, "application/pdf", file.FileName);
        }
    }
}
