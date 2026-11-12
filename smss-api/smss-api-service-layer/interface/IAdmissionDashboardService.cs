using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    public interface IAdmissionDashboardService
    {
        Task<ApiResponse<AdmissionDashboardDto>> GetSummaryAsync(string? schoolId, CancellationToken ct);
    }
}
