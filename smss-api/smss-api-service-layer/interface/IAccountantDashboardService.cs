using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    public interface IAccountantDashboardService
    {
        Task<ApiResponse<AccountantDashboardDto>> GetSummaryAsync(string? schoolId, CancellationToken ct);
    }
}
