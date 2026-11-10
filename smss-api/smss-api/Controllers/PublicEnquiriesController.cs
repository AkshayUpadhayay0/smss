using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // The public marketing site's Contact Us / Request Demo forms. ANONYMOUS and platform-level: it touches only tb_enquiries and no school data.
    // Rate limited per IP (policy "PublicEnquiries", configured in appsettings RateLimiting:PublicEnquiries).
    [ApiController]
    [Route("api/public/enquiries")]
    [AllowAnonymous]
    public class PublicEnquiriesController : ControllerBase
    {
        public const string RateLimitPolicy = "PublicEnquiries";

        private readonly IEnquiryService _service;
        public PublicEnquiriesController(IEnquiryService service) => _service = service;

        // POST api/public/enquiries   Body: CreateEnquiryRequest (enquiryType CONTACT | DEMO)
        [HttpPost]
        [EnableRateLimiting(RateLimitPolicy)]
        [RequestSizeLimit(32 * 1024)]
        public async Task<IActionResult> Submit([FromBody] CreateEnquiryRequest request, CancellationToken ct)
        {
            // UseForwardedHeaders (Program.cs) has already replaced the connection address with the real client's, but only for requests from a configured proxy
            var response = await _service.SubmitAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
