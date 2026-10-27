using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // The Student dashboard: one read-only response for the signed-in student, composed from the existing services. Student role only; the school and the
    // student always come from the token, so there is no id in the route.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class StudentDashboardController : ControllerBase
    {
        private readonly IStudentDashboardService _service;
        public StudentDashboardController(IStudentDashboardService service) => _service = service;

        // GET api/StudentDashboard
        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            if (!User.IsStudent()) return this.Forbidden("Only a student can see the student dashboard.");
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            var response = await _service.GetAsync(User.GetSchoolId(), userId, ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
