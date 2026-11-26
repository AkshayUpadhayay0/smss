using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // The PUBLIC parent / student account endpoints (no sign-in: the one-time link is the credential). Every one takes its secret in the request BODY, never in a URL or a query string, is rate limited per client
    // address (policy "ParentAuth", appsettings RateLimiting:ParentAuth) and answers in the standard envelope. Thin: the rules are in ParentAccountService.
    // Signing in itself is the ordinary POST api/Auth/login with the generated guardian login id.
    [ApiController]
    [Route("api/parent/auth")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicy)]
    public class ParentAuthController : ControllerBase
    {
        public const string RateLimitPolicy = "ParentAuth";
        private readonly IParentAccountService _service;
        public ParentAuthController(IParentAccountService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> r)
        {
            Response.Headers.CacheControl = "no-store";
            return StatusCode(r.StatusCode, r);
        }

        // POST api/parent/auth/check-link   { token }  -> { valid }   (does not use the link)
        [HttpPost("check-link")]
        public async Task<IActionResult> CheckLink([FromBody] CheckLinkRequest request, CancellationToken ct) => Reply(await _service.CheckLinkAsync(request.Token, ct));

        // POST api/parent/auth/set-password   { token, newPassword }   (a set-password OR a reset link; also the link of a student login)
        [HttpPost("set-password")]
        public async Task<IActionResult> SetPassword([FromBody] SetParentPasswordRequest request, CancellationToken ct) => Reply(await _service.SetPasswordAsync(request, ct));

        // POST api/parent/auth/forgot   { contact }   ALWAYS the same 200 answer
        [HttpPost("forgot")]
        public async Task<IActionResult> Forgot([FromBody] ForgotLoginRequest request, CancellationToken ct)
        {
            var client = ClientAddress.PartitionKey(HttpContext.Connection.RemoteIpAddress);
            return Reply(await _service.ForgotAsync(request, client, ct));
        }
    }
}
