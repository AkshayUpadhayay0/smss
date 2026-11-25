using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

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
    }
}
