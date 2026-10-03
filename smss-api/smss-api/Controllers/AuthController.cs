using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        // POST api/Auth/login
        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var response = await _authService.LoginAsync(request);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/Auth/refresh  (the access token is expired by definition, so this is anonymous;
        // the refresh token itself is the credential)
        [AllowAnonymous]
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request)
        {
            var response = await _authService.RefreshAsync(request);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/Auth/logout
        [AllowAnonymous]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request)
        {
            var response = await _authService.LogoutAsync(request);
            return StatusCode(response.StatusCode, response);
        }

        // GET api/Auth/me
        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            if (!TryGetUserId(out var userId)) return Unauthorized();
            var response = await _authService.GetCurrentUserAsync(userId);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/Auth/change-password
        [Authorize]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            if (!TryGetUserId(out var userId)) return Unauthorized();
            var response = await _authService.ChangePasswordAsync(userId, request);
            return StatusCode(response.StatusCode, response);
        }

        private bool TryGetUserId(out long userId) =>
            long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }
}
