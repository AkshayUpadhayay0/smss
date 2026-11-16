using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // Fee payment collection. The school and the collecting user always come from the token, never from the request.
    // Recording and voiding a payment are limited to the School Admin and Accountant roles (CanManageFees). Voiding is a soft void (the payment is kept, marked Voided).
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class FeePaymentController : ControllerBase
    {
        private readonly IFeePaymentService _service;
        public FeePaymentController(IFeePaymentService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

        // GET api/FeePayment/concessions   (the school's ACTIVE concessions, for the Collect Payment form: School Admin and Accountant, who can collect)
        [HttpGet("concessions")]
        public async Task<IActionResult> GetConcessions(CancellationToken ct)
        {
            if (!User.CanManageFees()) return this.Forbidden("Only a School Admin or an Accountant can choose a concession for a payment.");
            return Reply(await _service.GetConcessionChoicesAsync(User.GetSchoolId(), ct));
        }

        // POST api/FeePayment/students/{studentId}
        // Body: { amount, paymentMode, allocations?: [{ feeDueId, amount }], remarks }. No allocations = oldest installment first.
        [HttpPost("students/{studentId:length(1,20)}")]
        public async Task<IActionResult> RecordPayment(string studentId, [FromBody] RecordPaymentRequest request, CancellationToken ct)
        {
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            if (!User.CanManageFees())
                return Reply(new ApiResponse<object> { Status = false, StatusCode = 403, Message = "Only a School Admin or an Accountant can record a payment.", Data = null });
            return Reply(await _service.RecordPaymentAsync(User.GetSchoolId(), studentId, userId, request, ct));
        }

        // GET api/FeePayment/students/{studentId}   (payment history, newest first)
        [HttpGet("students/{studentId:length(1,20)}")]
        public async Task<IActionResult> GetPayments(string studentId, CancellationToken ct)
        {
            if (!User.CanViewFees()) return this.Forbidden("Only a School Admin, Accountant or Principal can view a student's payments.");
            return Reply(await _service.GetPaymentsAsync(User.GetSchoolId(), studentId, ct));
        }

        // POST api/FeePayment/{paymentId}/void   Body: { voidReason }
        // 403 unless the caller is a School Admin or Accountant. 404 when the payment isn't in the caller's school or is already voided.
        [HttpPost("{paymentId:long}/void")]
        public async Task<IActionResult> VoidPayment(long paymentId, [FromBody] VoidPaymentRequest request, CancellationToken ct)
        {
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            if (!User.CanManageFees())
                return Reply(new ApiResponse<object> { Status = false, StatusCode = 403, Message = "Only a School Admin or an Accountant can void a payment.", Data = null });
            return Reply(await _service.VoidPaymentAsync(User.GetSchoolId(), paymentId, userId, request, ct));
        }

        // GET api/FeePayment/{paymentId}/receipt
        // School Admin / Accountant / Principal (CanViewFees): any receipt of their own school. A Student: only a receipt of their OWN payment (the student id comes from the
        // token). Every other role 403; another school's payment 404; another student's payment 404 (not 403: a 403 would confirm that the id exists).
        [HttpGet("{paymentId:long}/receipt")]
        public async Task<IActionResult> GetReceipt(long paymentId, CancellationToken ct)
        {
            if (User.CanViewFees()) return Reply(await _service.GetReceiptAsync(User.GetSchoolId(), paymentId, ct));
            if (User.IsStudent())
            {
                if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
                return Reply(await _service.GetOwnReceiptAsync(User.GetSchoolId(), userId, paymentId, ct));
            }
            return this.Forbidden("Only a School Admin, Accountant, Principal or the student who paid can view a receipt.");
        }
    }
}
