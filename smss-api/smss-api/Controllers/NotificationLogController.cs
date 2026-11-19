using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // The audit of pushes sent: did this student get the reminder. School Admin only; the school comes from the token.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class NotificationLogController : ControllerBase
    {
        private readonly INotificationLogService _service;
        public NotificationLogController(INotificationLogService service) => _service = service;

        // GET api/NotificationLog?studentId=&from=2026-11-01&to=2026-11-19   (default: the last 7 days; at most 92 days and 500 rows; a student of another school answers 404)
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? studentId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        {
            if (!User.IsSchoolAdmin()) return this.Forbidden("Only a School Admin can see the notification log.");
            var response = await _service.ListAsync(User.GetSchoolId(), studentId, from, to, ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
