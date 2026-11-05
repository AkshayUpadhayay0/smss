using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // A class teacher's read-only fee status for their own section (status and amounts per student, no receipts / modes / concessions). School Admin may also ask for any section.
    // The school always comes from the token. [Authorize(Roles)] is switched off project-wide, so the roles are checked here and WHICH teacher in the service.
    [ApiController]
    [Route("api/Fee")]
    [Authorize]
    public class FeeSectionStatusController : ControllerBase
    {
        private readonly IFeeSectionStatusService _service;
        public FeeSectionStatusController(IFeeSectionStatusService service) => _service = service;

        // GET api/Fee/section-status?sectionId=&academicYearId=&month=2026-11   (month optional: the current month, kept inside the academic year)
        [HttpGet("section-status")]
        public async Task<IActionResult> GetSectionStatus([FromQuery] long? sectionId, [FromQuery] long? academicYearId, [FromQuery] string? month, CancellationToken ct)
        {
            if (!User.IsSchoolAdmin() && !User.IsTeacher()) return this.Forbidden("Only a School Admin or the section's class teacher can see a section's fee status.");
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            if (sectionId is null) return StatusCode(400, new smss_api_service_layer.dto.ApiResponse<object> { Status = false, StatusCode = 400, Message = "sectionId is required.", Data = null });
            var response = await _service.GetAsync(User.GetSchoolId(), userId, User.IsSchoolAdmin(), sectionId.Value, academicYearId, month, ct);
            return StatusCode(response.StatusCode, response);
        }

        // GET api/Fee/section-status/months?sectionId=&academicYearId=   (student x month grid of status codes for the whole academic year; same authorization)
        [HttpGet("section-status/months")]
        public async Task<IActionResult> GetSectionStatusMonths([FromQuery] long? sectionId, [FromQuery] long? academicYearId, CancellationToken ct)
        {
            if (!User.IsSchoolAdmin() && !User.IsTeacher()) return this.Forbidden("Only a School Admin or the section's class teacher can see a section's fee status.");
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            if (sectionId is null) return StatusCode(400, new smss_api_service_layer.dto.ApiResponse<object> { Status = false, StatusCode = 400, Message = "sectionId is required.", Data = null });
            var response = await _service.GetGridAsync(User.GetSchoolId(), userId, User.IsSchoolAdmin(), sectionId.Value, academicYearId, ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
