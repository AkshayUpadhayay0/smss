using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;

namespace smss_api.Controllers
{
    // The "Our Partner Schools" section of the public site. ANONYMOUS, read-only, and takes NO input: a visitor cannot pick or probe a school.
    // Returns only name / logo / city / state of schools that are Active and opted in by the Super Admin (see PartnerSchoolDto).
    // Rate limited per IP (policy "PublicPartnerSchools", appsettings RateLimiting:PublicPartnerSchools), cached on the server and in the browser.
    [ApiController]
    [Route("api/public/partner-schools")]
    [AllowAnonymous]
    public class PublicPartnerSchoolsController : ControllerBase
    {
        public const string RateLimitPolicy = "PublicPartnerSchools";

        private readonly IPartnerSchoolService _service;
        private readonly IConfiguration _config;
        public PublicPartnerSchoolsController(IPartnerSchoolService service, IConfiguration config)
        {
            _service = service;
            _config = config;
        }

        // GET api/public/partner-schools
        [HttpGet]
        [EnableRateLimiting(RateLimitPolicy)]
        public async Task<IActionResult> List(CancellationToken ct)
        {
            var response = await _service.GetPublicListAsync(ct);
            Response.Headers.CacheControl = response.StatusCode == 200 ? $"public, max-age={PartnerSchoolService.CacheSecondsOf(_config)}" : "no-store";
            return StatusCode(response.StatusCode, response);
        }
    }
}
