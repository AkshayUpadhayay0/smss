using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // Leave types: a global lookup, readable by any signed-in user.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class LeaveTypeController : ControllerBase
    {
        private readonly ILeaveTypeService _service;
        public LeaveTypeController(ILeaveTypeService service) => _service = service;

        // GET api/LeaveType?appliesTo=employee|student
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string? appliesTo, CancellationToken ct)
        {
            var r = await _service.GetAsync(appliesTo, ct);
            return StatusCode(r.StatusCode, r);
        }
    }

    // Employee leave. The school and the acting user always come from the token. Who may do what:
    //   apply:   the caller for themselves, or a School Admin for any employee   cancel: the applicant or a School Admin (checked in the service)
    //   decide / list all:  CanApproveLeave (School Admin or Principal)           my: any employee
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class EmployeeLeaveController : ControllerBase
    {
        private readonly IEmployeeLeaveService _service;
        public EmployeeLeaveController(IEmployeeLeaveService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
        private bool TryUser(out long userId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

        // POST api/EmployeeLeave   Body: { employeeId?, leaveTypeId, fromDate, toDate, reason }
        [HttpPost]
        public async Task<IActionResult> Apply([FromBody] ApplyEmployeeLeaveRequest request, CancellationToken ct)
        {
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _service.ApplyAsync(User.GetSchoolId(), userId, User.IsSchoolAdmin(), request, ct));
        }

        // PUT api/EmployeeLeave/{id}/decide   Body: { decision: "Approve" | "Reject", decisionReason? }   (reject needs the reason)
        [HttpPut("{id:long}/decide")]
        public async Task<IActionResult> Decide(long id, [FromBody] DecideLeaveRequest request, CancellationToken ct)
        {
            if (!User.CanApproveLeave()) return this.Forbidden("Only a School Admin or a Principal can decide leave requests.");
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _service.DecideAsync(User.GetSchoolId(), userId, true, id, request, ct));
        }

        // POST api/EmployeeLeave/{id}/cancel   (the applicant or a School Admin; Pending only)
        [HttpPost("{id:long}/cancel")]
        public async Task<IActionResult> Cancel(long id, CancellationToken ct)
        {
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _service.CancelAsync(User.GetSchoolId(), userId, User.IsSchoolAdmin(), id, ct));
        }

        // GET api/EmployeeLeave/my   (the caller's own requests)
        [HttpGet("my")]
        public async Task<IActionResult> My(CancellationToken ct)
        {
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _service.GetMyAsync(User.GetSchoolId(), userId, ct));
        }

        // GET api/EmployeeLeave?status=Pending&employeeId=   (School Admin / Principal)
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? employeeId, CancellationToken ct)
        {
            if (!User.CanApproveLeave()) return this.Forbidden("Only a School Admin or a Principal can see all leave requests.");
            return Reply(await _service.ListAsync(User.GetSchoolId(), true, status, employeeId, ct));
        }
    }

    // Student leave, recorded by a School Admin or the student's class teacher. The service checks WHICH class teacher against the Class-Teacher module.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class StudentLeaveController : ControllerBase
    {
        private readonly IStudentLeaveService _service;
        public StudentLeaveController(IStudentLeaveService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
        private bool TryUser(out long userId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

        // POST api/StudentLeave   Body: { studentId, leaveTypeId, fromDate, toDate, reason }
        [HttpPost]
        public async Task<IActionResult> Apply([FromBody] ApplyStudentLeaveRequest request, CancellationToken ct)
        {
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _service.ApplyAsync(User.GetSchoolId(), userId, User.IsSchoolAdmin(), request, ct));
        }

        // PUT api/StudentLeave/{id}/decide   Body: { decision: "Approve" | "Reject", decisionReason? }
        [HttpPut("{id:long}/decide")]
        public async Task<IActionResult> Decide(long id, [FromBody] DecideLeaveRequest request, CancellationToken ct)
        {
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _service.DecideAsync(User.GetSchoolId(), userId, User.IsSchoolAdmin(), id, request, ct));
        }

        // POST api/StudentLeave/{id}/cancel   (a School Admin or the student's class teacher; Pending only)
        [HttpPost("{id:long}/cancel")]
        public async Task<IActionResult> Cancel(long id, CancellationToken ct)
        {
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _service.CancelAsync(User.GetSchoolId(), userId, User.IsSchoolAdmin(), id, ct));
        }

        // GET api/StudentLeave?sectionId=&status=   (School Admin: all; a class teacher: their own section(s) only)
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] long? sectionId, [FromQuery] string? status, CancellationToken ct)
        {
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _service.ListAsync(User.GetSchoolId(), userId, User.IsSchoolAdmin(), sectionId, status, ct));
        }
    }
}
