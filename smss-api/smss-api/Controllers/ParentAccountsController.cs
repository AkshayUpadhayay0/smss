using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // Staff side of parent accounts. The school ALWAYS comes from the token; a guardian of another school is a 404. Nothing here ever returns or accepts a password, a token or a link: the admin only asks the system to
    // send one. Status and Resend: the people who confirm admissions (School Admin, Principal, Admission Staff). Reset and bulk creation: School Admin only.
    [ApiController]
    [Route("api/ParentAccounts")]
    [Authorize]
    public class ParentAccountsController : ControllerBase
    {
        private readonly IParentAccountService _service;
        public ParentAccountsController(IParentAccountService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> r) => StatusCode(r.StatusCode, r);
        private IActionResult? Gate(bool allowed, string message, out string schoolId, out long userId)
        {
            schoolId = User.GetSchoolId() ?? string.Empty; userId = 0;
            if (!allowed) return this.Forbidden(message);
            if (schoolId.Length == 0) return StatusCode(400, new ApiResponse<object> { Status = false, StatusCode = 400, Message = smss_api_service_layer.helper.TenantMessages.NoSchool });
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId)) return Unauthorized();
            return null;
        }

        // GET api/ParentAccounts/{guardianId}   the account state and the recent deliveries
        [HttpGet("{guardianId:long}")]
        public async Task<IActionResult> Status(long guardianId, CancellationToken ct)
        {
            if (Gate(User.CanManageApplications(), "Only the School Admin, Principal or Admission Staff can see parent accounts.", out var school, out _) is { } refused) return refused;
            return Reply(await _service.GetStatusAsync(school, guardianId, ct));
        }

        // POST api/ParentAccounts/{guardianId}/resend   a new set-password link (3 resends per 24 hours)
        [HttpPost("{guardianId:long}/resend")]
        public async Task<IActionResult> Resend(long guardianId, CancellationToken ct)
        {
            if (Gate(User.CanManageApplications(), "Only the School Admin, Principal or Admission Staff can resend a parent link.", out var school, out var user) is { } refused) return refused;
            return Reply(await _service.ResendSetPasswordLinkAsync(school, guardianId, user, ct));
        }

        // POST api/ParentAccounts/{guardianId}/reset   the School Admin's reset: sends a reset link (the admin never sees or sets a password)
        [HttpPost("{guardianId:long}/reset")]
        public async Task<IActionResult> Reset(long guardianId, CancellationToken ct)
        {
            if (Gate(User.IsSchoolAdmin(), "Only the School Admin can reset a parent's password.", out var school, out var user) is { } refused) return refused;
            return Reply(await _service.AdminResetAsync(school, guardianId, user, ct));
        }

        // GET api/ParentAccounts/bulk/preview   how many guardians the next run would create (School Admin)
        [HttpGet("bulk/preview")]
        public async Task<IActionResult> BulkPreview(CancellationToken ct)
        {
            if (Gate(User.IsSchoolAdmin(), "Only the School Admin can create parent accounts in bulk.", out var school, out _) is { } refused) return refused;
            return Reply(await _service.BulkPreviewAsync(school, ct));
        }

        // POST api/ParentAccounts/bulk/create   { confirmSchoolHoldsParentConsent: true }   at most 100 per run (School Admin)
        [HttpPost("bulk/create")]
        public async Task<IActionResult> BulkCreate([FromBody] BulkParentAccountRequest request, CancellationToken ct)
        {
            if (Gate(User.IsSchoolAdmin(), "Only the School Admin can create parent accounts in bulk.", out var school, out var user) is { } refused) return refused;
            return Reply(await _service.BulkCreateAsync(school, user, request, ct));
        }
    }
}
