using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // Record leaving (Stage 8a). School Admin and Principal only (the same people who promote); everyone else gets 403. The school comes from the token only; a student of another school answers 404.
    // Recording and correcting are POSTs (PATCH is blocked in the dev environment).
    [ApiController]
    [Route("api/StudentLeaving")]
    [Authorize]
    public class StudentLeavingController : ControllerBase
    {
        private readonly ILeavingService _service;
        public StudentLeavingController(ILeavingService service) => _service = service;

        // GET api/StudentLeaving/check?studentId=ST1&leavingDate=2026-11-26   what recording the leaving would do, the dues up to that date, and what stops it (read-only)
        [HttpGet("check")]
        public async Task<IActionResult> Check([FromQuery] string? studentId, [FromQuery] DateOnly? leavingDate, CancellationToken ct)
        {
            if (!User.CanPromoteStudents()) return this.Forbidden("Only a School Admin or a Principal can record a student leaving.");
            var response = await _service.CheckAsync(User.GetSchoolId(), studentId, leavingDate, ct);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/StudentLeaving/record   body { studentId, type: LEFT|TRANSFERRED_OUT, leavingDate?, reason, confirm: true, overrideDues?, overrideReason? }
        [HttpPost("record")]
        public async Task<IActionResult> Record([FromBody] RecordLeavingRequest request, CancellationToken ct)
        {
            if (!User.CanPromoteStudents()) return this.Forbidden("Only a School Admin or a Principal can record a student leaving.");
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            var response = await _service.RecordAsync(User.GetSchoolId(), userId, request, ct);
            return StatusCode(response.StatusCode, response);
        }

        // GET api/StudentLeaving/correction/check?studentId=ST1
        [HttpGet("correction/check")]
        public async Task<IActionResult> CheckCorrection([FromQuery] string? studentId, CancellationToken ct)
        {
            if (!User.CanPromoteStudents()) return this.Forbidden("Only a School Admin or a Principal can correct a leaving.");
            var response = await _service.CheckCorrectionAsync(User.GetSchoolId(), studentId, ct);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/StudentLeaving/correction   body { studentId, reason, confirm: true }
        [HttpPost("correction")]
        public async Task<IActionResult> Correct([FromBody] LeavingCorrectionRequest request, CancellationToken ct)
        {
            if (!User.CanPromoteStudents()) return this.Forbidden("Only a School Admin or a Principal can correct a leaving.");
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            var response = await _service.CorrectAsync(User.GetSchoolId(), userId, request, ct);
            return StatusCode(response.StatusCode, response);
        }

        // GET api/StudentLeaving/leavers?academicYearId=3   the students recorded as left or transferred out (newest first, at most 500)
        [HttpGet("leavers")]
        public async Task<IActionResult> Leavers([FromQuery] long? academicYearId, CancellationToken ct)
        {
            if (!User.CanPromoteStudents()) return this.Forbidden("Only a School Admin or a Principal can see the students who left.");
            var response = await _service.ListLeaversAsync(User.GetSchoolId(), academicYearId, ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
