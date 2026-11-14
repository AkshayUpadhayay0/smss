using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;

namespace smss_api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class SchoolRegistrationController : ControllerBase
    {
        private readonly ISchoolRegistrationService _schoolRegistrationService;

        public SchoolRegistrationController(ISchoolRegistrationService schoolRegistrationService)
        {
            _schoolRegistrationService = schoolRegistrationService;
        }

        // GET api/SchoolRegistration/schools
        [HttpGet("schools")]
        [SuperAdminOnly]
        public async Task<IActionResult> GetAllSchools()
        {
            var response = await _schoolRegistrationService.GetSchoolsAsync();
            return StatusCode(response.StatusCode, response);
        }

        // GET api/SchoolRegistration/schools/sch2026001
        [HttpGet("schools/{schoolId}")]
        [SuperAdminOnly]
        public async Task<IActionResult> GetSchoolById(string schoolId)
        {
            var response = await _schoolRegistrationService.GetSchoolByIdAsync(schoolId);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/SchoolRegistration/register
        [HttpPost("register")]
        [SuperAdminOnly]
        public async Task<IActionResult> RegisterSchool([FromBody] CreateSchoolRequest request)
        {
            var response = await _schoolRegistrationService.RegisterSchoolAsync(request, HttpContext.RequestAborted);
            return StatusCode(response.StatusCode, response);
        }

        // PUT api/SchoolRegistration/schools/sch2026001
        [HttpPut("schools/{schoolId}")]
        [SuperAdminOnly]
        public async Task<IActionResult> UpdateSchool(string schoolId, [FromBody] UpdateSchoolRequest request)
        {
            var response = await _schoolRegistrationService.UpdateSchoolAsync(schoolId, request);
            return StatusCode(response.StatusCode, response);
        }

        // School id for "my school" endpoints comes from the token only; null for accounts without a school
        private string? TokenSchoolId => User.GetSchoolId();

        // GET api/SchoolRegistration/me
        [HttpGet("me")]
        public async Task<IActionResult> GetMySchool()
        {
            var response = await _schoolRegistrationService.GetMySchoolAsync(TokenSchoolId);
            return StatusCode(response.StatusCode, response);
        }

        // GET api/SchoolRegistration/me/brand   (every signed-in role of the school: name + public logo path only; school from the token)
        [HttpGet("me/brand")]
        public async Task<IActionResult> GetMyBrand()
        {
            var response = await _schoolRegistrationService.GetMyBrandAsync(TokenSchoolId);
            return StatusCode(response.StatusCode, response);
        }

        // PUT api/SchoolRegistration/me
        [HttpPut("me")]
        public async Task<IActionResult> UpdateMySchool([FromBody] UpdateMySchoolProfileRequest request)
        {
            if (!User.IsSchoolAdmin()) return this.Forbidden("Only a School Admin can change the school profile.");
            var response = await _schoolRegistrationService.UpdateMySchoolAsync(TokenSchoolId, request);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/SchoolRegistration/me/logo   (multipart/form-data, field name: file)
        [HttpPost("me/logo")]
        [RequestSizeLimit(MaxLogoRequestBytes)]
        public async Task<IActionResult> UploadMyLogo(IFormFile? file, CancellationToken ct)
        {
            if (!User.IsSchoolAdmin()) return this.Forbidden("Only a School Admin can change the school logo.");
            if (file == null)
                return BadRequest(new ApiResponse<object> { Status = false, StatusCode = 400, Message = "Logo file is required.", Data = null });

            await using var stream = file.OpenReadStream();
            var response = await _schoolRegistrationService.UploadMyLogoAsync(TokenSchoolId, stream, file.FileName, file.Length, ct);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/SchoolRegistration/me/logo/remove
        [HttpPost("me/logo/remove")]
        public async Task<IActionResult> RemoveMyLogo()
        {
            if (!User.IsSchoolAdmin()) return this.Forbidden("Only a School Admin can change the school logo.");
            var response = await _schoolRegistrationService.RemoveMyLogoAsync(TokenSchoolId);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/SchoolRegistration/schools/sch2026001/toggle-status
        [HttpPost("schools/{schoolId}/toggle-status")]
        [SuperAdminOnly]
        public async Task<IActionResult> ToggleSchoolStatus(string schoolId)
        {
            var response = await _schoolRegistrationService.ToggleSchoolStatusAsync(schoolId);
            return StatusCode(response.StatusCode, response);
        }


        private const long MaxLogoRequestBytes = LogoRules.MaxRequestBytes;

        // POST api/SchoolRegistration/schools/sch2026001/logo   (multipart/form-data, field name: file)
        [HttpPost("schools/{schoolId}/logo")]
        [SuperAdminOnly]
        [RequestSizeLimit(MaxLogoRequestBytes)]
        public async Task<IActionResult> UploadLogo(string schoolId, IFormFile? file, CancellationToken ct)
        {
            if (file == null)
                return BadRequest(new ApiResponse<object> { Status = false, StatusCode = 400, Message = "Logo file is required.", Data = null });

            await using var stream = file.OpenReadStream();
            var response = await _schoolRegistrationService.UploadLogoAsync(schoolId, stream, file.FileName, file.Length, ct);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/SchoolRegistration/schools/sch2026001/logo/remove
        [HttpPost("schools/{schoolId}/logo/remove")]
        [SuperAdminOnly]
        public async Task<IActionResult> RemoveLogo(string schoolId)
        {
            var response = await _schoolRegistrationService.RemoveLogoAsync(schoolId);
            return StatusCode(response.StatusCode, response);
        }
    }
}
