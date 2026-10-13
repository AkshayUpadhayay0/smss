using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // "Import Masters": a school picks ready-made classes, subjects, designations, departments, admission types and
    // document types and they are created for it in one go. Always the caller's own school (JWT school_id claim).
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class MasterImportController : ControllerBase
    {
        private readonly IMasterImportService _service;
        public MasterImportController(IMasterImportService service) => _service = service;

        // GET api/MasterImport/catalog
        [HttpGet("catalog")]
        public async Task<IActionResult> GetCatalog(CancellationToken ct)
        {
            var response = await _service.GetCatalogAsync(User.GetSchoolId(), ct);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/MasterImport/import
        [HttpPost("import")]
        public async Task<IActionResult> Import([FromBody] ImportMastersRequest request, CancellationToken ct)
        {
            var response = await _service.ImportAsync(User.GetSchoolId(), request, ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
