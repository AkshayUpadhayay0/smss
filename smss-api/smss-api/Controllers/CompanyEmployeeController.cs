using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // SMSS's own staff (not a school's employees). Super Admin only. "Delete" is a status toggle (POST, by project convention): nothing is removed.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [SuperAdminOnly]
    public class CompanyEmployeeController : ControllerBase
    {
        private readonly ICompanyEmployeeService _service;
        public CompanyEmployeeController(ICompanyEmployeeService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

        // GET api/CompanyEmployee?search=&status=Active|Inactive
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] string? status, CancellationToken ct) => Reply(await _service.ListAsync(search, status, ct));

        // GET api/CompanyEmployee/{id}
        [HttpGet("{id:long}")]
        public async Task<IActionResult> Get(long id, CancellationToken ct) => Reply(await _service.GetAsync(id, ct));

        // POST api/CompanyEmployee
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CompanyEmployeeRequest request, CancellationToken ct) => Reply(await _service.CreateAsync(request, ct));

        // PUT api/CompanyEmployee/{id}   (the salary here is the CURRENT salary; recorded salary payments keep their own amounts)
        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] CompanyEmployeeRequest request, CancellationToken ct) => Reply(await _service.UpdateAsync(id, request, ct));

        // POST api/CompanyEmployee/{id}/toggle-status
        [HttpPost("{id:long}/toggle-status")]
        public async Task<IActionResult> ToggleStatus(long id, CancellationToken ct) => Reply(await _service.ToggleStatusAsync(id, ct));
    }
}
