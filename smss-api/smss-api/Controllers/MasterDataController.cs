using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using smss_api_service_layer.dto;
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



        // =========================================================
        // BOARD TYPE
        // =========================================================

        // GET: api/MasterData/board-types?includeInactive=true
        [HttpGet("board-types")]
        public async Task<IActionResult> GetBoardTypes([FromQuery] bool includeInactive = false)
        {
            // Inactive rows are for the admin screen only.
            //if (includeInactive && !User.IsInRole("SUPER_ADMIN"))
            //    return Forbid();

            var response = await _masterDataService.GetBoardTypesAsync(includeInactive);
            return StatusCode(response.StatusCode, response);
        }

        // POST: api/MasterData/board-types
        [HttpPost("board-types")]
        //[Authorize(Roles = "SUPER_ADMIN")]
        public async Task<IActionResult> CreateBoardType([FromBody] CreateBoardTypeRequestDto request)
        {
            var response = await _masterDataService.CreateBoardTypeAsync(request);
            return StatusCode(response.StatusCode, response);
        }

        // PUT: api/MasterData/board-types/{id}
        [HttpPut("board-types/{id:long}")]
        //[Authorize(Roles = "SUPER_ADMIN")]
        public async Task<IActionResult> UpdateBoardType(long id, [FromBody] UpdateBoardTypeRequestDto request)
        {
            var response = await _masterDataService.UpdateBoardTypeAsync(id, request);
            return StatusCode(response.StatusCode, response);
        }

        // POST: api/MasterData/board-types/{id}/toggle-status
        [HttpPost("board-types/{id:long}/toggle-status")]
        //[Authorize(Roles = "SUPER_ADMIN")]
        public async Task<IActionResult> ToggleBoardTypeStatus(long id)
        {
            var response = await _masterDataService.ToggleBoardTypeStatusAsync(id);
            return StatusCode(response.StatusCode, response);
        }

        // =========================================================
        // SCHOOL TYPE
        // =========================================================

        // GET: api/MasterData/school-types?includeInactive=true
        [HttpGet("school-types")]
        public async Task<IActionResult> GetSchoolTypes([FromQuery] bool includeInactive = false)
        {
            // TODO (auth): restore the SUPER_ADMIN check for includeInactive.
            // if (includeInactive && !User.IsInRole("SUPER_ADMIN")) return Forbid();

            var response = await _masterDataService.GetSchoolTypesAsync(includeInactive);
            return StatusCode(response.StatusCode, response);
        }

        // POST: api/MasterData/school-types
        [HttpPost("school-types")]
        // [Authorize(Roles = "SUPER_ADMIN")]  // TODO (auth)
        public async Task<IActionResult> CreateSchoolType([FromBody] CreateSchoolTypeRequestDto request)
        {
            var response = await _masterDataService.CreateSchoolTypeAsync(request);
            return StatusCode(response.StatusCode, response);
        }

        // PUT: api/MasterData/school-types/{id}
        [HttpPut("school-types/{id:long}")]
        // [Authorize(Roles = "SUPER_ADMIN")]  // TODO (auth)
        public async Task<IActionResult> UpdateSchoolType(long id, [FromBody] UpdateSchoolTypeRequestDto request)
        {
            var response = await _masterDataService.UpdateSchoolTypeAsync(id, request);
            return StatusCode(response.StatusCode, response);
        }

        // POST: api/MasterData/school-types/{id}/toggle-status
        [HttpPost("school-types/{id:long}/toggle-status")]
        // [Authorize(Roles = "SUPER_ADMIN")]  // TODO (auth)
        public async Task<IActionResult> ToggleSchoolTypeStatus(long id)
        {
            var response = await _masterDataService.ToggleSchoolTypeStatusAsync(id);
            return StatusCode(response.StatusCode, response);
        }

        // =========================================================
        // SCHOOL LEVEL
        // =========================================================

        // GET: api/MasterData/school-levels?includeInactive=true
        [HttpGet("school-levels")]
        public async Task<IActionResult> GetSchoolLevels([FromQuery] bool includeInactive = false)
        {
            // TODO (auth): restore the SUPER_ADMIN check for includeInactive.
            // if (includeInactive && !User.IsInRole("SUPER_ADMIN")) return Forbid();

            var response = await _masterDataService.GetSchoolLevelsAsync(includeInactive);
            return StatusCode(response.StatusCode, response);
        }

        // POST: api/MasterData/school-levels
        [HttpPost("school-levels")]
        // [Authorize(Roles = "SUPER_ADMIN")]  // TODO (auth)
        public async Task<IActionResult> CreateSchoolLevel([FromBody] CreateSchoolLevelRequestDto request)
        {
            var response = await _masterDataService.CreateSchoolLevelAsync(request);
            return StatusCode(response.StatusCode, response);
        }

        // PUT: api/MasterData/school-levels/{id}
        [HttpPut("school-levels/{id:long}")]
        // [Authorize(Roles = "SUPER_ADMIN")]  // TODO (auth)
        public async Task<IActionResult> UpdateSchoolLevel(long id, [FromBody] UpdateSchoolLevelRequestDto request)
        {
            var response = await _masterDataService.UpdateSchoolLevelAsync(id, request);
            return StatusCode(response.StatusCode, response);
        }

        // POST: api/MasterData/school-levels/{id}/toggle-status
        [HttpPost("school-levels/{id:long}/toggle-status")]
        // [Authorize(Roles = "SUPER_ADMIN")]  // TODO (auth)
        public async Task<IActionResult> ToggleSchoolLevelStatus(long id)
        {
            var response = await _masterDataService.ToggleSchoolLevelStatusAsync(id);
            return StatusCode(response.StatusCode, response);
        }

        // =========================================================
        // STATUS
        // =========================================================

        // GET: api/MasterData/status?includeInactive=true
        [HttpGet("status")]
        public async Task<IActionResult> GetStatus([FromQuery] bool includeInactive = false)
        {
            // TODO (auth): restore the SUPER_ADMIN check for includeInactive.
            // if (includeInactive && !User.IsInRole("SUPER_ADMIN")) return Forbid();

            var response = await _masterDataService.GetStatusAsync(includeInactive);
            return StatusCode(response.StatusCode, response);
        }

        // POST: api/MasterData/status
        [HttpPost("status")]
        // [Authorize(Roles = "SUPER_ADMIN")]  // TODO (auth)
        public async Task<IActionResult> CreateStatus([FromBody] CreateStatusRequestDto request)
        {
            var response = await _masterDataService.CreateStatusAsync(request);
            return StatusCode(response.StatusCode, response);
        }

        // PUT: api/MasterData/status/{id}
        [HttpPut("status/{id:int}")]
        // [Authorize(Roles = "SUPER_ADMIN")]  // TODO (auth)
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateStatusRequestDto request)
        {
            var response = await _masterDataService.UpdateStatusAsync(id, request);
            return StatusCode(response.StatusCode, response);
        }

        // POST: api/MasterData/status/{id}/toggle-status
        [HttpPost("status/{id:int}/toggle-status")]
        // [Authorize(Roles = "SUPER_ADMIN")]  // TODO (auth)
        public async Task<IActionResult> ToggleStatusActive(int id)
        {
            var response = await _masterDataService.ToggleStatusStatusAsync(id);
            return StatusCode(response.StatusCode, response);
        }


        // =========================================================
        // ROLES
        // =========================================================

        // GET: api/MasterData/role?includeInactive=true
        [HttpGet("role")]
        public async Task<IActionResult> GetRoles([FromQuery] bool includeInactive = false)
        {
            // TODO (auth): restore the SUPER_ADMIN check for includeInactive.
            // if (includeInactive && !User.IsInRole("SUPER_ADMIN")) return Forbid();

            var response = await _masterDataService.GetRolesAsync(includeInactive);
            return StatusCode(response.StatusCode, response);
        }

        // POST: api/MasterData/role
        [HttpPost("role")]
        // [Authorize(Roles = "SUPER_ADMIN")]  // TODO (auth)
        public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequestDto request)
        {
            var response = await _masterDataService.CreateRoleAsync(request);
            return StatusCode(response.StatusCode, response);
        }

        // PUT: api/MasterData/role/{id}
        [HttpPut("role/{id:long}")]
        // [Authorize(Roles = "SUPER_ADMIN")]  // TODO (auth)
        public async Task<IActionResult> UpdateRole(long id, [FromBody] UpdateRoleRequestDto request)
        {
            var response = await _masterDataService.UpdateRoleAsync(id, request);
            return StatusCode(response.StatusCode, response);
        }

        // POST: api/MasterData/role/{id}/toggle-status
        [HttpPost("role/{id:long}/toggle-status")]
        // [Authorize(Roles = "SUPER_ADMIN")]  // TODO (auth)
        public async Task<IActionResult> ToggleRoleStatus(long id)
        {
            var response = await _masterDataService.ToggleRoleStatusAsync(id);
            return StatusCode(response.StatusCode, response);
        }


    }
}
