using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // Mid-year section change (Stage 8c). School Admin and Principal only; everyone else gets 403. The school comes from the token only; a student of another school answers 404.
    [ApiController]
    [Route("api/SectionChanges")]
    [Authorize]
    public class SectionChangesController : ControllerBase
    {
        private readonly ISectionChangeService _service;
        public SectionChangesController(ISectionChangeService service) => _service = service;

        private IActionResult Deny() => this.Forbidden("Only a School Admin or a Principal can change a student's section.");

        // GET api/SectionChanges/check?studentId=ST1   the sections the student can move to (with strength and the next roll number) and what stops it (read-only)
        [HttpGet("check")]
        public async Task<IActionResult> Check([FromQuery] string? studentId, CancellationToken ct)
        {
            if (!User.CanPromoteStudents()) return Deny();
            var r = await _service.CheckAsync(User.GetSchoolId(), studentId, ct);
            return StatusCode(r.StatusCode, r);
        }

        // POST api/SectionChanges   body { studentId, toSectionId, effectiveDate?, reason (10+ chars), rollNumber?, confirm: true }
        [HttpPost]
        public async Task<IActionResult> Change([FromBody] ChangeSectionRequest request, CancellationToken ct)
        {
            if (!User.CanPromoteStudents()) return Deny();
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            var r = await _service.ChangeAsync(User.GetSchoolId(), userId, request, ct);
            return StatusCode(r.StatusCode, r);
        }

        // GET api/SectionChanges/history?studentId=ST1   every change of this student (newest first)
        [HttpGet("history")]
        public async Task<IActionResult> History([FromQuery] string? studentId, CancellationToken ct)
        {
            if (!User.CanPromoteStudents()) return Deny();
            var r = await _service.HistoryAsync(User.GetSchoolId(), studentId, ct);
            return StatusCode(r.StatusCode, r);
        }
    }
}
