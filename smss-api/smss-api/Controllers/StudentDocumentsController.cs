using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // A student's documents (the required ones are set on the Document Types screen). The school always comes from the token, never from the request. Who may do what:
    //   School Admin, Principal, Admission Staff (CanManageApplications): upload, waive, void, download, read every student, and the pending list and count.
    //   Teacher: read the STATUSES (what is submitted, waived or pending) of the students in the section they are the class teacher of, nothing else: no files, no list, no changes.
    //   Accountant, Student, Guardian, Driver and every other role: no access in this stage (403).
    // Documents never block an admission or any other flow.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class StudentDocumentsController : ControllerBase
    {
        private readonly IStudentDocumentService _service;
        public StudentDocumentsController(IStudentDocumentService service) => _service = service;

        private const string ManageRefusal = "Only a School Admin, a Principal or Admission Staff can manage student documents.";
        private const string ReadRefusal = "You do not have access to student documents.";

        private IActionResult Respond(int code, string message) =>
            StatusCode(code, new ApiResponse<object> { Status = false, StatusCode = code, Message = message, Data = null });

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

        private bool TryGetUserId(out long userId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

        /// <summary>The caller's school from the token, or a 400 when the token has none.</summary>
        private IActionResult? School(out string schoolId)
        {
            schoolId = User.GetSchoolId() ?? string.Empty;
            return schoolId.Length == 0 ? Respond(400, TenantMessages.NoSchool) : null;
        }

        // GET api/StudentDocuments/{studentId}
        [HttpGet("{studentId}")]
        public async Task<IActionResult> Get(string studentId, CancellationToken ct)
        {
            var manage = User.CanManageApplications();
            var teacher = User.IsTeacher();
            if (!manage && !teacher) return Respond(403, ReadRefusal);
            if (School(out var school) is { } noSchool) return noSchool;
            if (!TryGetUserId(out var userId)) return Unauthorized();
            return Reply(await _service.GetAsync(school, studentId, new StudentDocumentCaller(userId, manage, teacher), ct));
        }

        // GET api/StudentDocuments/pending?classId=&sectionId=&search=
        [HttpGet("pending")]
        public async Task<IActionResult> Pending([FromQuery] long? classId, [FromQuery] long? sectionId, [FromQuery] string? search, CancellationToken ct)
        {
            if (!User.CanManageApplications()) return Respond(403, ManageRefusal);
            if (School(out var school) is { } noSchool) return noSchool;
            return Reply(await _service.ListPendingAsync(school, classId, sectionId, search, ct));
        }

        // GET api/StudentDocuments/pending/count   (the dashboard tiles)
        [HttpGet("pending/count")]
        public async Task<IActionResult> PendingCount(CancellationToken ct)
        {
            if (!User.CanManageApplications()) return Respond(403, ManageRefusal);
            if (School(out var school) is { } noSchool) return noSchool;
            return Reply(await _service.CountPendingAsync(school, ct));
        }

        // POST api/StudentDocuments/{studentId}/upload   (multipart/form-data: file, documentTypeId, remarks?)
        [HttpPost("{studentId}/upload")]
        [RequestSizeLimit(DocumentRules.MaxRequestBytes)]
        public async Task<IActionResult> Upload(string studentId, IFormFile? file, [FromForm] long? documentTypeId, [FromForm] string? remarks, CancellationToken ct)
        {
            if (!User.CanManageApplications()) return Respond(403, ManageRefusal);
            if (School(out var school) is { } noSchool) return noSchool;
            if (!TryGetUserId(out var userId)) return Unauthorized();
            if (file == null) return Respond(400, "A document file is required.");
            if (documentTypeId is null or <= 0) return Respond(400, "A document type is required.");
            await using var stream = file.OpenReadStream();
            return Reply(await _service.UploadAsync(school, studentId, documentTypeId.Value, remarks, userId, stream, file.FileName, file.Length, ct));
        }

        // POST api/StudentDocuments/{studentId}/waive   Body: { documentTypeId, reason }
        [HttpPost("{studentId}/waive")]
        public async Task<IActionResult> Waive(string studentId, [FromBody] WaiveStudentDocumentRequestDto request, CancellationToken ct)
        {
            if (!User.CanManageApplications()) return Respond(403, ManageRefusal);
            if (School(out var school) is { } noSchool) return noSchool;
            if (!TryGetUserId(out var userId)) return Unauthorized();
            return Reply(await _service.WaiveAsync(school, studentId, request, userId, ct));
        }

        // POST api/StudentDocuments/{studentId}/{documentId}/void   Body (optional): { reason }   (a POST: PATCH is blocked in this environment)
        [HttpPost("{studentId}/{documentId:long}/void")]
        public async Task<IActionResult> Void(string studentId, long documentId, [FromBody] VoidStudentDocumentRequestDto? request, CancellationToken ct)
        {
            if (!User.CanManageApplications()) return Respond(403, ManageRefusal);
            if (School(out var school) is { } noSchool) return noSchool;
            if (!TryGetUserId(out var userId)) return Unauthorized();
            return Reply(await _service.VoidAsync(school, studentId, documentId, request?.Reason, userId, ct));
        }

        // GET api/StudentDocuments/{studentId}/{documentId}/download   (authenticated: the file is NOT in the public uploads folder)
        [HttpGet("{studentId}/{documentId:long}/download")]
        public async Task<IActionResult> Download(string studentId, long documentId, CancellationToken ct)
        {
            if (!User.CanManageApplications()) return Respond(403, ManageRefusal);
            if (School(out var school) is { } noSchool) return noSchool;
            var (failure, content, contentType, fileName) = await _service.OpenAsync(school, studentId, documentId, ct);
            if (failure != null) return Reply(failure);
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Cache-Control"] = "private, no-store";
            return File(content!, contentType!, fileName!);
        }
    }
}
