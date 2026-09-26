using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;

namespace smss_api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SchoolRegistrationController : ControllerBase
    {
        private readonly ISchoolRegistrationService _schoolRegistrationService;

        public SchoolRegistrationController(ISchoolRegistrationService schoolRegistrationService)
        {
            _schoolRegistrationService = schoolRegistrationService;
        }

        // GET api/SchoolRegistration/schools
        [HttpGet("schools")]
        public async Task<IActionResult> GetAllSchools()
        {
            var response = await _schoolRegistrationService.GetSchoolsAsync();
            return StatusCode(response.StatusCode, response);
        }

        // GET api/SchoolRegistration/schools/sch2026001
        [HttpGet("schools/{schoolId}")]
        public async Task<IActionResult> GetSchoolById(string schoolId)
        {
            var response = await _schoolRegistrationService.GetSchoolByIdAsync(schoolId);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/SchoolRegistration/register
        [HttpPost("register")]
        public async Task<IActionResult> RegisterSchool([FromBody] CreateSchoolRequest request)
        {
            var response = await _schoolRegistrationService.RegisterSchoolAsync(request);
            return StatusCode(response.StatusCode, response);
        }

        // PUT api/SchoolRegistration/schools/sch2026001
        [HttpPut("schools/{schoolId}")]
        public async Task<IActionResult> UpdateSchool(string schoolId, [FromBody] UpdateSchoolRequest request)
        {
            var response = await _schoolRegistrationService.UpdateSchoolAsync(schoolId, request);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/SchoolRegistration/schools/sch2026001/toggle-status
        [HttpPost("schools/{schoolId}/toggle-status")]
        public async Task<IActionResult> ToggleSchoolStatus(string schoolId)
        {
            var response = await _schoolRegistrationService.ToggleSchoolStatusAsync(schoolId);
            return StatusCode(response.StatusCode, response);
        }

    }
}
