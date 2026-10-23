using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // Homework (publish + view). The school and the acting user always come from the token. Who may do what:
    //   create:                     School Admin (any section + subject) or a Teacher (only where the Timetable says they teach it: checked in the service)
    //   update / delete / attachment: the owning teacher or a School Admin (the service refuses any other teacher)
    //   GET my:                     a Teacher / School Admin: the homework they own          GET (list), GET section/{id}: School Admin only
    //   GET student/my:             a Student: their own current section's homework           GET {id}, GET attachment/{id}: owner, School Admin, or a student in that section
    // Principal / Accountant / Guardian have no access yet.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class HomeworkController : ControllerBase
    {
        private readonly IHomeworkService _service;
        public HomeworkController(IHomeworkService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
        private IActionResult BadRequestMessage(string message) =>
            StatusCode(400, new ApiResponse<object> { Status = false, StatusCode = 400, Message = message, Data = null });
        private bool TryCaller(out HomeworkCaller caller)
        {
            var ok = long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
            caller = new HomeworkCaller(userId, User.IsSchoolAdmin(), User.CanManageHomework() && !User.IsSchoolAdmin(), User.IsStudent());
            return ok;
        }
        private IActionResult? RefuseUnlessManager() => User.CanManageHomework() ? null : this.Forbidden("Only a School Admin or a Teacher can manage homework.");

        // POST api/Homework   Body: { academicYearId?, sectionId, subjectId, employeeId?, title, description?, assignedDate?, dueDate }
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateHomeworkRequest request, CancellationToken ct)
        {
            if (RefuseUnlessManager() is { } refused) return refused;
            if (!TryCaller(out var caller)) return Unauthorized();
            return Reply(await _service.CreateAsync(User.GetSchoolId(), caller, request, ct));
        }

        // PUT api/Homework/{id}   Body: { title, description?, assignedDate, dueDate }
        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] UpdateHomeworkRequest request, CancellationToken ct)
        {
            if (RefuseUnlessManager() is { } refused) return refused;
            if (!TryCaller(out var caller)) return Unauthorized();
            return Reply(await _service.UpdateAsync(User.GetSchoolId(), caller, id, request, ct));
        }

        // DELETE api/Homework/{id}   (soft: hidden, status Inactive)
        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id, CancellationToken ct)
        {
            if (RefuseUnlessManager() is { } refused) return refused;
            if (!TryCaller(out var caller)) return Unauthorized();
            return Reply(await _service.DeleteAsync(User.GetSchoolId(), caller, id, ct));
        }

        // GET api/Homework/{id}   (the owning teacher, a School Admin, or a student of that section)
        [HttpGet("{id:long}")]
        public async Task<IActionResult> Get(long id, CancellationToken ct)
        {
            if (!TryCaller(out var caller)) return Unauthorized();
            return Reply(await _service.GetAsync(User.GetSchoolId(), caller, id, ct));
        }

        // GET api/Homework/my?academicYearId=&sectionId=&subjectId=&from=&to=   (the homework the caller set; dates filter on the due date)
        [HttpGet("my")]
        public async Task<IActionResult> My([FromQuery] long? academicYearId, [FromQuery] long? sectionId, [FromQuery] long? subjectId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        {
            if (RefuseUnlessManager() is { } refused) return refused;
            if (!TryCaller(out var caller)) return Unauthorized();
            return Reply(await _service.GetMyAsync(User.GetSchoolId(), caller, academicYearId, sectionId, subjectId, from, to, ct));
        }

        // GET api/Homework?sectionId=&academicYearId=&subjectId=&from=&to=   (School Admin: the whole school)
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] long? sectionId, [FromQuery] long? academicYearId, [FromQuery] long? subjectId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
            !User.IsSchoolAdmin() ? this.Forbidden("Only a School Admin can see all homework.")
                                  : Reply(await _service.ListAsync(User.GetSchoolId(), academicYearId, sectionId, subjectId, from, to, ct));

        // GET api/Homework/section/{sectionId}?academicYearId=&from=&to=   (School Admin: everything assigned to the section, whoever set it)
        [HttpGet("section/{sectionId:long}")]
        public async Task<IActionResult> GetSection(long sectionId, [FromQuery] long? academicYearId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
            !User.IsSchoolAdmin() ? this.Forbidden("Only a School Admin can see a section's homework overview.")
                                  : Reply(await _service.GetSectionAsync(User.GetSchoolId(), sectionId, academicYearId, from, to, ct));

        // GET api/Homework/student/my?from=&to=   (a Student: their own current section, every subject, read-only)
        [HttpGet("student/my")]
        public async Task<IActionResult> StudentMy([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        {
            if (!User.IsStudent()) return this.Forbidden("Only a student can see their own homework here.");
            if (!TryCaller(out var caller)) return Unauthorized();
            return Reply(await _service.GetStudentMyAsync(User.GetSchoolId(), caller, from, to, ct));
        }

        // POST api/Homework/{id}/attachment   (multipart/form-data: file)   one attachment per homework; sending another replaces it
        [HttpPost("{id:long}/attachment")]
        [RequestSizeLimit(DocumentRules.MaxRequestBytes)]
        public async Task<IActionResult> UploadAttachment(long id, IFormFile? file, CancellationToken ct)
        {
            if (RefuseUnlessManager() is { } refused) return refused;
            if (!TryCaller(out var caller)) return Unauthorized();
            if (file == null) return BadRequestMessage("An attachment file is required.");
            await using var stream = file.OpenReadStream();
            return Reply(await _service.UploadAttachmentAsync(User.GetSchoolId(), caller, id, stream, file.FileName, file.Length, ct));
        }

        // POST api/Homework/{id}/attachment/remove
        [HttpPost("{id:long}/attachment/remove")]
        public async Task<IActionResult> RemoveAttachment(long id, CancellationToken ct)
        {
            if (RefuseUnlessManager() is { } refused) return refused;
            if (!TryCaller(out var caller)) return Unauthorized();
            return Reply(await _service.RemoveAttachmentAsync(User.GetSchoolId(), caller, id, ct));
        }

        // GET api/Homework/attachment/{id}   (authenticated download: the file is NOT in the public uploads folder; the same visibility as the homework row)
        [HttpGet("attachment/{id:long}")]
        public async Task<IActionResult> DownloadAttachment(long id, CancellationToken ct)
        {
            if (!TryCaller(out var caller)) return Unauthorized();
            var (failure, content, contentType, fileName) = await _service.OpenAttachmentAsync(User.GetSchoolId(), caller, id, ct);
            if (failure != null) return Reply(failure);
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Cache-Control"] = "private, no-store";
            return File(content!, contentType!, fileName!);
        }
    }
}
