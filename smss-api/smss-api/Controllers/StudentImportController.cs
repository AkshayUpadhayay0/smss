using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    /// <summary>
    /// Student Excel import. Same rule as direct student entry: School Admin only, and the school always comes from the token
    /// (there is no school id in any route or body here). Every action that reads an upload takes the file again: the commit
    /// validates the file from scratch and never trusts an earlier preview.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class StudentImportController : ControllerBase
    {
        private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        private readonly IStudentImportService _service;
        public StudentImportController(IStudentImportService service) => _service = service;

        [HttpGet("template")]
        public async Task<IActionResult> Template(CancellationToken ct)
        {
            var denied = Guard(out var school);
            if (denied != null) return denied;
            var file = await _service.TemplateAsync(school, ct);
            return file.Content == null ? Reject(file.StatusCode, file.Message) : File(file.Content, XlsxType, file.FileName);
        }

        [HttpPost("preview")]
        [RequestSizeLimit(3_000_000)]
        [RequestFormLimits(MultipartBodyLengthLimit = 3_000_000)]
        public async Task<IActionResult> Preview(IFormFile? file, CancellationToken ct)
        {
            var denied = Guard(out var school);
            if (denied != null) return denied;
            var (bytes, error) = await ReadUploadAsync(file, ct);
            if (bytes == null) return error!;
            var response = await _service.PreviewAsync(school, bytes, ct);
            return StatusCode(response.StatusCode, response);
        }

        [HttpPost("commit")]
        [RequestSizeLimit(3_000_000)]
        [RequestFormLimits(MultipartBodyLengthLimit = 3_000_000)]
        public async Task<IActionResult> Commit(IFormFile? file, CancellationToken ct)
        {
            var denied = Guard(out var school);
            if (denied != null) return denied;
            var (bytes, error) = await ReadUploadAsync(file, ct);
            if (bytes == null) return error!;
            var response = await _service.CommitAsync(school, bytes, ct);
            return StatusCode(response.StatusCode, response);
        }

        [HttpPost("error-report")]
        [RequestSizeLimit(3_000_000)]
        [RequestFormLimits(MultipartBodyLengthLimit = 3_000_000)]
        public async Task<IActionResult> ErrorReport(IFormFile? file, CancellationToken ct)
        {
            var denied = Guard(out var school);
            if (denied != null) return denied;
            var (bytes, error) = await ReadUploadAsync(file, ct);
            if (bytes == null) return error!;
            var report = await _service.ErrorReportAsync(school, bytes, ct);
            return report.Content == null ? Reject(report.StatusCode, report.Message) : File(report.Content, XlsxType, report.FileName);
        }

        private IActionResult? Guard(out string school)
        {
            school = User.GetSchoolId() ?? string.Empty;
            if (school.Length == 0) return Reject(400, TenantMessages.NoSchool);
            if (!User.IsSchoolAdmin()) return Reject(403, "Only a School Admin can import students.");
            return null;
        }

        /// <summary>The extension must be .xlsx (a .xlsm or .xls is refused here already); the file's real content is checked by the service.</summary>
        private async Task<(byte[]? Bytes, IActionResult? Error)> ReadUploadAsync(IFormFile? file, CancellationToken ct)
        {
            if (file == null || file.Length == 0) return (null, Reject(400, "Choose an .xlsx file to upload."));
            if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
                return (null, Reject(400, "Only .xlsx files are accepted. Download the template, fill it in and save it as an Excel Workbook (.xlsx)."));
            if (file.Length > ExcelFileGuard.MaxFileBytes) return (null, Reject(400, "The file is larger than 1 MB. Split it into smaller files."));
            using var ms = new MemoryStream((int)file.Length);
            await file.CopyToAsync(ms, ct);
            return (ms.ToArray(), null);
        }

        private IActionResult Reject(int code, string message) =>
            StatusCode(code, new ApiResponse<object> { Status = false, StatusCode = code, Message = message, Data = null });
    }
}
