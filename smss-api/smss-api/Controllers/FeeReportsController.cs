using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // Read-only fee reports (same access as the dashboard). The student-wise statement is the student's Fees tab
    // (GET api/StudentFee/{id}/fee-items, /fee-dues and GET api/FeePayment/students/{id}), deliberately not duplicated here.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class FeeReportsController : ControllerBase
    {
        private readonly IFeeReportService _service;
        public FeeReportsController(IFeeReportService service) => _service = service;

        // GET api/FeeReports/class/{classId}?academicYearId=1&sectionId=
        [HttpGet("class/{classId:long}")]
        public async Task<IActionResult> GetClassReport(long classId, [FromQuery] long? academicYearId, [FromQuery] long? sectionId, CancellationToken ct)
        {
            if (!User.CanViewFees())
                return StatusCode(403, new ApiResponse<object> { Status = false, StatusCode = 403, Message = "You do not have access to the fee reports.", Data = null });
            if (academicYearId is null or <= 0)
                return StatusCode(400, new ApiResponse<object> { Status = false, StatusCode = 400, Message = "academicYearId is required.", Data = null });
            var response = await _service.GetClassReportAsync(User.GetSchoolId(), classId, academicYearId.Value, sectionId, User.CanManageFees(), ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
