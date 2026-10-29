using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // The global subscription rate card. Super Admin only.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [SuperAdminOnly]
    public class SubscriptionPricingController : ControllerBase
    {
        private readonly ISubscriptionPricingService _service;
        public SubscriptionPricingController(ISubscriptionPricingService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

        // GET api/SubscriptionPricing   (the current rate card + every earlier version)
        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken ct) => Reply(await _service.GetAsync(ct));

        // PUT api/SubscriptionPricing   Body: { ratePerUserMonthly, yearlyDiscountPercent, trialDays }. Adds a new version, deactivates the old one.
        [HttpPut]
        public async Task<IActionResult> Update([FromBody] UpdateSubscriptionPricingRequest request, CancellationToken ct) =>
            Reply(await _service.UpdateAsync(request, ct));
    }
}
