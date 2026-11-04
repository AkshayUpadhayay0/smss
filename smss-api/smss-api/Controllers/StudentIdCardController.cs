using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // Printable student ID cards (CR80, front + back). School Admin only. The school always comes from the token - no school id is accepted -
    // so a student or section of another school is simply "not found" (404). [Authorize(Roles)] is switched off project-wide, so the role is
    // checked here from the token (IsSchoolAdmin), like Employee / Class Teacher.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class StudentIdCardController : ControllerBase
    {
        private readonly IStudentIdCardService _service;
        public StudentIdCardController(IStudentIdCardService service) => _service = service;

        private IActionResult? Refuse() =>
            User.IsSchoolAdmin() ? null : this.Forbidden("Only a School Admin can print student ID cards.");

        // GET api/StudentIdCard/student/{studentId}
        // Success: the PDF itself (application/pdf, Content-Disposition attachment). Any failure: the standard JSON envelope.
        [HttpGet("student/{studentId:length(1,20)}")]
        public async Task<IActionResult> GetStudentCard(string studentId, CancellationToken ct)
        {
            if (Refuse() is { } refused) return refused;
            var response = await _service.GetStudentCardAsync(User.GetSchoolId(), studentId, ct);
            if (!response.Status || response.Data == null) return StatusCode(response.StatusCode, response);
            return File(response.Data.Content, "application/pdf", response.Data.FileName);
        }

        // POST api/StudentIdCard/section/{sectionId}   (POST like the other generate-something actions; it changes nothing)
        // Returns the standard envelope; data carries the PDF as base64 plus the no-photo and excluded lists.
        [HttpPost("section/{sectionId:long}")]
        public async Task<IActionResult> GetSectionCards(long sectionId, CancellationToken ct)
        {
            if (Refuse() is { } refused) return refused;
            var response = await _service.GetSectionCardsAsync(User.GetSchoolId(), sectionId, ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
