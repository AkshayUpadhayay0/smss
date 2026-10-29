using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using smss_api_service_layer.dto;

namespace smss_api.Helpers
{
    /// <summary>
    /// Limits a controller (or action) to the Super Admin. Answers 403 in the project's response shape. Use it together with [Authorize]:
    /// an anonymous caller is left to [Authorize], which answers 401. It is an AUTHORIZATION filter on purpose: those run before model binding,
    /// so a caller who is not allowed gets 403 whatever they sent, never a 400 that describes the request body. Role matching stays in ClaimsPrincipalExtensions.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class SuperAdminOnlyAttribute : Attribute, IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;
            if (user.Identity?.IsAuthenticated != true || user.IsSuperAdmin()) return;
            context.Result = new ObjectResult(new ApiResponse<object> { Status = false, StatusCode = 403, Message = "Only the Super Admin can do this.", Data = null }) { StatusCode = 403 };
        }
    }
}
