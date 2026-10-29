using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // Salary paid to SMSS's own staff: a flat amount per employee per month, locked once recorded. Super Admin only. Voiding is a soft void.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [SuperAdminOnly]
    public class CompanySalaryController : ControllerBase
    {
        private readonly ICompanySalaryService _service;
        public CompanySalaryController(ICompanySalaryService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

        // POST api/CompanySalary
        // Body: { companyEmployeeId, periodMonth, periodYear, amount?, paymentDate?, paymentMode, remarks? }
        // No amount = the employee's current monthly salary. One Active payment per employee per month.
        [HttpPost]
        public async Task<IActionResult> Record([FromBody] RecordCompanySalaryRequest request, CancellationToken ct)
        {
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            return Reply(await _service.RecordAsync(userId, request, ct));
        }

        // POST api/CompanySalary/{id}/void   Body: { voidReason }
        [HttpPost("{id:long}/void")]
        public async Task<IActionResult> Void(long id, [FromBody] VoidCompanyRecordRequest request, CancellationToken ct)
        {
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            return Reply(await _service.VoidAsync(id, userId, request, ct));
        }

        // GET api/CompanySalary?companyEmployeeId=&year=&month=&status=Active|Voided&page=1&pageSize=20   (all payments, filterable)
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] long? companyEmployeeId, [FromQuery] int? year, [FromQuery] int? month, [FromQuery] string? status,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
            Reply(await _service.ListAsync(companyEmployeeId, year, month, status, page, pageSize, ct));

        // GET api/CompanySalary/employee/{companyEmployeeId}   (one employee's payments, newest period first)
        [HttpGet("employee/{companyEmployeeId:long}")]
        public async Task<IActionResult> ForEmployee(long companyEmployeeId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
            Reply(await _service.ListAsync(companyEmployeeId, null, null, null, page, pageSize, ct));
    }
}
