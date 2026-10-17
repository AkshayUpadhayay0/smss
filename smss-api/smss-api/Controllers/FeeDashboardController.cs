using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // Read-only fee dashboard. School Admin / Accountant / Principal may view it (CanViewFees); the Principal gets `canManage = false`
    // in the response (same numbers, no actions). The school always comes from the token.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class FeeDashboardController : ControllerBase
    {
        private readonly IFeeReportService _service;
        public FeeDashboardController(IFeeReportService service) => _service = service;

        // GET api/FeeDashboard?academicYearId=1&classId=&sectionId=&days=7
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] long? academicYearId, [FromQuery] long? classId, [FromQuery] long? sectionId, [FromQuery] int? days, CancellationToken ct)
        {
            if (!User.CanViewFees())
                return StatusCode(403, new ApiResponse<object> { Status = false, StatusCode = 403, Message = "You do not have access to the fee dashboard.", Data = null });
            if (academicYearId is null or <= 0)
                return StatusCode(400, new ApiResponse<object> { Status = false, StatusCode = 400, Message = "academicYearId is required.", Data = null });
            var response = await _service.GetDashboardAsync(User.GetSchoolId(), academicYearId.Value, classId, sectionId, days ?? 7, User.CanManageFees(), ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
