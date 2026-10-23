using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // The school's admission applications. Two tiers, decided here from the token (the [Authorize(Roles)] attributes stay commented out project-wide):
    //   CanManageApplications (School Admin, Principal, Admission Staff): take applications in, edit them while undecided, move them to review, withdraw them,
    //       upload their documents, read everything, and Confirm an Approved application into a student (the only way one becomes a student)
    //   CanDecideAdmission (School Admin, Principal ONLY): approve and reject. Admission Staff never decide.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AdmissionController : ControllerBase
    {
        private readonly IAdmissionService _service;
        private readonly IAdmissionDocumentService _documents;
        public AdmissionController(IAdmissionService service, IAdmissionDocumentService documents)
        {
            _service = service;
            _documents = documents;
        }

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

        /// <summary>The role tier check + the own-school check, in the order every endpoint wants them.</summary>
        private IActionResult? Gate(string routeSchoolId, bool allowed, string refusal, out string schoolId)
        {
            schoolId = string.Empty;
            if (!allowed) return Respond(403, refusal);
            return RejectForeignSchool(routeSchoolId, out schoolId);
        }

        private const string ManageRefusal = "Only a School Admin, a Principal or Admission Staff can work with admission applications.";
        private const string DecideRefusal = "Only a School Admin or a Principal can approve or reject an application. Admission Staff can prepare and review it, but not decide it.";

        private IActionResult Respond(int code, string message) =>
            StatusCode(code, new ApiResponse<object> { Status = false, StatusCode = code, Message = message, Data = null });

        // The acting user always comes from the token. (The reviewedByUserId/approvedByUserId query values the first
        // draft of these routes used are no longer read: a client-supplied reviewer id could be forged.)
        private bool TryGetUserId(out long userId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

        // GET api/Admission/{schoolId}?status=UNDER_REVIEW&classId=&academicYearId=
        [HttpGet("{schoolId}")]
        public async Task<IActionResult> GetAll(string schoolId, [FromQuery] string? status, [FromQuery] long? classId, [FromQuery] long? academicYearId, CancellationToken ct)
        {
            if (Gate(schoolId, User.CanManageApplications(), ManageRefusal, out var own) is { } refused) return refused;
            return Reply(await _service.GetAllAsync(own, status, classId, academicYearId, ct));
        }

        [HttpGet("{schoolId}/{admissionId:long}")]
        public async Task<IActionResult> GetById(string schoolId, long admissionId, CancellationToken ct)
        {
            if (Gate(schoolId, User.CanManageApplications(), ManageRefusal, out var own) is { } refused) return refused;
            return Reply(await _service.GetByIdAsync(own, admissionId, ct));
        }

        [HttpPost("{schoolId}/submit")]
        public async Task<IActionResult> Submit(string schoolId, [FromBody] SubmitAdmissionRequestDto req, CancellationToken ct)
        {
            if (Gate(schoolId, User.CanManageApplications(), ManageRefusal, out var own) is { } refused) return refused;
            return Reply(await _service.SubmitAsync(own, req, ct));
        }

        // PUT api/Admission/{schoolId}/{admissionId}   Body: the same fields as submit. Only while Submitted or Under Review (409 after a decision).
        [HttpPut("{schoolId}/{admissionId:long}")]
        public async Task<IActionResult> Update(string schoolId, long admissionId, [FromBody] SubmitAdmissionRequestDto req, CancellationToken ct)
        {
            if (Gate(schoolId, User.CanManageApplications(), ManageRefusal, out var own) is { } refused) return refused;
            return Reply(await _service.UpdateAsync(own, admissionId, req, ct));
        }

        // POST .../start-review   Body (optional): { notes }
        [HttpPost("{schoolId}/{admissionId:long}/start-review")]
        public async Task<IActionResult> StartReview(string schoolId, long admissionId, [FromBody] AdmissionReviewRequestDto? req, CancellationToken ct)
        {
            if (Gate(schoolId, User.CanManageApplications(), ManageRefusal, out var own) is { } refused) return refused;
            if (!TryGetUserId(out var userId)) return Unauthorized();
            return Reply(await _service.StartReviewAsync(own, admissionId, userId, req, ct));
        }

        // POST .../approve   Body: { remarks? }   (must be Under Review)
        [HttpPost("{schoolId}/{admissionId:long}/approve")]
        public async Task<IActionResult> Approve(string schoolId, long admissionId, [FromBody] AdmissionDecisionRequestDto req, CancellationToken ct)
        {
            if (Gate(schoolId, User.CanDecideAdmission(), DecideRefusal, out var own) is { } refused) return refused;
            if (!TryGetUserId(out var userId)) return Unauthorized();
            return Reply(await _service.ApproveAsync(own, admissionId, userId, req, ct));
        }

        // POST .../reject   Body: { remarks }   (must be Under Review; the reason is required)
        [HttpPost("{schoolId}/{admissionId:long}/reject")]
        public async Task<IActionResult> Reject(string schoolId, long admissionId, [FromBody] AdmissionDecisionRequestDto req, CancellationToken ct)
        {
            if (Gate(schoolId, User.CanDecideAdmission(), DecideRefusal, out var own) is { } refused) return refused;
            if (!TryGetUserId(out var userId)) return Unauthorized();
            return Reply(await _service.RejectAsync(own, admissionId, userId, req, ct));
        }

        // POST .../cancel   (also .../withdraw)   The applicant pulled out: only before a decision has been made.
        [HttpPost("{schoolId}/{admissionId:long}/cancel")]
        [HttpPost("{schoolId}/{admissionId:long}/withdraw")]
        public async Task<IActionResult> Cancel(string schoolId, long admissionId, [FromBody] AdmissionDecisionRequestDto? req, CancellationToken ct)
        {
            if (Gate(schoolId, User.CanManageApplications(), ManageRefusal, out var own) is { } refused) return refused;
            return Reply(await _service.CancelAsync(own, admissionId, req ?? new AdmissionDecisionRequestDto(), ct));
        }

        // POST .../confirm   Creates the student + enrollment + guardian + fee structure from an APPROVED application: the single conversion path.
        // Needs the manage tier (School Admin, Principal, Admission Staff); before, any logged-in user of the school could call it.
        [HttpPost("{schoolId}/{admissionId:long}/confirm")]
        public async Task<IActionResult> Confirm(string schoolId, long admissionId, [FromBody] ConfirmAdmissionRequestDto req, CancellationToken ct)
        {
            if (Gate(schoolId, User.CanManageApplications(), ManageRefusal, out var own) is { } refused) return refused;
            return Reply(await _service.ConfirmAdmissionAsync(own, admissionId, req, ct));
        }

        // ---------------------------------------------------------------- documents (the Employee documents shape)
        // GET .../documents?includeRemoved=false
        [HttpGet("{schoolId}/{admissionId:long}/documents")]
        public async Task<IActionResult> GetDocuments(string schoolId, long admissionId, [FromQuery] bool includeRemoved = false, CancellationToken ct = default)
        {
            if (Gate(schoolId, User.CanManageApplications(), ManageRefusal, out var own) is { } refused) return refused;
            return Reply(await _documents.GetDocumentsAsync(own, admissionId, includeRemoved, ct));
        }

        // POST .../documents   (multipart/form-data: file, documentTypeId, remarks?)
        [HttpPost("{schoolId}/{admissionId:long}/documents")]
        [RequestSizeLimit(DocumentRules.MaxRequestBytes)]
        public async Task<IActionResult> UploadDocument(string schoolId, long admissionId, IFormFile? file, [FromForm] long? documentTypeId, [FromForm] string? remarks, CancellationToken ct)
        {
            if (Gate(schoolId, User.CanManageApplications(), ManageRefusal, out var own) is { } refused) return refused;
            if (!TryGetUserId(out var userId)) return Unauthorized();
            if (file == null) return Respond(400, "A document file is required.");
            if (documentTypeId is null or <= 0) return Respond(400, "A document type is required.");
            await using var stream = file.OpenReadStream();
            return Reply(await _documents.UploadDocumentAsync(own, admissionId, documentTypeId.Value, remarks, userId, stream, file.FileName, file.Length, ct));
        }

        // GET .../documents/{documentId}/download   (authenticated: the file is NOT in the public uploads folder)
        [HttpGet("{schoolId}/{admissionId:long}/documents/{documentId:long}/download")]
        public async Task<IActionResult> DownloadDocument(string schoolId, long admissionId, long documentId, CancellationToken ct)
        {
            if (Gate(schoolId, User.CanManageApplications(), ManageRefusal, out var own) is { } refused) return refused;
            var (failure, content, contentType, fileName) = await _documents.OpenDocumentAsync(own, admissionId, documentId, ct);
            if (failure != null) return Reply(failure);
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Cache-Control"] = "private, no-store";
            return File(content!, contentType!, fileName!);
        }

        // DELETE .../documents/{documentId}   (soft: the row is hidden, nothing is deleted)
        [HttpDelete("{schoolId}/{admissionId:long}/documents/{documentId:long}")]
        public async Task<IActionResult> RemoveDocument(string schoolId, long admissionId, long documentId, CancellationToken ct)
        {
            if (Gate(schoolId, User.CanManageApplications(), ManageRefusal, out var own) is { } refused) return refused;
            return Reply(await _documents.RemoveDocumentAsync(own, admissionId, documentId, ct));
        }
    }
}
