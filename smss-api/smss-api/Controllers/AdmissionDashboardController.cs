using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // The Admission Staff dashboard: one read-only response. Admission Staff only (not the School Admin, Principal, Accountant, Teacher or Super Admin). The school always comes
    // from the token, so there is no id in the route. 401 anonymously (the [Authorize] filter).
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AdmissionDashboardController : ControllerBase
    {
        private readonly IAdmissionDashboardService _service;
        public AdmissionDashboardController(IAdmissionDashboardService service) => _service = service;

        // GET api/AdmissionDashboard/summary
        [HttpGet("summary")]
        public async Task<IActionResult> Summary(CancellationToken ct)
        {
            if (!User.IsAdmissionStaff()) return this.Forbidden("Only Admission Staff can see the Admission dashboard.");
            var response = await _service.GetSummaryAsync(User.GetSchoolId(), ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
