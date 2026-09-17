using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MasterDataController : ControllerBase
    {
        private readonly IMasterDataService _masterDataService;
        public MasterDataController(IMasterDataService masterDataService)
        {
            _masterDataService = masterDataService;
        }

        // GET: api/MasterData/countries
        [HttpGet("countries")] 
        public async Task<IActionResult> GetCountries() 
        { 
            var response = await _masterDataService.GetCountriesAsync(); 
            return StatusCode(response.StatusCode, response); 
        } 

        // GET: api/MasterData/states/{countryId}
        [HttpGet("states/{countryId}")] 
        public async Task<IActionResult> GetStates(int countryId) 
        { 
            var response = await _masterDataService.GetStatesAsync(countryId); 
            return StatusCode(response.StatusCode, response); 
        } 

        // GET: api/MasterData/districts/{countryId}/{stateId}
        [HttpGet("districts/{countryId}/{stateId}")] 
        public async Task<IActionResult> GetDistricts( int countryId, int stateId) 
        { 
            var response = await _masterDataService.GetDistrictsAsync( countryId, stateId); 
            return StatusCode(response.StatusCode, response); 
        } 

        // GET: api/MasterData/cities/{countryId}/{stateId}/{districtId}
        [HttpGet("cities/{countryId}/{stateId}/{districtId}")] 
        public async Task<IActionResult> GetCities( int countryId, int stateId, int districtId) 
        { 
            var response = await _masterDataService.GetCitiesAsync( countryId, stateId, districtId); 
            return StatusCode(response.StatusCode, response); 
        }


        // GET: api/MasterData/status
        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            var response = await _masterDataService.GetStatusAsync();
            return StatusCode(response.StatusCode, response);
        }

        // ========================================================= // BOARD TYPE // ========================================================= 
        // GET: api/MasterData/board-types
        [HttpGet("board-types")] 
        public async Task<IActionResult> GetBoardTypes() { var response = await _masterDataService.GetBoardTypesAsync(); return StatusCode(response.StatusCode, response); } 
        
        // ========================================================= // SCHOOL TYPE // ========================================================= 
        // GET: api/MasterData/school-types
        [HttpGet("school-types")] 
        public async Task<IActionResult> GetSchoolTypes() { var response = await _masterDataService.GetSchoolTypesAsync(); return StatusCode(response.StatusCode, response); } 
        
        // ========================================================= // SCHOOL LEVEL // ========================================================= 
        // GET: api/MasterData/school-levels
        [HttpGet("school-levels")] 
        public async Task<IActionResult> GetSchoolLevels() { var response = await _masterDataService.GetSchoolLevelsAsync(); return StatusCode(response.StatusCode, response); }

        // ========================================================= // SCHOOL LEVEL // ========================================================= 
        // GET: api/MasterData/school-levels
        [HttpGet("role")]
        public async Task<IActionResult> GetRoles() { var response = await _masterDataService.GetRolesAsync(); return StatusCode(response.StatusCode, response); }



    }
}
