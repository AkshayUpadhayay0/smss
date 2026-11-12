using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    public interface IPrincipalDashboardService
    {
        Task<ApiResponse<PrincipalDashboardDto>> GetSummaryAsync(string? schoolId, long callerUserId, CancellationToken ct);
    }
}
