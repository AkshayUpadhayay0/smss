using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // The parent's read-only family screens. NOTHING here takes a guardian id, a school id or a role from the request: the guardian is the one behind the token, the school is the token's, and a child id in the route
    // is always checked against the guardian's links (portal access on) by the one access helper, inside IFamilyService. An unlinked, switched-off, other-school or missing child is the same 404.
    // A Student login is refused (403). Every endpoint is a GET: parents cannot change anything yet.
    [ApiController]
    [Route("api/Family")]
    [Authorize]
    public class FamilyController : ControllerBase
    {
        private readonly IFamilyService _service;
        public FamilyController(IFamilyService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> r) => StatusCode(r.StatusCode, r);
        private IActionResult Refuse(AccessDecision d) => StatusCode(d.StatusCode, new ApiResponse<object> { Status = false, StatusCode = d.StatusCode, Message = d.Message });

        private AccessCaller? Caller()
        {
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return null;
            long? claim = long.TryParse(User.FindFirstValue("guardian_id"), out var g) ? g : null;
            return new AccessCaller(User.GetSchoolId(), userId, User.IsStudent(), User.IsGuardianLogin(), claim);
        }

        // GET api/Family/children   one card per linked child (portal access on) and the fee tile
        [HttpGet("children")]
        public async Task<IActionResult> Children(CancellationToken ct) => Caller() is { } c ? Reply(await _service.ListChildrenAsync(c, ct)) : Unauthorized();

        // GET api/Family/children/{studentId}/profile
        [HttpGet("children/{studentId:length(1,20)}/profile")]
        public async Task<IActionResult> Profile(string studentId, CancellationToken ct) => Caller() is { } c ? Reply(await _service.ProfileAsync(c, studentId, ct)) : Unauthorized();

        // GET api/Family/children/{studentId}/attendance?from=&to=   (at most 92 days; Present / Absent only)
        [HttpGet("children/{studentId:length(1,20)}/attendance")]
        public async Task<IActionResult> Attendance(string studentId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
            Caller() is { } c ? Reply(await _service.AttendanceAsync(c, studentId, from, to, ct)) : Unauthorized();

        // GET api/Family/children/{studentId}/homework?from=&to=
        [HttpGet("children/{studentId:length(1,20)}/homework")]
        public async Task<IActionResult> Homework(string studentId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
            Caller() is { } c ? Reply(await _service.HomeworkAsync(c, studentId, from, to, ct)) : Unauthorized();

        // GET api/Family/children/{studentId}/timetable
        [HttpGet("children/{studentId:length(1,20)}/timetable")]
        public async Task<IActionResult> Timetable(string studentId, CancellationToken ct) => Caller() is { } c ? Reply(await _service.TimetableAsync(c, studentId, ct)) : Unauthorized();

        // GET api/Family/children/{studentId}/exam-schedule   (only the schemes of the child's own class)
        [HttpGet("children/{studentId:length(1,20)}/exam-schedule")]
        public async Task<IActionResult> ExamSchedule(string studentId, CancellationToken ct) => Caller() is { } c ? Reply(await _service.ExamScheduleAsync(c, studentId, ct)) : Unauthorized();

        // GET api/Family/children/{studentId}/report-cards   PUBLISHED cards only
        [HttpGet("children/{studentId:length(1,20)}/report-cards")]
        public async Task<IActionResult> ReportCards(string studentId, CancellationToken ct) => Caller() is { } c ? Reply(await _service.ReportCardsAsync(c, studentId, ct)) : Unauthorized();

        [HttpGet("children/{studentId:length(1,20)}/report-cards/{publicationId:long}")]
        public async Task<IActionResult> ReportCard(string studentId, long publicationId, CancellationToken ct) => Caller() is { } c ? Reply(await _service.ReportCardAsync(c, studentId, publicationId, ct)) : Unauthorized();

        [HttpGet("children/{studentId:length(1,20)}/report-cards/{publicationId:long}/pdf")]
        public async Task<IActionResult> ReportCardPdf(string studentId, long publicationId, CancellationToken ct)
        {
            if (Caller() is not { } c) return Unauthorized();
            var (decision, file) = await _service.ReportCardPdfAsync(c, studentId, publicationId, ct);
            if (file == null) return Refuse(decision);
            return file.Content == null ? StatusCode(file.StatusCode, new ApiResponse<object> { Status = false, StatusCode = file.StatusCode, Message = file.Message }) : File(file.Content, "application/pdf", file.FileName);
        }

        // GET api/Family/children/{studentId}/fees   the own-view fees (APPLIED late fee lines; never a concession reason, a waived late fee or a voided payment)
        [HttpGet("children/{studentId:length(1,20)}/fees")]
        public async Task<IActionResult> Fees(string studentId, CancellationToken ct) => Caller() is { } c ? Reply(await _service.FeesAsync(c, studentId, ct)) : Unauthorized();

        // GET api/Family/children/{studentId}/receipts/{paymentId}   a receipt of THIS child's own Active payment (another child's is a 404)
        [HttpGet("children/{studentId:length(1,20)}/receipts/{paymentId:long}")]
        public async Task<IActionResult> Receipt(string studentId, long paymentId, CancellationToken ct) => Caller() is { } c ? Reply(await _service.ReceiptAsync(c, studentId, paymentId, ct)) : Unauthorized();

        // GET api/Family/children/{studentId}/leave?status=&from=&to=   read only
        [HttpGet("children/{studentId:length(1,20)}/leave")]
        public async Task<IActionResult> Leave(string studentId, [FromQuery] string? status, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
            Caller() is { } c ? Reply(await _service.LeaveAsync(c, studentId, status, from, to, ct)) : Unauthorized();

        // GET api/Family/children/{studentId}/photo   (from PRIVATE storage, through the same helper)
        [HttpGet("children/{studentId:length(1,20)}/photo")]
        public async Task<IActionResult> Photo(string studentId, CancellationToken ct)
        {
            if (Caller() is not { } c) return Unauthorized();
            var (decision, photo) = await _service.PhotoAsync(c, studentId, ct);
            if (photo == null) return Refuse(decision);
            if (photo.Content == null) return StatusCode(photo.StatusCode, new ApiResponse<object> { Status = false, StatusCode = photo.StatusCode, Message = photo.Message });
            Response.Headers.CacheControl = "private, no-cache";
            return File(photo.Content, photo.ContentType, lastModified: null, entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue(photo.ETag));
        }

        // GET api/Family/notices?unreadOnly=   the school's notices for the Guardian tier (not per child)
        [HttpGet("notices")]
        public async Task<IActionResult> Notices([FromQuery] bool unreadOnly, CancellationToken ct) => Caller() is { } c ? Reply(await _service.NoticesAsync(c, unreadOnly, ct)) : Unauthorized();
    }
}
