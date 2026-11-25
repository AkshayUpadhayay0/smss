using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // "Assign fees for a year": gives enrolled students who have no fee structure the active structure of their class. School Admin only (CanConfigureFees); the school comes from the token only,
    // a year of another school answers 404.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class FeeRepairController : ControllerBase
    {
        private readonly IFeeRepairService _service;
        public FeeRepairController(IFeeRepairService service) => _service = service;

        // GET api/FeeRepair/years/9/missing   (the count and the classes that have no fee structure; nothing is saved)
        [HttpGet("years/{yearId:long}/missing")]
        public async Task<IActionResult> Missing(long yearId, CancellationToken ct)
        {
            if (!User.CanConfigureFees()) return this.Forbidden("Only a School Admin can assign fees for a year.");
            var response = await _service.PreviewAsync(User.GetSchoolId(), yearId, ct);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/FeeRepair/years/9/assign   (a POST, like every action on this project)
        [HttpPost("years/{yearId:long}/assign")]
        public async Task<IActionResult> Assign(long yearId, [FromBody] smss_api_service_layer.dto.FeeRepairRequest? request, CancellationToken ct)
        {
            if (!User.CanConfigureFees()) return this.Forbidden("Only a School Admin can assign fees for a year.");
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            var response = await _service.RunAsync(User.GetSchoolId(), userId, yearId, request, ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
