using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // Transfer certificates (Stage 8b). School Admin and Principal issue, view, download and void; everyone else gets 403. The school comes from the token only; a student or certificate of
    // another school answers 404. The PDF is drawn on demand from the stored snapshot and sent only to a signed-in caller: it is never stored under a public address.
    [ApiController]
    [Route("api/TransferCertificates")]
    [Authorize]
    public class TransferCertificatesController : ControllerBase
    {
        private readonly ITransferCertificateService _service;
        public TransferCertificatesController(ITransferCertificateService service) => _service = service;

        private IActionResult Deny() => this.Forbidden("Only a School Admin or a Principal can handle transfer certificates.");
        private bool UserId(out long id) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out id);

        // GET api/TransferCertificates/check?studentId=ST1   can a certificate be issued, the dues, and what stops it (read-only)
        [HttpGet("check")]
        public async Task<IActionResult> Check([FromQuery] string? studentId, CancellationToken ct)
        {
            if (!User.CanPromoteStudents()) return Deny();
            var r = await _service.CheckAsync(User.GetSchoolId(), studentId, ct);
            return StatusCode(r.StatusCode, r);
        }

        // POST api/TransferCertificates/issue   body { studentId, conduct?, remarks?, confirm: true, overrideDues?, overrideReason? }
        [HttpPost("issue")]
        public async Task<IActionResult> Issue([FromBody] IssueTcRequest request, CancellationToken ct)
        {
            if (!User.CanPromoteStudents()) return Deny();
            if (!UserId(out var userId)) return Unauthorized();
            var r = await _service.IssueAsync(User.GetSchoolId(), userId, request, ct);
            return StatusCode(r.StatusCode, r);
        }

        // GET api/TransferCertificates?studentId=&status=ISSUED|VOID
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? studentId, [FromQuery] string? status, CancellationToken ct)
        {
            if (!User.CanPromoteStudents()) return Deny();
            var r = await _service.ListAsync(User.GetSchoolId(), studentId, status, ct);
            return StatusCode(r.StatusCode, r);
        }

        // GET api/TransferCertificates/5   one certificate with every print (who and when)
        [HttpGet("{id:long}")]
        public async Task<IActionResult> Get(long id, CancellationToken ct)
        {
            if (!User.CanPromoteStudents()) return Deny();
            var r = await _service.GetAsync(User.GetSchoolId(), id, ct);
            return StatusCode(r.StatusCode, r);
        }

        // GET api/TransferCertificates/5/pdf   the original, drawn from the stored snapshot (audited)
        [HttpGet("{id:long}/pdf")]
        public async Task<IActionResult> Original(long id, CancellationToken ct)
        {
            if (!User.CanPromoteStudents()) return Deny();
            if (!UserId(out var userId)) return Unauthorized();
            return FileOrError(await _service.OriginalPdfAsync(User.GetSchoolId(), userId, id, ct));
        }

        // POST api/TransferCertificates/5/duplicate   a duplicate: same number, marked "DUPLICATE COPY No. N" (audited). A POST because it writes the audit row.
        [HttpPost("{id:long}/duplicate")]
        public async Task<IActionResult> Duplicate(long id, CancellationToken ct)
        {
            if (!User.CanPromoteStudents()) return Deny();
            if (!UserId(out var userId)) return Unauthorized();
            return FileOrError(await _service.DuplicatePdfAsync(User.GetSchoolId(), userId, id, ct));
        }

        // POST api/TransferCertificates/5/void   body { reason (10+ chars), confirm: true }
        [HttpPost("{id:long}/void")]
        public async Task<IActionResult> Void(long id, [FromBody] VoidTcRequest request, CancellationToken ct)
        {
            if (!User.CanPromoteStudents()) return Deny();
            if (!UserId(out var userId)) return Unauthorized();
            var r = await _service.VoidAsync(User.GetSchoolId(), userId, id, request, ct);
            return StatusCode(r.StatusCode, r);
        }

        private IActionResult FileOrError(TcFileResult f)
        {
            if (f.Content == null) return StatusCode(f.StatusCode, new ApiResponse<object> { Status = false, StatusCode = f.StatusCode, Message = f.Message });
            Response.Headers["Cache-Control"] = "no-store";
            return File(f.Content, "application/pdf", f.FileName);
        }
    }
}
