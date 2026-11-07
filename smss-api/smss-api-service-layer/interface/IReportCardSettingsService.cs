using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    /// <summary>Report card settings, the design chooser and the live preview. School Admin writes; the Principal reads; the school always comes from the token.</summary>
    public interface IReportCardSettingsService
    {
        Task<ApiResponse<ReportCardSettingsDto>> GetAsync(string? schoolId, CancellationToken ct);
        Task<ApiResponse<ReportCardSettingsDto>> SaveAsync(string? schoolId, long userId, ReportCardSettingsRequest request, CancellationToken ct);
        Task<ApiResponse<ReportCardSignatureDto>> UploadSignatureAsync(string? schoolId, long userId, Stream content, string fileName, long length, CancellationToken ct);
        Task<ApiResponse<ReportCardSignatureDto>> RemoveSignatureAsync(string? schoolId, long userId, CancellationToken ct);
        Task<ApiResponse<ReportCardSettingsDto>> SetSchemeDesignAsync(string? schoolId, long userId, SetSchemeDesignRequest request, CancellationToken ct);
        /// <summary>PNG pages of a card drawn with the given (possibly unsaved) settings: sample data, or one real student (School Admin and the section's class teacher only).</summary>
        Task<ApiResponse<ReportCardPreviewDto>> PreviewAsync(string? schoolId, ReportCaller caller, ReportCardPreviewRequest request, CancellationToken ct);
    }
}
