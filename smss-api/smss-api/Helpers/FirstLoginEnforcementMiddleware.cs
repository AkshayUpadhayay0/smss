using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using System.Security.Claims;
using System.Text.Json;

namespace smss_api.Helpers
{
    /// <summary>
    /// The AUTHORITATIVE forced password change. While a signed-in user's tb_users.is_first_login is true (they are still on the temporary
    /// password), every request that needs authentication is answered 403 "Password change required", except the few that are needed to
    /// leave that state (<see cref="Allowed"/>). The Angular guard only mirrors this for a nicer screen.
    ///
    /// Why the database and not a token claim: the flag is read fresh on every request, so a token issued before the change (or on another
    /// device) cannot carry on after it is set, and nothing can be forged or go stale. It costs one primary-key lookup per request.
    /// Anonymous endpoints (login, refresh, logout, bootstrap) are not touched, and neither is anything without a valid token (401 comes from authentication).
    /// Runs after authorization, so a request that would be 401/403 anyway keeps that answer.
    /// </summary>
    public class FirstLoginEnforcementMiddleware
    {
        public const string Message = "Password change required. Change your temporary password to continue.";

        /// <summary>The only authenticated calls a first-login user may make: set the new password, see who they are, refresh and leave.</summary>
        private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
        {
            "POST /api/Auth/change-password",
            "GET /api/Auth/me",
            "POST /api/Auth/refresh",
            "POST /api/Auth/logout",
        };

        private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly RequestDelegate _next;
        public FirstLoginEnforcementMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context, IAuthRepository users, ILogger<FirstLoginEnforcementMiddleware> logger)
        {
            var endpoint = context.GetEndpoint();
            // only real endpoints that require a signed-in user: anonymous ones and unknown paths are not this middleware's business
            if (endpoint == null || endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() != null
                || context.User.Identity?.IsAuthenticated != true)
            {
                await _next(context);
                return;
            }

            var path = (context.Request.Path.Value ?? string.Empty).TrimEnd('/');
            if (Allowed.Contains($"{context.Request.Method} {path}"))
            {
                await _next(context);
                return;
            }

            if (!long.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                await Reply(context, 401, "Invalid session.");
                return;
            }

            var firstLogin = await users.GetFirstLoginFlagAsync(userId);
            if (firstLogin == null)
            {
                logger.LogWarning("A valid token for user {UserId} who no longer exists was refused", userId);
                await Reply(context, 401, "Account not found.");
                return;
            }
            if (firstLogin == true)
            {
                await Reply(context, 403, Message);
                return;
            }

            await _next(context);
        }

        private static async Task Reply(HttpContext context, int code, string message)
        {
            context.Response.StatusCode = code;
            context.Response.ContentType = "application/json";
            var body = new ApiResponse<object> { Status = false, StatusCode = code, Message = message, Data = null };
            await context.Response.WriteAsync(JsonSerializer.Serialize(body, Json));
        }
    }
}
