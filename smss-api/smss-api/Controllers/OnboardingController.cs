using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // The first-login setup checklist, derived from existing data. The school always comes from the token. School Admin, Principal and Accountant may read it.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class OnboardingController : ControllerBase
    {
        private readonly IOnboardingService _service;
        public OnboardingController(IOnboardingService service) => _service = service;

        // GET api/Onboarding/status
        [HttpGet("status")]
        public async Task<IActionResult> Status(CancellationToken ct)
        {
            if (!(User.IsSchoolAdmin() || User.IsPrincipal() || User.IsAccountant())) return this.Forbidden("Only a School Admin, Principal or Accountant can see the setup status.");
            long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
            var response = await _service.GetStatusAsync(User.GetSchoolId(), userId, ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
