using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // Report card settings (colours, header, recognition, UDISE, footer, rules, signature, cover and rules pages), the design chosen per scheme, and the live preview.
    // School Admin writes. The Principal reads (settings, designs and a preview with sample data). Everyone else is refused. The school always comes from the token.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ReportCardSettingsController : ControllerBase
    {
        private readonly IReportCardSettingsService _service;
        public ReportCardSettingsController(IReportCardSettingsService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
        private IActionResult? CannotWrite() => User.CanConfigureReportCards() ? null : this.Forbidden("Only a School Admin can change the report card settings.");
        private IActionResult? CannotRead() => User.CanViewReportSetup() ? null : this.Forbidden("You can't see the report card settings.");
        private bool TryUser(out long userId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

        // GET api/ReportCardSettings   settings (or the defaults), the designs, and the schemes with their design
        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken ct) => CannotRead() ?? Reply(await _service.GetAsync(User.GetSchoolId(), ct));

        [HttpPut]
        public async Task<IActionResult> Save([FromBody] ReportCardSettingsRequest request, CancellationToken ct)
        {
            if (CannotWrite() is { } refused) return refused;
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _service.SaveAsync(User.GetSchoolId(), userId, request, ct));
        }

        // POST api/ReportCardSettings/signature   multipart field "file" (PNG / JPG / WEBP, 2 MB). Every upload is a NEW file; old published cards keep theirs.
        [HttpPost("signature")]
        [RequestSizeLimit(LogoRules.MaxRequestBytes)]
        public async Task<IActionResult> UploadSignature(IFormFile? file, CancellationToken ct)
        {
            if (CannotWrite() is { } refused) return refused;
            if (!TryUser(out var userId)) return Unauthorized();
            if (file == null) return StatusCode(400, new ApiResponse<object> { Status = false, StatusCode = 400, Message = "Signature file is required.", Data = null });
            await using var stream = file.OpenReadStream();
            return Reply(await _service.UploadSignatureAsync(User.GetSchoolId(), userId, stream, file.FileName, file.Length, ct));
        }

        [HttpPost("signature/remove")]
        public async Task<IActionResult> RemoveSignature(CancellationToken ct)
        {
            if (CannotWrite() is { } refused) return refused;
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _service.RemoveSignatureAsync(User.GetSchoolId(), userId, ct));
        }

        // PUT api/ReportCardSettings/scheme-design   Body: { schemeId, templateKey }   only a built design that suits the scheme's mode
        [HttpPut("scheme-design")]
        public async Task<IActionResult> SetSchemeDesign([FromBody] SetSchemeDesignRequest request, CancellationToken ct)
        {
            if (CannotWrite() is { } refused) return refused;
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _service.SetSchemeDesignAsync(User.GetSchoolId(), userId, request, ct));
        }

        // POST api/ReportCardSettings/preview   Body: { templateKey, settings?, terms?, schemeId?, sectionId?, scope?, termId?, studentId? }   PNG pages (base64) of a card drawn with the settings on screen
        [HttpPost("preview")]
        public async Task<IActionResult> Preview([FromBody] ReportCardPreviewRequest request, CancellationToken ct)
        {
            if (!User.CanUseReportInputs()) return this.Forbidden("You can't preview report cards.");
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _service.PreviewAsync(User.GetSchoolId(), User.ToReportCaller(userId), request, ct));
        }
    }
}
