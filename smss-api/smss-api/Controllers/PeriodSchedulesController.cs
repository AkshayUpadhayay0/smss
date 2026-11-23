using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // S4: bell schedules and their periods / breaks. Everything is scoped to the caller's own school (from the token); no school id is accepted.
    // School Admin configures; Principal views. (Which schedule a class follows in a year is under /api/AcademicSetup/years/{id}/schedules.)
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class PeriodSchedulesController : ControllerBase
    {
        private readonly IPeriodScheduleService _service;
        public PeriodSchedulesController(IPeriodScheduleService service) => _service = service;

        private IActionResult? Refuse(bool write) =>
            (write ? User.IsSchoolAdmin() : User.CanViewAcademicSetup()) ? null : this.Forbidden(write ? "Only a School Admin can change bell schedules." : "Only a School Admin or Principal can see bell schedules.");

        private bool TryUser(out long userId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
        private IActionResult Reply<T>(ApiResponse<T> r) => StatusCode(r.StatusCode, r);

        // GET api/PeriodSchedules
        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct) => Refuse(false) ?? Reply(await _service.ListAsync(User.GetSchoolId(), ct));

        // POST api/PeriodSchedules   { scheduleName, copyFromScheduleId? }
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateScheduleRequest request, CancellationToken ct) =>
            Refuse(true) ?? (TryUser(out var u) ? Reply(await _service.CreateAsync(User.GetSchoolId(), u, request, ct)) : Unauthorized());

        // PUT api/PeriodSchedules/5   { scheduleName }
        [HttpPut("{id:long}")]
        public async Task<IActionResult> Rename(long id, [FromBody] RenameScheduleRequest request, CancellationToken ct) =>
            Refuse(true) ?? (TryUser(out var u) ? Reply(await _service.RenameAsync(User.GetSchoolId(), u, id, request, ct)) : Unauthorized());

        // POST api/PeriodSchedules/5/set-default   (a POST, not a PATCH: PATCH is blocked in this dev environment)
        [HttpPost("{id:long}/set-default")]
        public async Task<IActionResult> SetDefault(long id, CancellationToken ct) =>
            Refuse(true) ?? (TryUser(out var u) ? Reply(await _service.SetDefaultAsync(User.GetSchoolId(), u, id, ct)) : Unauthorized());

        // DELETE api/PeriodSchedules/5
        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id, CancellationToken ct) => Refuse(true) ?? Reply(await _service.DeleteAsync(User.GetSchoolId(), id, ct));

        // POST api/PeriodSchedules/5/slots   { slotType: PERIOD|BREAK, periodNumber?, label?, startTime, endTime }
        [HttpPost("{id:long}/slots")]
        public async Task<IActionResult> AddSlot(long id, [FromBody] ScheduleSlotRequest request, CancellationToken ct) =>
            Refuse(true) ?? Reply(await _service.AddSlotAsync(User.GetSchoolId(), id, request, ct));

        // PUT api/PeriodSchedules/5/slots/9
        [HttpPut("{id:long}/slots/{periodId:long}")]
        public async Task<IActionResult> UpdateSlot(long id, long periodId, [FromBody] ScheduleSlotRequest request, CancellationToken ct) =>
            Refuse(true) ?? Reply(await _service.UpdateSlotAsync(User.GetSchoolId(), id, periodId, request, ct));

        // POST api/PeriodSchedules/5/slots/9/toggle-status
        [HttpPost("{id:long}/slots/{periodId:long}/toggle-status")]
        public async Task<IActionResult> ToggleSlot(long id, long periodId, CancellationToken ct) =>
            Refuse(true) ?? Reply(await _service.ToggleSlotAsync(User.GetSchoolId(), id, periodId, ct));

        // DELETE api/PeriodSchedules/5/slots/9   (only a slot that no timetable entry ever used)
        [HttpDelete("{id:long}/slots/{periodId:long}")]
        public async Task<IActionResult> DeleteSlot(long id, long periodId, CancellationToken ct) =>
            Refuse(true) ?? Reply(await _service.DeleteSlotAsync(User.GetSchoolId(), id, periodId, ct));
    }
}
