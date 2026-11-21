using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // Academic Setup: what a school offers in each academic year. Everything is scoped to the caller's own school (from the token); no school id is accepted.
    // School Admin configures; Principal views. (The class / section MASTERS stay under /api/Classes and /api/Sections.)
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AcademicSetupController : ControllerBase
    {
        private readonly IAcademicSetupService _service;
        public AcademicSetupController(IAcademicSetupService service) => _service = service;

        // GET api/AcademicSetup/years/5/classes
        [HttpGet("years/{yearId:long}/classes")]
        public async Task<IActionResult> GetClasses(long yearId, CancellationToken ct)
        {
            if (!User.CanViewAcademicSetup()) return this.Forbidden("Only a School Admin or Principal can see the academic setup.");
            var response = await _service.GetOfferingAsync(User.GetSchoolId(), yearId, ct);
            return StatusCode(response.StatusCode, response);
        }

        // PUT api/AcademicSetup/years/5/classes   body { classes: [ { classId, sections: [ { sectionId, maxStrength? } ] } ] }  = the WHOLE offered set
        [HttpPut("years/{yearId:long}/classes")]
        public async Task<IActionResult> SaveClasses(long yearId, [FromBody] SaveYearOfferingRequest request, CancellationToken ct)
        {
            if (!User.IsSchoolAdmin()) return this.Forbidden("Only a School Admin can change the academic setup.");
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            var response = await _service.SaveOfferingAsync(User.GetSchoolId(), userId, yearId, request, ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
