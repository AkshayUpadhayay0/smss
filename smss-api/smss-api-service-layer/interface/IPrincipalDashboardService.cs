using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    public interface IPrincipalDashboardService
    {
        /// <summary><paramref name="academicYearId"/>: the year to VIEW (404 when it is not this school's); null = the school's current year.</summary>
        Task<ApiResponse<PrincipalDashboardDto>> GetSummaryAsync(string? schoolId, long callerUserId, long? academicYearId, CancellationToken ct);
    }
}
