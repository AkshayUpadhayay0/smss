using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    public interface ISchoolAdminDashboardService
    {
        /// <summary>The School Admin dashboard for the caller's own school (schoolId from the token; null/empty answers 400). <paramref name="today"/> is for tests: the India date when null.</summary>
        Task<ApiResponse<SchoolAdminDashboardDto>> GetAsync(string? schoolId, CancellationToken ct, DateOnly? today = null);
    }
}
