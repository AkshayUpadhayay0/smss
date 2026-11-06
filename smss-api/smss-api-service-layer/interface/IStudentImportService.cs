using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    public class ImportFileResult
    {
        public byte[]? Content { get; set; }
        public string FileName { get; set; } = "";
        public int StatusCode { get; set; } = 200;
        public string Message { get; set; } = "";
    }

    // schoolId always comes from the caller's token (the controller guarantees it).
    public interface IStudentImportService
    {
        Task<ImportFileResult> TemplateAsync(string schoolId, CancellationToken ct);
        /// <summary>Dry run: validates everything, writes nothing.</summary>
        Task<ApiResponse<ImportPreviewDto>> PreviewAsync(string schoolId, byte[] file, CancellationToken ct);
        /// <summary>Re-validates the file from scratch and, only if there is no error, writes every student in one transaction.</summary>
        Task<ApiResponse<ImportResultDto>> CommitAsync(string schoolId, byte[] file, CancellationToken ct);
        /// <summary>The uploaded rows again, with a Status and Messages column, in the same layout so it can be fixed and uploaded again.</summary>
        Task<ImportFileResult> ErrorReportAsync(string schoolId, byte[] file, CancellationToken ct);
    }
}
