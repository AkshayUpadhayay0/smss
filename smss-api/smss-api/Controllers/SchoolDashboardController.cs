using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // The School Admin dashboard: one read-only response composed from the existing services. School Admin only for now (not the Principal, Teacher or Accountant,
    // not even the Super Admin). The school always comes from the token, so there is no id in the route.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SchoolDashboardController : ControllerBase
    {
        private readonly ISchoolDashboardService _service;
        public SchoolDashboardController(ISchoolDashboardService service) => _service = service;

        // GET api/SchoolDashboard/summary
        [HttpGet("summary")]
        public async Task<IActionResult> Summary(CancellationToken ct)
        {
            if (!User.IsSchoolAdmin()) return this.Forbidden("Only a School Admin can see the school dashboard.");
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            var response = await _service.GetSummaryAsync(User.GetSchoolId(), userId, ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
