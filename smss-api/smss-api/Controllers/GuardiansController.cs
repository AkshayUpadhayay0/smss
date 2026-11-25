using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // Parents' contact details: one guardian, the "Complete parent details" list and the bulk "use mobile number as WhatsApp" action. The school ALWAYS comes from the token, never from the request, so
    // another school's guardian is a 404. School Admin, Principal and Admission Staff (CanManageApplications); every other role is refused (403).
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class GuardiansController : ControllerBase
    {
        private readonly IGuardianService _service;
        private readonly IGuardianMergeService _merge;
        public GuardiansController(IGuardianService service, IGuardianMergeService merge) { _service = service; _merge = merge; }

        private const string Refusal = "Only a School Admin, a Principal or Admission Staff can work with parents' contact details.";

        private IActionResult Respond(int code, string message) =>
            StatusCode(code, new ApiResponse<object> { Status = false, StatusCode = code, Message = message, Data = null });

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

        private bool TryGetUserId(out long userId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

        private IActionResult? Gate(out string schoolId)
        {
            schoolId = string.Empty;
            if (!User.CanManageApplications()) return Respond(403, Refusal);
            schoolId = User.GetSchoolId() ?? string.Empty;
            return schoolId.Length == 0 ? Respond(400, TenantMessages.NoSchool) : null;
        }

        private const string MergeRefusal = "Only a School Admin or a Principal can look for duplicate parents and merge them.";

        /// <summary>The duplicate report and the merge are for the School Admin and the Principal only (Admission Staff may edit parents' details but never merge them).</summary>
        private IActionResult? MergeGate(out string schoolId)
        {
            schoolId = string.Empty;
            if (!(User.IsSchoolAdmin() || User.IsPrincipal())) return Respond(403, MergeRefusal);
            schoolId = User.GetSchoolId() ?? string.Empty;
            return schoolId.Length == 0 ? Respond(400, TenantMessages.NoSchool) : null;
        }

        // GET api/Guardians/duplicates   (active guardians that share a phone number, grouped, with a name-similarity flag and their students)
        [HttpGet("duplicates")]
        public async Task<IActionResult> Duplicates(CancellationToken ct)
        {
            if (MergeGate(out var school) is { } refused) return refused;
            return Reply(await _merge.ListDuplicatesAsync(school, ct));
        }

        // POST api/Guardians/merge/preview   Body: { survivorId, absorbedId }   (changes nothing)
        [HttpPost("merge/preview")]
        public async Task<IActionResult> MergePreview([FromBody] MergeRequestDto request, CancellationToken ct)
        {
            if (MergeGate(out var school) is { } refused) return refused;
            return Reply(await _merge.PreviewAsync(school, request, ct));
        }

        // POST api/Guardians/merge   Body: { survivorId, absorbedId, confirm: true, reason? }
        [HttpPost("merge")]
        public async Task<IActionResult> Merge([FromBody] MergeRequestDto request, CancellationToken ct)
        {
            if (MergeGate(out var school) is { } refused) return refused;
            if (!TryGetUserId(out var userId)) return Unauthorized();
            return Reply(await _merge.MergeAsync(school, request, userId, ct));
        }

        // GET api/Guardians/{guardianId}
        [HttpGet("{guardianId:long}")]
        public async Task<IActionResult> Get(long guardianId, CancellationToken ct)
        {
            if (Gate(out var school) is { } refused) return refused;
            return Reply(await _service.GetAsync(school, guardianId, ct));
        }

        // PUT api/Guardians/{guardianId}
        [HttpPut("{guardianId:long}")]
        public async Task<IActionResult> Update(long guardianId, [FromBody] GuardianUpdateRequestDto request, CancellationToken ct)
        {
            if (Gate(out var school) is { } refused) return refused;
            if (!TryGetUserId(out var userId)) return Unauthorized();
            return Reply(await _service.UpdateAsync(school, guardianId, request, userId, ct));
        }

        // GET api/Guardians/siblings?classId=&search=&limit=   (the sibling pick: students with their guardians; mobile numbers are masked)
        [HttpGet("siblings")]
        public async Task<IActionResult> Siblings([FromQuery] long? classId, [FromQuery] string? search, [FromQuery] int limit = 20, CancellationToken ct = default)
        {
            if (Gate(out var school) is { } refused) return refused;
            return Reply(await _service.SearchSiblingsAsync(school, classId, search, limit, ct));
        }

        // GET api/Guardians/incomplete?classId=&sectionId=&search=&page=&pageSize=
        [HttpGet("incomplete")]
        public async Task<IActionResult> Incomplete([FromQuery] long? classId, [FromQuery] long? sectionId, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
        {
            if (Gate(out var school) is { } refused) return refused;
            return Reply(await _service.ListIncompleteAsync(school, classId, sectionId, search, page, pageSize, ct));
        }

        // GET api/Guardians/incomplete/count   (the dashboard tiles)
        [HttpGet("incomplete/count")]
        public async Task<IActionResult> IncompleteCount(CancellationToken ct)
        {
            if (Gate(out var school) is { } refused) return refused;
            return Reply(await _service.CountIncompleteAsync(school, ct));
        }

        // POST api/Guardians/bulk-whatsapp   Body: { guardianIds: [..] }   (a POST: PATCH is blocked in this environment)
        [HttpPost("bulk-whatsapp")]
        public async Task<IActionResult> BulkWhatsApp([FromBody] BulkWhatsAppRequestDto request, CancellationToken ct)
        {
            if (Gate(out var school) is { } refused) return refused;
            if (!TryGetUserId(out var userId)) return Unauthorized();
            return Reply(await _service.UseMobileAsWhatsAppAsync(school, request.GuardianIds, userId, ct));
        }
    }
}
