using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // Promotion of students into a later year. School Admin and Principal only (CanPromoteStudents); everyone else, teachers included, gets 403. The school comes from the token only;
    // a year, class or section of another school answers 404. The preview is a POST (it carries the admin's edits) and writes nothing.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PromotionController : ControllerBase
    {
        private readonly IPromotionService _service;
        public PromotionController(IPromotionService service) => _service = service;

        // POST api/Promotion/preview   body { sourceYearId, targetYearId, classId, sectionId?, promotionDate?, decisions?: [{ studentId, decision, toClassId?, toSectionId?, rollNumber? }] }
        [HttpPost("preview")]
        public async Task<IActionResult> Preview([FromBody] PromotionPreviewRequest request, CancellationToken ct)
        {
            if (!User.CanPromoteStudents()) return this.Forbidden("Only a School Admin or a Principal can promote students.");
            var response = await _service.PreviewAsync(User.GetSchoolId(), request, ct);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/Promotion/execute   body = the preview request + { confirm: true, overrideReason?, overrideStudentIds?, acknowledgeNoFeeStructure?, confirmBackdating? }
        [HttpPost("execute")]
        public async Task<IActionResult> Execute([FromBody] PromotionExecuteRequest request, CancellationToken ct)
        {
            if (!User.CanPromoteStudents()) return this.Forbidden("Only a School Admin or a Principal can promote students.");
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            var response = await _service.ExecuteAsync(User.GetSchoolId(), userId, request, ct);
            return StatusCode(response.StatusCode, response);
        }

        // GET api/Promotion/pending?summary=true   the students waiting to be promoted into the current year (summary = the count only, for the dashboard tile)
        [HttpGet("pending")]
        public async Task<IActionResult> Pending([FromQuery] bool summary, CancellationToken ct)
        {
            if (!User.CanPromoteStudents()) return this.Forbidden("Only a School Admin or a Principal can see the students waiting for promotion.");
            var response = await _service.GetPendingAsync(User.GetSchoolId(), !summary, ct);
            return StatusCode(response.StatusCode, response);
        }

        // GET api/Promotion/corrections/check?studentId=ST1&sourceYearId=3   what correcting this student's promotion would remove, or why it is refused (read-only)
        [HttpGet("corrections/check")]
        public async Task<IActionResult> CheckCorrection([FromQuery] string? studentId, [FromQuery] long sourceYearId, CancellationToken ct)
        {
            if (!User.CanPromoteStudents()) return this.Forbidden("Only a School Admin or a Principal can correct a promotion.");
            var response = await _service.CheckCorrectionAsync(User.GetSchoolId(), studentId, sourceYearId, ct);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/Promotion/corrections   body { studentId, sourceYearId, reason, confirm: true }
        [HttpPost("corrections")]
        public async Task<IActionResult> Correct([FromBody] PromotionCorrectionRequest request, CancellationToken ct)
        {
            if (!User.CanPromoteStudents()) return this.Forbidden("Only a School Admin or a Principal can correct a promotion.");
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            var response = await _service.CorrectAsync(User.GetSchoolId(), userId, request, ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
