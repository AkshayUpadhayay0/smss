using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // Late fee settings and the payment-screen preview. The rule is READ by School Admin, Accountant and Principal (CanViewFees: the Principal view-only) and CHANGED by the School Admin only (CanConfigureFees).
    // The school comes from the token only; a student or tax type of another school answers 404.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class LateFeeController : ControllerBase
    {
        private readonly ILateFeeService _service;
        public LateFeeController(ILateFeeService service) => _service = service;

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
    }
}
