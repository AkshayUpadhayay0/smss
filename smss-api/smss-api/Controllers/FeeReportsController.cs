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

        // GET api/FeeReports/concessions-given?from=2026-11-01&to=2026-11-30&page=1&pageSize=25
        // The audit list of concessions given at payment time. School Admin, Accountant and Principal (read-only); the school comes from the token.
        [HttpGet("concessions-given")]
        public async Task<IActionResult> GetConcessionsGiven([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
        {
            if (!User.CanViewFees())
                return StatusCode(403, new ApiResponse<object> { Status = false, StatusCode = 403, Message = "You do not have access to the fee reports.", Data = null });
            var response = await _service.GetConcessionsGivenAsync(User.GetSchoolId(), from, to, page, pageSize, ct);
            return StatusCode(response.StatusCode, response);
        }

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
