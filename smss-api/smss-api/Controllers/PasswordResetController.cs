using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // Admin-initiated password reset. Super Admin -> a School Admin; School Admin -> any login of their OWN school. Everything else is refused by the service.
    // The caller (user id, school, roles) comes from the token; the body carries only the target's login id and a reason.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PasswordResetController : ControllerBase
    {
        private readonly IPasswordResetService _service;
        public PasswordResetController(IPasswordResetService service) => _service = service;

        // POST api/PasswordReset   Body: { loginId, reason }  ->  { loginId, displayName, temporaryPassword }  (the password is shown ONCE)
        [HttpPost]
        public async Task<IActionResult> Reset([FromBody] ResetPasswordRequest request, CancellationToken ct)
        {
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
            var response = await _service.ResetAsync(new PasswordResetCaller(userId, User.GetSchoolId(), roles), request, ct);
            Response.Headers["Cache-Control"] = "private, no-store";   // the body holds a password
            return StatusCode(response.StatusCode, response);
        }
    }
}
