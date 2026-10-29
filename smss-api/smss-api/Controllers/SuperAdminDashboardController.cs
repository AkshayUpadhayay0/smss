using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // Read-only numbers behind the Super Admin dashboard (school management + subscription and payments). Super Admin only.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [SuperAdminOnly]
    public class SuperAdminDashboardController : ControllerBase
    {
        private readonly ISuperAdminDashboardService _service;
        public SuperAdminDashboardController(ISuperAdminDashboardService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

        // GET api/SuperAdminDashboard/summary?from=yyyy-MM-dd&to=yyyy-MM-dd   (default: the current month to date)
        [HttpGet("summary")]
        public async Task<IActionResult> Summary([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
            Reply(await _service.GetSummaryAsync(from, to, ct));

        // GET api/SuperAdminDashboard/schools?search=&status=TRIAL|ACTIVE|OVERDUE|CANCELLED|NOT_SET&page=1&pageSize=10
        [HttpGet("schools")]
        public async Task<IActionResult> Schools([FromQuery] string? search, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default) =>
            Reply(await _service.ListSchoolsAsync(search, status, page, pageSize, ct));
    }
}
