using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId always comes from the caller's token; null/empty means the account has no school (400).
    public interface IAcademicSetupService
    {
        Task<ApiResponse<YearOfferingDto>> GetOfferingAsync(string? schoolId, long academicYearId, CancellationToken ct);
        /// <summary>Replaces the year's offered classes and sections. A closed year is refused (409); withdrawing something in use is refused (409, with the reasons).</summary>
        Task<ApiResponse<YearOfferingDto>> SaveOfferingAsync(string? schoolId, long userId, long academicYearId, SaveYearOfferingRequest request, CancellationToken ct);
    }
}
