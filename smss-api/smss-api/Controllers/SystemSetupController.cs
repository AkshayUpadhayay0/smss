using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using System.Security.Cryptography;
using System.Text;

namespace smss_api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SystemSetupController : ControllerBase
    {
        private readonly ISystemSetupService _service;
        private readonly IConfiguration _config;

        public SystemSetupController(ISystemSetupService service, IConfiguration config)
        {
            _service = service;
            _config = config;
        }

        [HttpPost("bootstrap-super-admin")]
        public async Task<IActionResult> BootstrapSuperAdmin([FromBody] CreateSuperAdminRequest request, [FromHeader(Name = "X-Setup-Key")] string? setupKey)
        {
            if (!_config.GetValue<bool>("Bootstrap:Enabled"))
                return NotFound();

            var expectedKey = _config["Bootstrap:SuperAdminSetupKey"];

            if (string.IsNullOrEmpty(expectedKey) || string.IsNullOrEmpty(setupKey) || !FixedTimeEquals(setupKey, expectedKey))
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Status = false,
                    StatusCode = 401,
                    Message = "Invalid or missing setup key.",
                    Data = null
                });
            }

            var response = await _service.BootstrapSuperAdminAsync(request);
            return StatusCode(response.StatusCode, response);
        }



        // Constant-time string comparison so response timing can't leak the correct key
        private static bool FixedTimeEquals(string a, string b)
        {
            var aBytes = Encoding.UTF8.GetBytes(a);
            var bBytes = Encoding.UTF8.GetBytes(b);
            if (aBytes.Length != bBytes.Length) return false;
            return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
        }
    }
}
