using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // Stage 2: driver duty and live location.
    //  - me / start / end-with-login: the Driver login (JWT). The role check is the FIRST statement. Driver and school come from the token, never from a body.
    //  - location / end-with-token: authenticated ONLY by the scoped tracking token in the X-Duty-Token header (no JWT: the background task never refreshes a login).
    //    The header value is read here and handed to the service, which only ever hashes it. It is never logged, echoed or put in an error.
    //  - live: School Admin and Principal (CanViewTransport).

    [ApiController]
    [Authorize]
    public class DriverDutyController : ControllerBase
    {
        public const string TokenHeader = "X-Duty-Token";
        public const string RateLimitPolicy = "DriverLocation";

        private readonly IDriverDutyService _service;
        public DriverDutyController(IDriverDutyService service) => _service = service;
        private IActionResult Reply<T>(ApiResponse<T> r) => StatusCode(r.StatusCode, r);

        private bool TryUserId(out long userId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
        private string? TrackingToken() => Request.Headers.TryGetValue(TokenHeader, out var v) && v.Count == 1 ? v[0] : null;

        // GET api/Drivers/me   (the literal route wins over api/Drivers/{driverId})
        [HttpGet("~/api/Drivers/me")]
        public async Task<IActionResult> GetMe(CancellationToken ct)
        {
            if (!User.IsDriver()) return this.Forbidden("Only a Driver can view their own duty details.");
            if (!TryUserId(out var userId)) return Unauthorized();
            return Reply(await _service.GetMeAsync(User.GetSchoolId(), userId, ct));
        }

        // POST api/DriverDuty/start   201 new session / 200 resumed. Returns the tracking token ONCE; a resume issues a new token and the old one stops working.
        [HttpPost("~/api/DriverDuty/start")]
        public async Task<IActionResult> Start(CancellationToken ct)
        {
            if (!User.IsDriver()) return this.Forbidden("Only a Driver can start duty.");
            if (!TryUserId(out var userId)) return Unauthorized();
            return Reply(await _service.StartAsync(User.GetSchoolId(), userId, ct));
        }

        // POST api/DriverDuty/location   header X-Duty-Token. 200 accepted / dropped (Accepted=false), 400 impossible values, 401 unknown token,
        // 409 SESSION_ENDED | SESSION_EXPIRED | DRIVER_DEACTIVATED (stop), 429 TOO_SOON (retry on the next ping, Retry-After header).
        [AllowAnonymous]
        [EnableRateLimiting(RateLimitPolicy)]
        [HttpPost("~/api/DriverDuty/location")]
        public async Task<IActionResult> Location([FromBody] DutyLocationRequest? request, CancellationToken ct)
        {
            var response = await _service.RecordLocationAsync(TrackingToken(), request, ct);
            if (response.StatusCode == 429 && response.Data?.RetryAfterSeconds is { } wait) Response.Headers.RetryAfter = wait.ToString();
            Response.Headers.CacheControl = "no-store";
            return Reply(response);
        }

        // POST api/DriverDuty/end   either the tracking token header or the driver's login. Idempotent: ending twice is a 200.
        [AllowAnonymous]
        [EnableRateLimiting(RateLimitPolicy)]
        [HttpPost("~/api/DriverDuty/end")]
        public async Task<IActionResult> End(CancellationToken ct)
        {
            Response.Headers.CacheControl = "no-store";
            if (TrackingToken() is { } token) return Reply(await _service.EndByTokenAsync(token, ct));
            if (User.Identity?.IsAuthenticated != true) return Unauthorized();
            if (!User.IsDriver()) return this.Forbidden("Only a Driver can end duty.");
            if (!TryUserId(out var userId)) return Unauthorized();
            return Reply(await _service.EndForDriverAsync(User.GetSchoolId(), userId, ct));
        }
    }

    [ApiController]
    [Route("api/Transport")]
    [Authorize]
    public class TransportLiveController : ControllerBase
    {
        private readonly IDriverDutyService _service;
        public TransportLiveController(IDriverDutyService service) => _service = service;

        // GET api/Transport/live?areaId=&vehicleId=   School Admin and Principal: every open duty session of THEIR school with the latest fix.
        [HttpGet("live")]
        public async Task<IActionResult> Live([FromQuery] long? areaId, [FromQuery] long? vehicleId, CancellationToken ct)
        {
            if (!User.CanViewTransport()) return this.Forbidden("Only a School Admin or a Principal can view the live map.");
            Response.Headers.CacheControl = "no-store";
            var r = await _service.GetLiveAsync(User.GetSchoolId(), areaId, vehicleId, ct);
            return StatusCode(r.StatusCode, r);
        }
    }
}
