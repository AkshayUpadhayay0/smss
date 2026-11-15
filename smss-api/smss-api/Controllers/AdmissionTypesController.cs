using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // Admission types are a GLOBAL master owned by the Super Admin (writes: MasterDataController, admission-types).
    // This controller is only the read-only list every signed-in school role uses to fill the Admission form: ACTIVE types only.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AdmissionTypesController : ControllerBase
    {
        private readonly IMasterDataService _masterData;
        public AdmissionTypesController(IMasterDataService masterData) => _masterData = masterData;

        // GET api/AdmissionTypes
        [HttpGet]
        public async Task<IActionResult> GetActive()
        {
            var response = await _masterData.GetAdmissionTypesAsync(includeInactive: false);
            return StatusCode(response.StatusCode, response);
        }
    }
}
