using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // A student's fees. Reading a student's fees and fee dues: School Admin, Accountant, Principal (CanViewFees); `fee-items` is also readable by the student themselves (their own only); `preview` (a class's fee structure for the admission forms, no student data) is still open to any signed-in user. Changing how the student is billed: School Admin only (CanConfigureFees);
    // collecting / voiding payments is the FeePayment controller (School Admin + Accountant). The school comes from the token only (no school id in the route), and the acting user for a
    // concession comes from the token too.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class StudentFeeController : ControllerBase
    {
        private readonly IStudentFeeService _service;
        private readonly IStudentFeeBillingService _billing;

        public StudentFeeController(IStudentFeeService service, IStudentFeeBillingService billing)
        {
            _service = service;
            _billing = billing;
        }

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

        // GET api/StudentFee/preview?academicYearId=1&classId=2
        // The fee heads a new student of that class + year would get (with the frequencies each may be paid at), for the admission form.
        [HttpGet("preview")]
        public async Task<IActionResult> Preview([FromQuery] long? academicYearId, [FromQuery] long? classId, [FromQuery] DateOnly? startDate, CancellationToken ct)
        {
            if (academicYearId is null or <= 0 || classId is null or <= 0)
                return StatusCode(400, new ApiResponse<object> { Status = false, StatusCode = 400, Message = "academicYearId and classId are required.", Data = null });
            return Reply(await _billing.PreviewAsync(User.GetSchoolId(), academicYearId.Value, classId.Value, startDate, ct));
        }

        // GET api/StudentFee/{studentId}/fee-items
        // School Admin / Accountant / Principal (CanViewFees): any student of their school. A Student: only their OWN (student id from the token; another student = 404, not 403, so the id is not confirmed).
        // Every other role 403; another school's student 404.
        [HttpGet("{studentId:length(1,20)}/fee-items")]
        public async Task<IActionResult> GetFeeItems(string studentId, CancellationToken ct)
        {
            if (User.CanViewFees()) return Reply(await _billing.GetItemsAsync(User.GetSchoolId(), studentId, ct));
            if (User.IsStudent())
            {
                if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
                return Reply(await _billing.GetOwnItemsAsync(User.GetSchoolId(), userId, studentId, ct));
            }
            return this.Forbidden("Only a School Admin, Accountant, Principal or the student themselves can view a student's fee items.");
        }

        // PUT api/StudentFee/{studentId}/fee-items/{itemId}   (change the frequency and/or whether the fee head applies)
        [HttpPut("{studentId:length(1,20)}/fee-items/{itemId:long}")]
        public async Task<IActionResult> UpdateFeeItem(string studentId, long itemId, [FromBody] UpdateFeeItemRequest request, CancellationToken ct)
        {
            if (!User.CanConfigureFees()) return this.Forbidden("Only a School Admin can change how a student is billed.");
            return Reply(await _billing.UpdateItemAsync(User.GetSchoolId(), studentId, itemId, request, ct));
        }

        // GET api/StudentFee/{studentId}/fee-dues   (every installment with its computed status)
        [HttpGet("{studentId:length(1,20)}/fee-dues")]
        public async Task<IActionResult> GetFeeDues(string studentId, CancellationToken ct)
        {
            if (!User.CanViewFees()) return this.Forbidden("Only a School Admin, Accountant or Principal can view a student's fee dues.");
            return Reply(await _billing.GetDuesAsync(User.GetSchoolId(), studentId, ct));
        }

        // GET api/StudentFee/{studentId}
        // The assigned structure's lines, applied concessions, net payable per frequency, and what can still be assigned / applied.
        [HttpGet("{studentId:length(1,20)}")]
        public async Task<IActionResult> GetFees(string studentId, CancellationToken ct)
        {
            if (!User.CanViewFees()) return this.Forbidden("Only a School Admin, Accountant or Principal can view a student's fees.");
            return Reply(await _service.GetFeesAsync(User.GetSchoolId(), studentId, ct));
        }

        // PUT api/StudentFee/{studentId}/structure
        [HttpPut("{studentId:length(1,20)}/structure")]
        public async Task<IActionResult> AssignStructure(string studentId, [FromBody] AssignFeeStructureRequest request, CancellationToken ct)
        {
            if (!User.CanConfigureFees()) return this.Forbidden("Only a School Admin can assign a fee structure.");
            return Reply(await _service.AssignStructureAsync(User.GetSchoolId(), studentId, request, ct));
        }

        // POST api/StudentFee/{studentId}/concessions
        [HttpPost("{studentId:length(1,20)}/concessions")]
        public async Task<IActionResult> ApplyConcession(string studentId, [FromBody] ApplyConcessionRequest request, CancellationToken ct)
        {
            if (!User.CanConfigureFees()) return this.Forbidden("Only a School Admin can apply a concession.");
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            return Reply(await _service.ApplyConcessionAsync(User.GetSchoolId(), studentId, userId, request, ct));
        }

        // DELETE api/StudentFee/{studentId}/concessions/{concessionId}?academicYearId=   (the year is needed only when the student has it in several)
        [HttpDelete("{studentId:length(1,20)}/concessions/{concessionId:long}")]
        public async Task<IActionResult> RemoveConcession(string studentId, long concessionId, [FromQuery] long? academicYearId, CancellationToken ct)
        {
            if (!User.CanConfigureFees()) return this.Forbidden("Only a School Admin can remove a concession.");
            return Reply(await _service.RemoveConcessionAsync(User.GetSchoolId(), studentId, concessionId, academicYearId, ct));
        }
    }
}
