using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // Late fee settings and the payment-screen preview. The rule is READ by School Admin, Accountant and Principal (CanViewFees: the Principal view-only) and CHANGED by the School Admin only (CanConfigureFees).
    // The preview is for whoever collects payments (CanManageFees: School Admin and Accountant). The school comes from the token only; a student or tax type of another school answers 404.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class LateFeeController : ControllerBase
    {
        private readonly ILateFeeService _service;
        private readonly ILateFeeReportService _report;
        public LateFeeController(ILateFeeService service, ILateFeeReportService report) { _service = service; _report = report; }

        // GET api/LateFee/report?from=2026-11-01&to=2026-11-30&classId=&view=charged|waived&page=1&pageSize=25
        // The Late fee report: School Admin, Accountant and Principal (CanViewFees; the Principal is read-only like everywhere in the fee reports). Voided payments are never in it.
        [HttpGet("report")]
        public async Task<IActionResult> Report([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] long? classId, [FromQuery] string? view, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
        {
            if (!User.CanViewFees()) return this.Forbidden("You do not have access to the fee reports.");
            var response = await _report.GetAsync(User.GetSchoolId(), from, to, classId, view, page, pageSize, ct);
            return StatusCode(response.StatusCode, response);
        }

        // GET api/LateFee/rule
        [HttpGet("rule")]
        public async Task<IActionResult> GetRule(CancellationToken ct)
        {
            if (!User.CanViewFees()) return this.Forbidden("Only a School Admin, an Accountant or a Principal can see the late fee settings.");
            var response = await _service.GetRuleAsync(User.GetSchoolId(), ct);
            return StatusCode(response.StatusCode, response);
        }

        // PUT api/LateFee/rule   body { enabled, ruleType, amount, percent, graceDays, capPerDue, taxApplies, feeTypeIds }
        [HttpPut("rule")]
        public async Task<IActionResult> SaveRule([FromBody] SaveLateFeeRuleRequest request, CancellationToken ct)
        {
            if (!User.CanConfigureFees()) return this.Forbidden("Only a School Admin can change the late fee settings.");
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            var response = await _service.SaveRuleAsync(User.GetSchoolId(), userId, request, ct);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/LateFee/preview/{studentId}   body { amount | allocations, taxTypeId? }   (a POST because it carries the allocations; it saves nothing)
        [HttpPost("preview/{studentId:length(1,20)}")]
        public async Task<IActionResult> Preview(string studentId, [FromBody] LateFeePreviewRequest request, CancellationToken ct)
        {
            if (!User.CanManageFees()) return this.Forbidden("Only a School Admin or an Accountant can collect a payment.");
            var response = await _service.PreviewAsync(User.GetSchoolId(), studentId, request, ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
