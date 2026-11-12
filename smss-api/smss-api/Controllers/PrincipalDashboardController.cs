using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // The Principal dashboard: one read-only response composed from the existing services. Principal only (not the School Admin, Teacher, Accountant or Super Admin).
    // The school always comes from the token, so there is no id in the route. 401 anonymously (the [Authorize] filter).
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PrincipalDashboardController : ControllerBase
    {
        private readonly IPrincipalDashboardService _service;
        public PrincipalDashboardController(IPrincipalDashboardService service) => _service = service;

        // GET api/PrincipalDashboard/summary
        [HttpGet("summary")]
        public async Task<IActionResult> Summary(CancellationToken ct)
        {
            if (!User.IsPrincipal()) return this.Forbidden("Only the Principal can see the Principal dashboard.");
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            var response = await _service.GetSummaryAsync(User.GetSchoolId(), userId, ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
