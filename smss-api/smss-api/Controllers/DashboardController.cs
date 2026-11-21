using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // The redesigned School Admin dashboard. School Admin only; the school always comes from the token (there is no id anywhere in the request).
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DashboardController : ControllerBase
    {
        private readonly ISchoolAdminDashboardService _service;
        public DashboardController(ISchoolAdminDashboardService service) => _service = service;

        // GET api/Dashboard/school-admin
        [HttpGet("school-admin")]
        public async Task<IActionResult> SchoolAdmin(CancellationToken ct)
        {
            if (!User.IsSchoolAdmin()) return this.Forbidden("Only a School Admin can see the school dashboard.");
            var response = await _service.GetAsync(User.GetSchoolId(), ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
