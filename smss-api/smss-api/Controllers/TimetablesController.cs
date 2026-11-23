using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // S6: timetable VERSIONS of a section (several per year, one ACTIVE). School Admin only; the school and the user come from the token.
    // The original /api/Timetable endpoints (grids, my, student/my, lookups, assign, unassign) keep working and read / write the ACTIVE timetable.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class TimetablesController : ControllerBase
    {
        private readonly ITimetableVersionService _versions;
        private readonly ITimetableService _grids;
        public TimetablesController(ITimetableVersionService versions, ITimetableService grids) { _versions = versions; _grids = grids; }

        private IActionResult? Refuse() => User.IsSchoolAdmin() ? null : this.Forbidden("Only a School Admin can manage timetables.");
        private IActionResult Reply<T>(ApiResponse<T> r) => StatusCode(r.StatusCode, r);
        private bool TryUser(out long userId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

        // GET api/Timetables?sectionId=3&academicYearId=   (the section's timetables: active first; the year defaults to the current one)
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] long? sectionId, [FromQuery] long? academicYearId, CancellationToken ct)
        {
            if (Refuse() is { } refused) return refused;
            if (sectionId is null or <= 0) return StatusCode(400, new ApiResponse<object> { Status = false, StatusCode = 400, Message = "sectionId is required.", Data = null });
            return Reply(await _versions.ListAsync(User.GetSchoolId(), academicYearId, sectionId.Value, ct));
        }

        // GET api/Timetables/{id}/grid   (the weekly grid of THIS timetable, whatever its state)
        [HttpGet("{id:long}/grid")]
        public async Task<IActionResult> Grid(long id, CancellationToken ct) => Refuse() ?? Reply(await _grids.GetTimetableGridAsync(User.GetSchoolId(), id, ct));

        // POST api/Timetables   { academicYearId?, sectionId, timetableName, cloneFromTimetableId? }   -> a new DRAFT
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateTimetableRequest request, CancellationToken ct) =>
            Refuse() ?? (TryUser(out var u) ? Reply(await _versions.CreateAsync(User.GetSchoolId(), u, request, ct)) : Unauthorized());

        // POST api/Timetables/{id}/rename   { timetableName }
        [HttpPost("{id:long}/rename")]
        public async Task<IActionResult> Rename(long id, [FromBody] RenameTimetableRequest request, CancellationToken ct) =>
            Refuse() ?? (TryUser(out var u) ? Reply(await _versions.RenameAsync(User.GetSchoolId(), u, id, request, ct)) : Unauthorized());

        // POST api/Timetables/{id}/slots/assign   { dayOfWeek, periodId, subjectId, employeeId?, roomNo? }
        [HttpPost("{id:long}/slots/assign")]
        public async Task<IActionResult> Assign(long id, [FromBody] AssignSlotRequest request, CancellationToken ct) =>
            Refuse() ?? (TryUser(out var u) ? Reply(await _versions.AssignAsync(User.GetSchoolId(), u, id, request, ct)) : Unauthorized());

        // POST api/Timetables/{id}/slots/unassign   { dayOfWeek, periodId }
        [HttpPost("{id:long}/slots/unassign")]
        public async Task<IActionResult> Unassign(long id, [FromBody] UnassignSlotRequest request, CancellationToken ct) =>
            Refuse() ?? (TryUser(out var u) ? Reply(await _versions.UnassignAsync(User.GetSchoolId(), u, id, request, ct)) : Unauthorized());

        // POST api/Timetables/{id}/validate   (dry run: lists every problem, saves nothing)
        [HttpPost("{id:long}/validate")]
        public async Task<IActionResult> Validate(long id, CancellationToken ct) => Refuse() ?? Reply(await _versions.ValidateAsync(User.GetSchoolId(), id, ct));

        // POST api/Timetables/{id}/activate
        [HttpPost("{id:long}/activate")]
        public async Task<IActionResult> Activate(long id, CancellationToken ct) =>
            Refuse() ?? (TryUser(out var u) ? Reply(await _versions.ActivateAsync(User.GetSchoolId(), u, id, ct)) : Unauthorized());

        // POST api/Timetables/{id}/archive   and   /restore
        [HttpPost("{id:long}/archive")]
        public async Task<IActionResult> Archive(long id, CancellationToken ct) =>
            Refuse() ?? (TryUser(out var u) ? Reply(await _versions.ArchiveAsync(User.GetSchoolId(), u, id, ct)) : Unauthorized());

        [HttpPost("{id:long}/restore")]
        public async Task<IActionResult> Restore(long id, CancellationToken ct) =>
            Refuse() ?? (TryUser(out var u) ? Reply(await _versions.RestoreAsync(User.GetSchoolId(), u, id, ct)) : Unauthorized());

        // DELETE api/Timetables/{id}   (only a draft that was never active)
        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id, CancellationToken ct) => Refuse() ?? Reply(await _versions.DeleteAsync(User.GetSchoolId(), id, ct));

        // POST api/Timetables/{id}/apply-preview   { sectionIds: [], timetableName? }   (what would be created; nothing is saved)
        [HttpPost("{id:long}/apply-preview")]
        public async Task<IActionResult> ApplyPreview(long id, [FromBody] ApplyToSectionsRequest request, CancellationToken ct) =>
            Refuse() ?? Reply(await _versions.PreviewApplyAsync(User.GetSchoolId(), id, request, ct));

        // POST api/Timetables/{id}/apply   { sectionIds: [], timetableName? }   -> a new independent DRAFT per chosen section (never overwrites)
        [HttpPost("{id:long}/apply")]
        public async Task<IActionResult> Apply(long id, [FromBody] ApplyToSectionsRequest request, CancellationToken ct) =>
            Refuse() ?? (TryUser(out var u) ? Reply(await _versions.ApplyAsync(User.GetSchoolId(), u, id, request, ct)) : Unauthorized());
    }
}
