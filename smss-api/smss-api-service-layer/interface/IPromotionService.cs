using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId always comes from the caller's token; a year, class or section of another school answers 404.
    public interface IPromotionService
    {
        /// <summary>What promoting one class (or section) into a later year WOULD do. Read-only: nothing is written.</summary>
        Task<ApiResponse<PromotionPreviewDto>> PreviewAsync(string? schoolId, PromotionPreviewRequest request, CancellationToken ct);
    }
}
