using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    public interface IFeeSectionStatusService
    {
        /// <summary>
        /// Fee status of one section's active students for an academic year (the school's current one when none is given). A School Admin may ask for any section; anyone else must be
        /// the section's Active class teacher for that year (403). A section or year of another school is 404.
        /// </summary>
        /// <param name="month">yyyy-MM; null = the current month (school time) kept inside the academic year. A month outside the year answers 200 with MonthInAcademicYear = false and no students.</param>
        Task<ApiResponse<SectionFeeStatusDto>> GetAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, long sectionId, long? academicYearId, string? month, CancellationToken ct);

        /// <summary>The whole academic year at a glance: every student x every month, one status code per cell and no amounts. Same authorization.</summary>
        Task<ApiResponse<SectionFeeGridDto>> GetGridAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, long sectionId, long? academicYearId, CancellationToken ct);
    }
}
