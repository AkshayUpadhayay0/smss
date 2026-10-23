using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // Employee management. School Admin only, reads included (personal and contact details): the project's [Authorize(Roles)] attributes are
    // switched off, so the role is checked here from the token (IsSchoolAdmin). The school always comes from the token; no school id is accepted.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class EmployeeController : ControllerBase
    {
        private readonly IEmployeeService _service;
        public EmployeeController(IEmployeeService service) => _service = service;

        private IActionResult? Refuse() =>
            User.IsSchoolAdmin() ? null : this.Forbidden("Only a School Admin can manage employees.");
        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
        private IActionResult BadRequestMessage(string message) =>
            StatusCode(400, new ApiResponse<object> { Status = false, StatusCode = 400, Message = message, Data = null });

        // GET api/Employee/lookups   (designations, departments, employment statuses, genders, blood groups, employee document types, assignable roles)
        [HttpGet("lookups")]
        public async Task<IActionResult> GetLookups(CancellationToken ct) =>
            Refuse() ?? Reply(await _service.GetLookupsAsync(User.GetSchoolId(), ct));

        // GET api/Employee?page=1&pageSize=10&search=&designationId=&departmentId=&employmentStatusId=&employeeType=&status=All|Active|Inactive
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] long? designationId, [FromQuery] long? departmentId, [FromQuery] long? employmentStatusId,
            [FromQuery] string? employeeType, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default) =>
            Refuse() ?? Reply(await _service.ListAsync(User.GetSchoolId(), search, designationId, departmentId, employmentStatusId, employeeType, status, page, pageSize, ct));

        // GET api/Employee/{employeeId}
        [HttpGet("{employeeId:length(1,20)}")]
        public async Task<IActionResult> Get(string employeeId, CancellationToken ct) =>
            Refuse() ?? Reply(await _service.GetAsync(User.GetSchoolId(), employeeId, ct));

        // POST api/Employee
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] EmployeeRequest request, CancellationToken ct) =>
            Refuse() ?? Reply(await _service.CreateAsync(User.GetSchoolId(), request, ct));

        // PUT api/Employee/{employeeId}
        [HttpPut("{employeeId:length(1,20)}")]
        public async Task<IActionResult> Update(string employeeId, [FromBody] EmployeeRequest request, CancellationToken ct) =>
            Refuse() ?? Reply(await _service.UpdateAsync(User.GetSchoolId(), employeeId, request, ct));

        // POST api/Employee/{employeeId}/toggle-status   (POST, not PATCH: PATCH is blocked in this environment)
        [HttpPost("{employeeId:length(1,20)}/toggle-status")]
        public async Task<IActionResult> ToggleStatus(string employeeId, CancellationToken ct) =>
            Refuse() ?? Reply(await _service.ToggleStatusAsync(User.GetSchoolId(), employeeId, ct));

        // POST api/Employee/{employeeId}/photo   (multipart/form-data, field name: file)
        [HttpPost("{employeeId:length(1,20)}/photo")]
        [RequestSizeLimit(LogoRules.MaxRequestBytes)]
        public async Task<IActionResult> UploadPhoto(string employeeId, IFormFile? file, CancellationToken ct)
        {
            if (Refuse() is { } refused) return refused;
            if (file == null) return BadRequestMessage("Photo file is required.");
            await using var stream = file.OpenReadStream();
            return Reply(await _service.UploadPhotoAsync(User.GetSchoolId(), employeeId, stream, file.FileName, file.Length, ct));
        }

        // POST api/Employee/{employeeId}/photo/remove
        [HttpPost("{employeeId:length(1,20)}/photo/remove")]
        public async Task<IActionResult> RemovePhoto(string employeeId, CancellationToken ct) =>
            Refuse() ?? Reply(await _service.RemovePhotoAsync(User.GetSchoolId(), employeeId, ct));

        // GET api/Employee/{employeeId}/documents?includeRemoved=false
        [HttpGet("{employeeId:length(1,20)}/documents")]
        public async Task<IActionResult> GetDocuments(string employeeId, [FromQuery] bool includeRemoved = false, CancellationToken ct = default) =>
            Refuse() ?? Reply(await _service.GetDocumentsAsync(User.GetSchoolId(), employeeId, includeRemoved, ct));

        // POST api/Employee/{employeeId}/documents   (multipart/form-data: file, documentTypeId, remarks?)
        [HttpPost("{employeeId:length(1,20)}/documents")]
        [RequestSizeLimit(DocumentRules.MaxRequestBytes)]
        public async Task<IActionResult> UploadDocument(string employeeId, IFormFile? file, [FromForm] long? documentTypeId, [FromForm] string? remarks, CancellationToken ct)
        {
            if (Refuse() is { } refused) return refused;
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            if (file == null) return BadRequestMessage("A document file is required.");
            if (documentTypeId is null or <= 0) return BadRequestMessage("A document type is required.");
            await using var stream = file.OpenReadStream();
            return Reply(await _service.UploadDocumentAsync(User.GetSchoolId(), employeeId, documentTypeId.Value, remarks, userId, stream, file.FileName, file.Length, ct));
        }

        // GET api/Employee/{employeeId}/documents/{documentId}/download   (authenticated: the file is NOT in the public uploads folder)
        [HttpGet("{employeeId:length(1,20)}/documents/{documentId:long}/download")]
        public async Task<IActionResult> DownloadDocument(string employeeId, long documentId, CancellationToken ct)
        {
            if (Refuse() is { } refused) return refused;
            var (failure, content, contentType, fileName) = await _service.OpenDocumentAsync(User.GetSchoolId(), employeeId, documentId, ct);
            if (failure != null) return Reply(failure);
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Cache-Control"] = "private, no-store";
            return File(content!, contentType!, fileName!);
        }

        // POST api/Employee/{employeeId}/documents/{documentId}/remove
        [HttpPost("{employeeId:length(1,20)}/documents/{documentId:long}/remove")]
        public async Task<IActionResult> RemoveDocument(string employeeId, long documentId, CancellationToken ct) =>
            Refuse() ?? Reply(await _service.RemoveDocumentAsync(User.GetSchoolId(), employeeId, documentId, ct));

        // POST api/Employee/{employeeId}/create-login   Body: { roleIds: [..] }
        // Returns the generated login id + temporary password ONCE (it is never stored in plain text), like the student create-login.
        [HttpPost("{employeeId:length(1,20)}/create-login")]
        public async Task<IActionResult> CreateLogin(string employeeId, [FromBody] CreateEmployeeLoginRequest request, CancellationToken ct) =>
            Refuse() ?? Reply(await _service.CreateLoginAsync(User.GetSchoolId(), employeeId, request, ct));
    }
}
