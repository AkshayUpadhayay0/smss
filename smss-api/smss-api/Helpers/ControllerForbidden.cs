using Microsoft.AspNetCore.Mvc;
using smss_api_service_layer.dto;

namespace smss_api.Helpers
{
    public static class ControllerForbidden
    {
        /// <summary>The standard 403 in the project's response shape.</summary>
        public static IActionResult Forbidden(this ControllerBase controller, string message) =>
            controller.StatusCode(403, new ApiResponse<object> { Status = false, StatusCode = 403, Message = message, Data = null });
    }
}
