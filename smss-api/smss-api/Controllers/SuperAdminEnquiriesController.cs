using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // Enquiries from the public site, for the Super Admin only. The handling user always comes from the token.
    // The status change is a POST, not a PATCH: PATCH is blocked in this project's dev environment (see CLAUDE.md).
    [ApiController]
    [Route("api/superadmin/enquiries")]
    [Authorize]
    [SuperAdminOnly]
    public class SuperAdminEnquiriesController : ControllerBase
    {
        private readonly IEnquiryService _service;
        public SuperAdminEnquiriesController(IEnquiryService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

        // GET api/superadmin/enquiries?type=&status=&from=yyyy-MM-dd&to=yyyy-MM-dd&search=&sort=created_desc|created_asc&page=1&pageSize=10
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? type, [FromQuery] string? status, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
            [FromQuery] string? search, [FromQuery] string? sort, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default) =>
            Reply(await _service.ListAsync(type, status, from, to, search, sort, page, pageSize, ct));

        // GET api/superadmin/enquiries/summary   (declared before {id}; the :long constraint keeps "summary" from matching it anyway)
        [HttpGet("summary")]
        public async Task<IActionResult> Summary(CancellationToken ct) => Reply(await _service.GetSummaryAsync(ct));

        // GET api/superadmin/enquiries/{id}
        [HttpGet("{id:long}")]
        public async Task<IActionResult> Get(long id, CancellationToken ct) => Reply(await _service.GetAsync(id, ct));

        // POST api/superadmin/enquiries/{id}/status   Body: { status, adminNotes? }
        [HttpPost("{id:long}/status")]
        public async Task<IActionResult> UpdateStatus(long id, [FromBody] UpdateEnquiryStatusRequest request, CancellationToken ct)
        {
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            return Reply(await _service.UpdateStatusAsync(id, userId, request, ct));
        }
    }
}
