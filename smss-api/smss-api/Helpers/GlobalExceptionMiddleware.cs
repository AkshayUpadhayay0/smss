using smss_api_service_layer.dto;
using System.Text.Json;

namespace smss_api.Helpers
{
    /// <summary>
    /// The last line of defence for a request: anything a controller / service did not handle is logged in full and
    /// answered with the standard ApiResponse 500 (never a stack trace). The process keeps serving other requests.
    /// </summary>
    public class GlobalExceptionMiddleware
    {
        private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;

        public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // The client went away; nothing to answer and nothing wrong
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);

                if (context.Response.HasStarted) return;   // too late to change the response; the log above is what matters

                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json";
                var body = new ApiResponse<object> { Status = false, StatusCode = 500, Message = "Something went wrong. Please try again later.", Data = null };
                await context.Response.WriteAsync(JsonSerializer.Serialize(body, Json));
            }
        }
    }
}
