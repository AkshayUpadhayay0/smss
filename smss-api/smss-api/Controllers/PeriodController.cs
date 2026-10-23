using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // The school's bell schedule. School Admin only (IsSchoolAdmin from the token); the school always comes from the token.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PeriodController : ControllerBase
    {
        private readonly IPeriodService _service;
        public PeriodController(IPeriodService service) => _service = service;

        private IActionResult? Refuse() => User.IsSchoolAdmin() ? null : this.Forbidden("Only a School Admin can manage periods.");
        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

        // GET api/Period?includeInactive=true
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] bool includeInactive = true, CancellationToken ct = default) =>
            Refuse() ?? Reply(await _service.ListAsync(User.GetSchoolId(), includeInactive, ct));

        // POST api/Period   Body: { periodNumber, startTime, endTime }
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] PeriodRequest request, CancellationToken ct) =>
            Refuse() ?? Reply(await _service.CreateAsync(User.GetSchoolId(), request, ct));

        // PUT api/Period/{periodId}
        [HttpPut("{periodId:long}")]
        public async Task<IActionResult> Update(long periodId, [FromBody] PeriodRequest request, CancellationToken ct) =>
            Refuse() ?? Reply(await _service.UpdateAsync(User.GetSchoolId(), periodId, request, ct));

        // POST api/Period/{periodId}/toggle-status   (the soft delete: Active <-> Inactive; refused while timetable entries use it)
        [HttpPost("{periodId:long}/toggle-status")]
        public async Task<IActionResult> ToggleStatus(long periodId, CancellationToken ct) =>
            Refuse() ?? Reply(await _service.ToggleStatusAsync(User.GetSchoolId(), periodId, ct));
    }
}
