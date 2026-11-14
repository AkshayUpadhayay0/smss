using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // Stage 3: students and transport areas.
    //  - api/StudentTransport/...  School Admin writes (assign, change, remove), School Admin and Principal read. Every other role, a Student and a Driver included, is refused with 403.
    //  - api/Transport/my-bus      Student only. The student comes from the TOKEN; nothing in the request names a student.
    // The role check is the FIRST statement of every action. The school always comes from the token; another school's ids are a 404.

    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class StudentTransportController : ControllerBase
    {
        private readonly IStudentTransportService _service;
        public StudentTransportController(IStudentTransportService service) => _service = service;
        private IActionResult Reply<T>(ApiResponse<T> r) => StatusCode(r.StatusCode, r);

        // GET api/StudentTransport/{studentId}?academicYearId=   (default: the current academic year)
        [HttpGet("{studentId:length(1,20)}")]
        public async Task<IActionResult> Get(string studentId, [FromQuery] long? academicYearId, CancellationToken ct)
        {
            if (!User.CanViewTransport()) return this.Forbidden("Only a School Admin or a Principal can view a student's transport.");
            return Reply(await _service.GetAsync(User.GetSchoolId(), studentId, academicYearId, ct));
        }

        // PUT api/StudentTransport/{studentId}   body { areaId, academicYearId? }   201 first assignment, 200 change or reactivation
        [HttpPut("{studentId:length(1,20)}")]
        public async Task<IActionResult> Assign(string studentId, [FromBody] StudentTransportRequest? request, CancellationToken ct)
        {
            if (!User.CanConfigureTransport()) return this.Forbidden("Only a School Admin can change a student's transport.");
            return Reply(await _service.AssignAsync(User.GetSchoolId(), studentId, request, ct));
        }

        // POST api/StudentTransport/{studentId}/remove?academicYearId=   (POST, not DELETE: it is a soft delete, and PATCH is blocked in this environment)
        [HttpPost("{studentId:length(1,20)}/remove")]
        public async Task<IActionResult> Remove(string studentId, [FromQuery] long? academicYearId, CancellationToken ct)
        {
            if (!User.CanConfigureTransport()) return this.Forbidden("Only a School Admin can change a student's transport.");
            return Reply(await _service.RemoveAsync(User.GetSchoolId(), studentId, academicYearId, ct));
        }
    }

    [ApiController]
    [Route("api/Transport")]
    [Authorize]
    public class StudentBusController : ControllerBase
    {
        private readonly IStudentTransportService _service;
        public StudentBusController(IStudentTransportService service) => _service = service;

        // GET api/Transport/my-bus   Student only. {assigned, area, buses[]}: assigned:false is a 200. Never cached.
        [HttpGet("my-bus")]
        public async Task<IActionResult> MyBus(CancellationToken ct)
        {
            if (!User.IsStudent()) return this.Forbidden("Only a Student can see their own bus.");
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            Response.Headers.CacheControl = "no-store";
            var r = await _service.GetMyBusAsync(User.GetSchoolId(), userId, ct);
            return StatusCode(r.StatusCode, r);
        }
    }
}
