using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // Push-notification device tokens (the mobile app). Any signed-in user of a school may register their own phone. The school and the user ALWAYS come
    // from the token: the body carries no school id or user id, so a client can never attach a token to someone else.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DeviceController : ControllerBase
    {
        private readonly IDeviceService _service;
        public DeviceController(IDeviceService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

        // POST api/Device/register   Body: { fcmToken, platform: "ANDROID"|"IOS", deviceLabel?, appVersion? }
        // Upsert: a token already known is re-assigned to the caller (a shared phone where another student signs in) and re-activated.
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDeviceRequest request, CancellationToken ct)
        {
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            return Reply(await _service.RegisterAsync(User.GetSchoolId(), userId, request, ct));
        }

        // POST api/Device/unregister   Body: { fcmToken }   (call before logout). Only a token the caller owns is deactivated.
        [HttpPost("unregister")]
        public async Task<IActionResult> Unregister([FromBody] UnregisterDeviceRequest request, CancellationToken ct)
        {
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            return Reply(await _service.UnregisterAsync(User.GetSchoolId(), userId, request, ct));
        }
    }
}
