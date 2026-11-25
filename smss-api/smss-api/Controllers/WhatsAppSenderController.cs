using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // A school's WhatsApp sender status (NOT_REGISTERED, PENDING, ACTIVE). No sending happens here and no credential is ever stored or returned.
    //   Super Admin: reads and changes any school's sender.
    //   School Admin, Principal: read their OWN school's status only (the school comes from the token; another school is a 404).
    //   Everyone else: 403.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class WhatsAppSenderController : ControllerBase
    {
        private readonly IWhatsAppSenderService _service;
        public WhatsAppSenderController(IWhatsAppSenderService service) => _service = service;

        private IActionResult Respond(int code, string message) =>
            StatusCode(code, new ApiResponse<object> { Status = false, StatusCode = code, Message = message, Data = null });

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

        private bool CanRead() => User.IsSuperAdmin() || User.IsSchoolAdmin() || User.IsPrincipal();

        // GET api/WhatsAppSender/me   (School Admin, Principal: their own school; the school comes from the token)
        [HttpGet("me")]
        public async Task<IActionResult> Mine(CancellationToken ct)
        {
            if (!(User.IsSchoolAdmin() || User.IsPrincipal())) return Respond(403, "Only a School Admin or a Principal can see their school's WhatsApp sender.");
            var school = User.GetSchoolId() ?? string.Empty;
            if (school.Length == 0) return Respond(400, "Your account is not linked to a school.");
            return Reply(await _service.GetAsync(school, school, false, ct));
        }

        // GET api/WhatsAppSender/schools/{schoolId}
        [HttpGet("schools/{schoolId}")]
        public async Task<IActionResult> Get(string schoolId, CancellationToken ct)
        {
            if (!CanRead()) return Respond(403, "You do not have access to WhatsApp sender settings.");
            return Reply(await _service.GetAsync(User.GetSchoolId(), schoolId, User.IsSuperAdmin(), ct));
        }

        // PUT api/WhatsAppSender/schools/{schoolId}   (Super Admin only)
        [HttpPut("schools/{schoolId}")]
        public async Task<IActionResult> Save(string schoolId, [FromBody] SaveSenderRequestDto request, CancellationToken ct)
        {
            if (!User.IsSuperAdmin()) return Respond(403, "Only the Super Admin can change a school's WhatsApp sender.");
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            return Reply(await _service.SaveAsync(schoolId, request, userId, ct));
        }
    }
}
