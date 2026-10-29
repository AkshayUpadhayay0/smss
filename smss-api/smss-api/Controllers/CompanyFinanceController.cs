using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // The company's financial overview (read-only aggregate). Super Admin only.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [SuperAdminOnly]
    public class CompanyFinanceController : ControllerBase
    {
        private readonly ICompanyFinanceService _service;
        public CompanyFinanceController(ICompanyFinanceService service) => _service = service;

        // GET api/CompanyFinance/overview?from=2026-04-01&to=2026-10-31   (defaults: 1 January of this year .. today; at most 60 months)
        [HttpGet("overview")]
        public async Task<IActionResult> Overview([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        {
            var response = await _service.GetOverviewAsync(from, to, ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
