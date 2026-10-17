using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class StudentController : ControllerBase
    {
        private readonly IStudentService _service;
        private readonly IStudentLoginService _loginService;

        public StudentController(IStudentService service, IStudentLoginService loginService)
        {
            _service = service;
            _loginService = loginService;
        }

        // The routes carry {schoolId}, but it is never trusted: the caller can only reach their OWN school (the JWT
        // "school_id" claim) and the service is always called with the token's value. TODO: drop {schoolId} from the
        // routes once the frontend stops sending it (same pending item as Document Type, Admission, Onboarding).
        private IActionResult? RejectForeignSchool(string routeSchoolId, out string schoolId)
        {
            schoolId = User.GetSchoolId() ?? string.Empty;
            if (schoolId.Length == 0) return Respond(400, TenantMessages.NoSchool);
            if (!string.Equals(routeSchoolId, schoolId, StringComparison.OrdinalIgnoreCase))
                return Respond(403, "You can only view your own school's students.");
            return null;
        }

        private IActionResult Respond(int code, string message) =>
            StatusCode(code, new ApiResponse<object> { Status = false, StatusCode = code, Message = message, Data = null });

        // GET api/Student/{schoolId}?academicYearId=1&classSectionId=5&feeStatus=Overdue   (feeStatus is optional; classSectionId = tb_sections.section_id)
        [HttpGet("{schoolId}")]
        public async Task<IActionResult> GetStudents(string schoolId, [FromQuery] long? academicYearId, [FromQuery] long? classSectionId, [FromQuery] string? feeStatus, CancellationToken ct)
        {
            if (RejectForeignSchool(schoolId, out var own) is { } rejected) return rejected;
            if (academicYearId is null or <= 0) return Respond(400, "academicYearId is required.");
            if (classSectionId is null or <= 0) return Respond(400, "classSectionId is required.");

            var response = await _service.GetStudentsAsync(own, academicYearId.Value, classSectionId.Value, feeStatus, ct);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/Student/{schoolId}/{studentId}/photo   (multipart/form-data, field name: file)
        [HttpPost("{schoolId}/{studentId:length(1,20)}/photo")]
        [RequestSizeLimit(LogoRules.MaxRequestBytes)]
        public async Task<IActionResult> UploadPhoto(string schoolId, string studentId, IFormFile? file, CancellationToken ct)
        {
            if (User.IsAccountantOnly()) return this.Forbidden("An Accountant cannot change a student's photo.");
            if (RejectForeignSchool(schoolId, out var own) is { } rejected) return rejected;
            if (file == null) return Respond(400, "Photo file is required.");

            await using var stream = file.OpenReadStream();
            var response = await _service.UploadPhotoAsync(own, studentId, stream, file.FileName, file.Length, ct);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/Student/{schoolId}/{studentId}/photo/remove
        [HttpPost("{schoolId}/{studentId:length(1,20)}/photo/remove")]
        public async Task<IActionResult> RemovePhoto(string schoolId, string studentId, CancellationToken ct)
        {
            if (User.IsAccountantOnly()) return this.Forbidden("An Accountant cannot change a student's photo.");
            if (RejectForeignSchool(schoolId, out var own) is { } rejected) return rejected;
            var response = await _service.RemovePhotoAsync(own, studentId, ct);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/Student/{schoolId}/{studentId}/create-login
        // Returns the generated login id + temporary password ONCE (it is never stored in plain text).
        [HttpPost("{schoolId}/{studentId:length(1,20)}/create-login")]
        public async Task<IActionResult> CreateLogin(string schoolId, string studentId, CancellationToken ct)
        {
            if (User.IsAccountantOnly()) return this.Forbidden("An Accountant cannot create a student login.");
            if (RejectForeignSchool(schoolId, out var own) is { } rejected) return rejected;
            var response = await _loginService.CreateStudentLoginAsync(own, studentId, ct);
            return StatusCode(response.StatusCode, response);
        }

        // GET api/Student/{schoolId}/{studentId}
        [HttpGet("{schoolId}/{studentId:length(1,20)}")]
        public async Task<IActionResult> GetStudentProfile(string schoolId, string studentId, CancellationToken ct)
        {
            if (RejectForeignSchool(schoolId, out var own) is { } rejected) return rejected;
            var response = await _service.GetStudentProfileAsync(own, studentId, limited: User.IsAccountantOnly(), ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
