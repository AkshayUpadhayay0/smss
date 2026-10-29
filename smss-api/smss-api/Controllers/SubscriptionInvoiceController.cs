using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // Subscription invoices (what SMSS bills a school). Super Admin only. The generating user comes from the token.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [SuperAdminOnly]
    public class SubscriptionInvoiceController : ControllerBase
    {
        private readonly ISubscriptionBillingService _service;
        public SubscriptionInvoiceController(ISubscriptionBillingService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

        // GET api/SubscriptionInvoice?schoolId=&status=Pending|Paid|Overdue&page=1&pageSize=20
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? schoolId, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
            Reply(await _service.ListInvoicesAsync(schoolId, status, page, pageSize, ct));

        // GET api/SubscriptionInvoice/{invoiceId}   (one invoice with its payments, voided ones included)
        [HttpGet("{invoiceId:long}")]
        public async Task<IActionResult> Get(long invoiceId, CancellationToken ct) => Reply(await _service.GetInvoiceAsync(invoiceId, ct));

        // POST api/SubscriptionInvoice/{schoolId}/generate   Body (optional): { dueDate }
        [HttpPost("{schoolId:length(1,100)}/generate")]
        public async Task<IActionResult> Generate(string schoolId, [FromBody] GenerateInvoiceRequest? request, CancellationToken ct)
        {
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            return Reply(await _service.GenerateInvoiceAsync(schoolId, userId, request ?? new GenerateInvoiceRequest(), ct));
        }
    }
}
