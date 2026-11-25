using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId always comes from the caller's token; a year of another school answers 404.
    public interface IFeeRepairService
    {
        Task<ApiResponse<FeeRepairPreviewDto>> PreviewAsync(string? schoolId, long academicYearId, CancellationToken ct);
        Task<ApiResponse<FeeRepairResultDto>> RunAsync(string? schoolId, long academicYearId, CancellationToken ct);
    }
}
