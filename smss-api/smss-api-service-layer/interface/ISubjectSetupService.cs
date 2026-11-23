using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId always comes from the caller's token; null/empty means the account has no school (400).
    public interface ISubjectSetupService
    {
        Task<ApiResponse<YearClassSubjectsDto>> GetAsync(string? schoolId, long academicYearId, long classId, CancellationToken ct);
        /// <summary>Replaces the class's subjects for the year and the exceptions of its sections. Closed year: 409. Something in use removed: 409 with counts.</summary>
        Task<ApiResponse<YearClassSubjectsDto>> SaveAsync(string? schoolId, long? userId, long academicYearId, long classId, SaveYearClassSubjectsRequest request, CancellationToken ct);
    }
}
