using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // The Teacher dashboard: one read-only response for the signed-in teacher, composed from the existing services. Teacher role only; the school and the
    // employee always come from the token, so there is no id in the route.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class TeacherDashboardController : ControllerBase
    {
        private readonly ITeacherDashboardService _service;
        public TeacherDashboardController(ITeacherDashboardService service) => _service = service;

        // GET api/TeacherDashboard
        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            if (!User.IsTeacher()) return this.Forbidden("Only a teacher can see the teacher dashboard.");
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            var response = await _service.GetAsync(User.GetSchoolId(), userId, ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
