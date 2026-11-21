using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // Super Admin controls over how a school appears on the public site. The school id comes from the route only.
    // A POST, not a PATCH: PATCH is blocked in this project's dev environment (see CLAUDE.md).
    [ApiController]
    [Route("api/superadmin/schools")]
    [Authorize]
    [SuperAdminOnly]
    public class SuperAdminSchoolsController : ControllerBase
    {
        private readonly IPartnerSchoolService _service;
        public SuperAdminSchoolsController(IPartnerSchoolService service) => _service = service;

        // POST api/superadmin/schools/{schoolId}/website-visibility   Body: { show: true | false }
        [HttpPost("{schoolId}/website-visibility")]
        public async Task<IActionResult> SetWebsiteVisibility(string schoolId, [FromBody] SetWebsiteVisibilityRequest request, CancellationToken ct)
        {
            var response = await _service.SetVisibilityAsync(schoolId, request, ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
