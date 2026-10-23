using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // The school calendar. Reads: any signed-in user of the school. Writes: School Admin or Principal (CanManageEvents).
    // The school and the creating user always come from the token; no school id is accepted.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SchoolEventController : ControllerBase
    {
        private readonly ISchoolEventService _service;
        public SchoolEventController(ISchoolEventService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
        private IActionResult? Refuse() => User.CanManageEvents() ? null : this.Forbidden("Only a School Admin or a Principal can manage calendar events.");

        // POST api/SchoolEvent
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SchoolEventRequest request, CancellationToken ct)
        {
            if (Refuse() is { } refused) return refused;
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            return Reply(await _service.CreateAsync(User.GetSchoolId(), userId, request, ct));
        }

        // PUT api/SchoolEvent/{eventId}
        [HttpPut("{eventId:long}")]
        public async Task<IActionResult> Update(long eventId, [FromBody] SchoolEventRequest request, CancellationToken ct) =>
            Refuse() ?? Reply(await _service.UpdateAsync(User.GetSchoolId(), eventId, request, ct));

        // DELETE api/SchoolEvent/{eventId}   (soft: the event is hidden, status Inactive)
        [HttpDelete("{eventId:long}")]
        public async Task<IActionResult> Delete(long eventId, CancellationToken ct) =>
            Refuse() ?? Reply(await _service.DeleteAsync(User.GetSchoolId(), eventId, ct));

        // GET api/SchoolEvent?from=2026-10-01&to=2026-10-31&academicYearId=   (events overlapping the range; the year is optional)
        [HttpGet]
        public async Task<IActionResult> GetRange([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] long? academicYearId, CancellationToken ct) =>
            Reply(await _service.GetRangeAsync(User.GetSchoolId(), academicYearId, from, to, ct));

        // GET api/SchoolEvent/upcoming?take=5   (the next events from today, ones still running included)
        [HttpGet("upcoming")]
        public async Task<IActionResult> GetUpcoming([FromQuery] int take = 5, CancellationToken ct = default) =>
            Reply(await _service.GetUpcomingAsync(User.GetSchoolId(), take, ct));

        // GET api/SchoolEvent/categories   (the categories with their colours, for the form and the legend)
        [HttpGet("categories")]
        public async Task<IActionResult> GetCategories(CancellationToken ct) => Reply(await _service.GetCategoriesAsync(ct));
    }
}
