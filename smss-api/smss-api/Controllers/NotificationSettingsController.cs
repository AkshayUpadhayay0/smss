using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // A school's push-notification settings and the fee-reminder dry run. School Admin only (IsSchoolAdmin, from the token). The school always comes from the
    // token, so one school can never read or change another's settings.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class NotificationSettingsController : ControllerBase
    {
        private readonly INotificationSettingsService _service;
        private readonly IFeeReminderService _reminders;
        public NotificationSettingsController(INotificationSettingsService service, IFeeReminderService reminders)
        {
            _service = service;
            _reminders = reminders;
        }

        private IActionResult? Refuse() =>
            User.IsSchoolAdmin() ? null : this.Forbidden("Only a School Admin can manage notification settings.");

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

        // GET api/NotificationSettings   (the saved settings, or the defaults: reminders OFF, 09:00, 0 grace days)
        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken ct) =>
            Refuse() ?? Reply(await _service.GetAsync(User.GetSchoolId(), ct));

        // PUT api/NotificationSettings   Body: { feeReminderEnabled, feeReminderSendTime: "09:00", feeReminderGraceDays: 0..30 }
        [HttpPut]
        public async Task<IActionResult> Save([FromBody] NotificationSettingsRequest request, CancellationToken ct)
        {
            if (Refuse() is { } refused) return refused;
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            return Reply(await _service.SaveAsync(User.GetSchoolId(), userId, request, ct));
        }

        // GET api/NotificationSettings/fee-reminders/preview   (dry run: how many students would be reminded today and how many have a device; sends and writes nothing)
        [HttpGet("fee-reminders/preview")]
        public async Task<IActionResult> Preview(CancellationToken ct) =>
            Refuse() ?? Reply(await _reminders.PreviewAsync(User.GetSchoolId(), ct));
    }
}
