using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AdmissionController : ControllerBase
    {
        private readonly IAdmissionService _service;
        public AdmissionController(IAdmissionService service) => _service = service;

        // The routes carry {schoolId}, but it is never trusted: the caller can only reach their OWN school (the JWT
        // "school_id" claim) and the service is always called with the token's value. TODO: drop {schoolId} from the
        // routes once the frontend stops sending it (same pending item as Document Type).
        private IActionResult? RejectForeignSchool(string routeSchoolId, out string schoolId)
        {
            schoolId = User.GetSchoolId() ?? string.Empty;
            if (schoolId.Length == 0) return Respond(400, TenantMessages.NoSchool);
            if (!string.Equals(routeSchoolId, schoolId, StringComparison.OrdinalIgnoreCase))
                return Respond(403, "You can only manage your own school's admissions.");
            return null;
        }

        private IActionResult Respond(int code, string message) =>
            StatusCode(code, new ApiResponse<object> { Status = false, StatusCode = code, Message = message, Data = null });

        // The acting user always comes from the token. (The reviewedByUserId/approvedByUserId query values the first
        // draft of these routes used are no longer read: a client-supplied reviewer id could be forged.)
        private bool TryGetUserId(out long userId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

        [HttpGet("{schoolId}")]
        public async Task<IActionResult> GetAll(string schoolId, CancellationToken ct)
        {
            if (RejectForeignSchool(schoolId, out var own) is { } rejected) return rejected;
            return Reply(await _service.GetAllAsync(own, ct));
        }

        [HttpGet("{schoolId}/{admissionId:long}")]
        public async Task<IActionResult> GetById(string schoolId, long admissionId, CancellationToken ct)
        {
            if (RejectForeignSchool(schoolId, out var own) is { } rejected) return rejected;
            return Reply(await _service.GetByIdAsync(own, admissionId, ct));
        }

        [HttpPost("{schoolId}/submit")]
        public async Task<IActionResult> Submit(string schoolId, [FromBody] SubmitAdmissionRequestDto req, CancellationToken ct)
        {
            if (RejectForeignSchool(schoolId, out var own) is { } rejected) return rejected;
            return Reply(await _service.SubmitAsync(own, req, ct));
        }

        [HttpPost("{schoolId}/{admissionId:long}/start-review")]
        public async Task<IActionResult> StartReview(string schoolId, long admissionId, CancellationToken ct)
        {
            if (RejectForeignSchool(schoolId, out var own) is { } rejected) return rejected;
            if (!TryGetUserId(out var userId)) return Unauthorized();
            return Reply(await _service.StartReviewAsync(own, admissionId, userId, ct));
        }

        [HttpPost("{schoolId}/{admissionId:long}/approve")]
        public async Task<IActionResult> Approve(string schoolId, long admissionId, [FromBody] AdmissionDecisionRequestDto req, CancellationToken ct)
        {
            if (RejectForeignSchool(schoolId, out var own) is { } rejected) return rejected;
            if (!TryGetUserId(out var userId)) return Unauthorized();
            return Reply(await _service.ApproveAsync(own, admissionId, userId, req, ct));
        }

        [HttpPost("{schoolId}/{admissionId:long}/reject")]
        public async Task<IActionResult> Reject(string schoolId, long admissionId, [FromBody] AdmissionDecisionRequestDto req, CancellationToken ct)
        {
            if (RejectForeignSchool(schoolId, out var own) is { } rejected) return rejected;
            return Reply(await _service.RejectAsync(own, admissionId, req, ct));
        }

        [HttpPost("{schoolId}/{admissionId:long}/cancel")]
        public async Task<IActionResult> Cancel(string schoolId, long admissionId, [FromBody] AdmissionDecisionRequestDto req, CancellationToken ct)
        {
            if (RejectForeignSchool(schoolId, out var own) is { } rejected) return rejected;
            return Reply(await _service.CancelAsync(own, admissionId, req, ct));
        }

        [HttpPost("{schoolId}/{admissionId:long}/confirm")]
        public async Task<IActionResult> Confirm(string schoolId, long admissionId, [FromBody] ConfirmAdmissionRequestDto req, CancellationToken ct)
        {
            if (RejectForeignSchool(schoolId, out var own) is { } rejected) return rejected;
            return Reply(await _service.ConfirmAdmissionAsync(own, admissionId, req, ct));
        }
    }
}
