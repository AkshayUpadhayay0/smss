using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // A student's fees. The school comes from the token only (no school id in the route), and the acting user for a
    // concession comes from the token too.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class StudentFeeController : ControllerBase
    {
        private readonly IStudentFeeService _service;
        public StudentFeeController(IStudentFeeService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

        // GET api/StudentFee/{studentId}
        // The assigned structure's lines, applied concessions, net payable per frequency, and what can still be assigned / applied.
        [HttpGet("{studentId:length(1,20)}")]
        public async Task<IActionResult> GetFees(string studentId, CancellationToken ct) =>
            Reply(await _service.GetFeesAsync(User.GetSchoolId(), studentId, ct));

        // PUT api/StudentFee/{studentId}/structure
        [HttpPut("{studentId:length(1,20)}/structure")]
        public async Task<IActionResult> AssignStructure(string studentId, [FromBody] AssignFeeStructureRequest request, CancellationToken ct) =>
            Reply(await _service.AssignStructureAsync(User.GetSchoolId(), studentId, request, ct));

        // POST api/StudentFee/{studentId}/concessions
        [HttpPost("{studentId:length(1,20)}/concessions")]
        public async Task<IActionResult> ApplyConcession(string studentId, [FromBody] ApplyConcessionRequest request, CancellationToken ct)
        {
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            return Reply(await _service.ApplyConcessionAsync(User.GetSchoolId(), studentId, userId, request, ct));
        }

        // DELETE api/StudentFee/{studentId}/concessions/{concessionId}
        [HttpDelete("{studentId:length(1,20)}/concessions/{concessionId:long}")]
        public async Task<IActionResult> RemoveConcession(string studentId, long concessionId, CancellationToken ct) =>
            Reply(await _service.RemoveConcessionAsync(User.GetSchoolId(), studentId, concessionId, ct));
    }
}
