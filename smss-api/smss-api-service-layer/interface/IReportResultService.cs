using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    /// <summary>Results and publishing: the live result preview, publishing a locked term or year, versions, and the card PDFs.</summary>
    public interface IReportResultService
    {
        Task<ApiResponse<ResultPreviewDto>> PreviewAsync(string? schoolId, ReportCaller caller, long schemeId, long sectionId, string scope, long? termId, CancellationToken ct);
        Task<ApiResponse<PublishResultDto>> PublishAsync(string? schoolId, ReportCaller caller, PublishRequest req, CancellationToken ct);
        Task<ApiResponse<List<PublicationDto>>> ListAsync(string? schoolId, ReportCaller caller, long? sectionId, long? schemeId, CancellationToken ct);
        Task<ApiResponse<PublicationDetailDto>> GetAsync(string? schoolId, ReportCaller caller, long publicationId, CancellationToken ct);
        /// <summary>A published card re-drawn from the stored model (never from live data).</summary>
        Task<ReportFileResult> CardPdfAsync(string? schoolId, ReportCaller caller, long publicationId, string studentId, CancellationToken ct);
        /// <summary>A card drawn from live data with a DRAFT watermark. School Admin and the section's class teacher only.</summary>
        Task<ReportFileResult> DraftPdfAsync(string? schoolId, ReportCaller caller, long schemeId, long sectionId, string scope, long? termId, string studentId, CancellationToken ct);

        Task<ApiResponse<List<MyReportCardDto>>> MyCardsAsync(string? schoolId, long userId, CancellationToken ct);
        Task<ApiResponse<MyReportCardDetailDto>> MyCardAsync(string? schoolId, long userId, long publicationId, CancellationToken ct);
        Task<ReportFileResult> MyCardPdfAsync(string? schoolId, long userId, long publicationId, CancellationToken ct);
    }
}
