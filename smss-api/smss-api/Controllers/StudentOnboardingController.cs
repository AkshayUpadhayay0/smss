using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class StudentOnboardingController : ControllerBase
    {
        private readonly IStudentOnboardingService _service;
        public StudentOnboardingController(IStudentOnboardingService service) => _service = service;

        // The route carries {schoolId}, but it is never trusted: the caller can only reach their OWN school (the JWT
        // "school_id" claim) and the service is always called with the token's value. TODO: drop {schoolId} from the
        // route once the frontend stops sending it (same pending item as Document Type and Admission).
        [HttpPost("{schoolId}/existing-student")]
        public async Task<IActionResult> CreateExistingStudent(string schoolId, [FromBody] DirectStudentEntryRequestDto request, CancellationToken ct)
        {
            var own = User.GetSchoolId() ?? string.Empty;
            if (own.Length == 0) return Reject(400, TenantMessages.NoSchool);
            if (!string.Equals(schoolId, own, StringComparison.OrdinalIgnoreCase))
                return Reject(403, "You can only onboard students into your own school.");

            // Creating a student straight in is a School Admin job (an Approved admission application becomes a student through Confirm Admission instead).
            if (!User.IsSchoolAdmin()) return Reject(403, "Only a School Admin can add a student directly.");

            var response = await _service.CreateDirectEntryAsync(own, request, ct);
            return StatusCode(response.StatusCode, response);
        }

        private IActionResult Reject(int code, string message) =>
            StatusCode(code, new ApiResponse<object> { Status = false, StatusCode = code, Message = message, Data = null });
    }
}
