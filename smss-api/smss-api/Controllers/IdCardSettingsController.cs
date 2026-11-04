using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // A school's student ID-card template and branding. School Admin only. The school always comes from the token (no school id is accepted), so
    // one school can never read or change another's settings. [Authorize(Roles)] is switched off project-wide, so the role is checked here
    // from the token (IsSchoolAdmin), like Student ID Card, Employee and Class Teacher.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class IdCardSettingsController : ControllerBase
    {
        private readonly IIdCardSettingsService _service;
        private readonly IStudentIdCardService _cards;
        public IdCardSettingsController(IIdCardSettingsService service, IStudentIdCardService cards)
        {
            _service = service;
            _cards = cards;
        }

        private IActionResult? Refuse() =>
            User.IsSchoolAdmin() ? null : this.Forbidden("Only a School Admin can manage ID card settings.");

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

        // GET api/IdCardSettings   (the saved settings, or the defaults when none were saved; also lists the built-in templates)
        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken ct) =>
            Refuse() ?? Reply(await _service.GetAsync(User.GetSchoolId(), ct));

        // PUT api/IdCardSettings
        [HttpPut]
        public async Task<IActionResult> Save([FromBody] IdCardSettingsRequest request, CancellationToken ct)
        {
            if (Refuse() is { } refused) return refused;
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            return Reply(await _service.SaveAsync(User.GetSchoolId(), userId, request, ct));
        }

        // POST api/IdCardSettings/signature   (multipart/form-data, field name: file; stored in PRIVATE storage, never served by URL)
        [HttpPost("signature")]
        [RequestSizeLimit(LogoRules.MaxRequestBytes)]
        public async Task<IActionResult> UploadSignature(IFormFile? file, CancellationToken ct)
        {
            if (Refuse() is { } refused) return refused;
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            if (file == null) return StatusCode(400, new ApiResponse<object> { Status = false, StatusCode = 400, Message = "Signature file is required.", Data = null });
            await using var stream = file.OpenReadStream();
            return Reply(await _service.UploadSignatureAsync(User.GetSchoolId(), userId, stream, file.FileName, file.Length, ct));
        }

        // POST api/IdCardSettings/signature/remove
        [HttpPost("signature/remove")]
        public async Task<IActionResult> RemoveSignature(CancellationToken ct)
        {
            if (Refuse() is { } refused) return refused;
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            return Reply(await _service.RemoveSignatureAsync(User.GetSchoolId(), userId, ct));
        }

        // POST api/IdCardSettings/preview   (renders a template front + back to PNG with the screen's current, possibly unsaved, choices; changes nothing)
        [HttpPost("preview")]
        public async Task<IActionResult> Preview([FromBody] IdCardPreviewRequest request, CancellationToken ct) =>
            Refuse() ?? Reply(await _cards.PreviewAsync(User.GetSchoolId(), request, ct));
    }
}
