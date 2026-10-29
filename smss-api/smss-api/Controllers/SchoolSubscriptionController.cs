using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // A school subscription (trial / billing frequency / cycle). Super Admin only. POST rather than PATCH, by project convention.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [SuperAdminOnly]
    public class SchoolSubscriptionController : ControllerBase
    {
        private readonly ISubscriptionBillingService _service;
        public SchoolSubscriptionController(ISubscriptionBillingService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

        // GET api/SchoolSubscription/{schoolId}
        [HttpGet("{schoolId:length(1,100)}")]
        public async Task<IActionResult> Get(string schoolId, CancellationToken ct) => Reply(await _service.GetSubscriptionAsync(schoolId, ct));

        // POST api/SchoolSubscription/{schoolId}/configure
        // Body: { billingFrequency: "MONTHLY" or "YEARLY", customRatePerStudentMonthly?, customYearlyDiscountPercent?, includeStaffInBilling? }
        // Moves TRIAL to ACTIVE and sets the first cycle. Null custom values mean "use the global pricing".
        [HttpPost("{schoolId:length(1,100)}/configure")]
        public async Task<IActionResult> Configure(string schoolId, [FromBody] ConfigureBillingRequest request, CancellationToken ct) =>
            Reply(await _service.ConfigureAsync(schoolId, request, ct));

        // PUT api/SchoolSubscription/{schoolId}/terms   Body: { customRatePerStudentMonthly?, customYearlyDiscountPercent?, includeStaffInBilling? }
        // Replaces the school's negotiated terms at any time after configuration. Null = global. Applies to invoices generated from now on.
        [HttpPut("{schoolId:length(1,100)}/terms")]
        public async Task<IActionResult> UpdateTerms(string schoolId, [FromBody] BillingTermsRequest request, CancellationToken ct) =>
            Reply(await _service.UpdateTermsAsync(schoolId, request, ct));
    }
}
