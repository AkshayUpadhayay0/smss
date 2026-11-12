using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // The Accountant dashboard: one read-only response. Accountant only (not the School Admin, Principal, Teacher or Super Admin). The school always comes from the token,
    // so there is no id in the route. 401 anonymously (the [Authorize] filter).
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AccountantDashboardController : ControllerBase
    {
        private readonly IAccountantDashboardService _service;
        public AccountantDashboardController(IAccountantDashboardService service) => _service = service;

        // GET api/AccountantDashboard/summary
        [HttpGet("summary")]
        public async Task<IActionResult> Summary(CancellationToken ct)
        {
            if (!User.IsAccountant()) return this.Forbidden("Only the Accountant can see the Accountant dashboard.");
            var response = await _service.GetSummaryAsync(User.GetSchoolId(), ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
