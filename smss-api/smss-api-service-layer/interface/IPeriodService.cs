using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId is the caller's own school from the token; null / empty answers 400. The controller decides who may call.
    public interface IPeriodService
    {
        Task<ApiResponse<List<PeriodDto>>> ListAsync(string? schoolId, bool includeInactive, CancellationToken ct);
        Task<ApiResponse<PeriodDto>> CreateAsync(string? schoolId, PeriodRequest req, CancellationToken ct);
        Task<ApiResponse<PeriodDto>> UpdateAsync(string? schoolId, long periodId, PeriodRequest req, CancellationToken ct);
        Task<ApiResponse<PeriodDto>> ToggleStatusAsync(string? schoolId, long periodId, CancellationToken ct);
    }
}
