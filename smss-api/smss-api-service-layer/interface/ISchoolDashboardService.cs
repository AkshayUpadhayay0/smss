using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId is the caller's own school from the token; null / empty answers 400. The caller is a School Admin (the controller checks).
    public interface ISchoolDashboardService
    {
        /// <summary>
        /// The School Admin dashboard in one call, composed from the existing services (attendance, fee reports, employees, leave, class-teacher, events, notices,
        /// admissions). Computed live on every request; nothing is stored. A part that fails is left empty and named in PartsUnavailable.
        /// </summary>
        Task<ApiResponse<SchoolDashboardDto>> GetSummaryAsync(string? schoolId, long callerUserId, CancellationToken ct);
    }
}
