using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // SMSS's own expenses. Super Admin only, logged directly (no approval step). Voiding is a soft void: the row is kept, marked Voided.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [SuperAdminOnly]
    public class CompanyExpenseController : ControllerBase
    {
        private readonly ICompanyExpenseService _service;
        public CompanyExpenseController(ICompanyExpenseService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

        // GET api/CompanyExpense/categories
        [HttpGet("categories")]
        public async Task<IActionResult> Categories(CancellationToken ct) => Reply(await _service.GetCategoriesAsync(ct));

        // GET api/CompanyExpense?categoryId=&from=2026-04-01&to=2026-10-31&status=Active|Voided&page=1&pageSize=20
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] long? categoryId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? status,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
            Reply(await _service.ListAsync(categoryId, from, to, status, page, pageSize, ct));

        // GET api/CompanyExpense/{id}
        [HttpGet("{id:long}")]
        public async Task<IActionResult> Get(long id, CancellationToken ct) => Reply(await _service.GetAsync(id, ct));

        // POST api/CompanyExpense   Body: { expenseDate, categoryId, description, amount, paymentMode }
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateCompanyExpenseRequest request, CancellationToken ct)
        {
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            return Reply(await _service.CreateAsync(userId, request, ct));
        }

        // POST api/CompanyExpense/{id}/void   Body: { voidReason }
        [HttpPost("{id:long}/void")]
        public async Task<IActionResult> Void(long id, [FromBody] VoidCompanyRecordRequest request, CancellationToken ct)
        {
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            return Reply(await _service.VoidAsync(id, userId, request, ct));
        }
    }
}
