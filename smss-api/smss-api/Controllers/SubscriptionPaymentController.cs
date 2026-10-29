using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // Payments against subscription invoices. Super Admin only. Voiding is a soft void (the payment is kept, marked Voided).
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [SuperAdminOnly]
    public class SubscriptionPaymentController : ControllerBase
    {
        private readonly ISubscriptionBillingService _service;
        public SubscriptionPaymentController(ISubscriptionBillingService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

        // POST api/SubscriptionPayment
        // Body: { schoolId, amount, paymentMode, referenceNumber?, remarks?, allocations?: [{ invoiceId, amount }] }
        // No allocations = the school's oldest unpaid invoices are paid first. Allocations must add up to amount exactly.
        [HttpPost]
        public async Task<IActionResult> Record([FromBody] RecordSubscriptionPaymentRequest request, CancellationToken ct)
        {
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            return Reply(await _service.RecordPaymentAsync(userId, request, ct));
        }

        // POST api/SubscriptionPayment/{paymentId}/void   Body: { voidReason }
        [HttpPost("{paymentId:long}/void")]
        public async Task<IActionResult> Void(long paymentId, [FromBody] VoidSubscriptionPaymentRequest request, CancellationToken ct)
        {
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            return Reply(await _service.VoidPaymentAsync(paymentId, userId, request, ct));
        }
    }
}
