using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    /// <summary>
    /// Asks whether anything has been ENTERED against a scheme (marks, grades, results). Stage 1 has no entries, so the default answer is "no"; Stage 2 replaces the
    /// implementation, and every destructive setup change (removing a term, exam, subject, class or scheme) already asks it first, so the rule is in place before there is anything to protect.
    /// </summary>
    public interface IReportEntryGuard
    {
        Task<bool> HasEntriesAsync(string schoolId, long schemeId, CancellationToken ct);
    }

    public interface IReportSchemeService
    {
        Task<ApiResponse<List<SchemeListDto>>> ListAsync(string? schoolId, long? academicYearId, bool includeInactive, CancellationToken ct);
        Task<ApiResponse<SchemeDto>> GetAsync(string? schoolId, long schemeId, CancellationToken ct);
        Task<ApiResponse<SchemeDto>> CreateAsync(string? schoolId, long userId, SaveSchemeRequest req, CancellationToken ct);
        Task<ApiResponse<SchemeDto>> UpdateAsync(string? schoolId, long userId, long schemeId, SaveSchemeRequest req, CancellationToken ct);
        /// <summary>Soft delete (Inactive) and frees its classes. 409 once anything has been entered against it.</summary>
        Task<ApiResponse<SchemeDto>> DeleteAsync(string? schoolId, long schemeId, CancellationToken ct);
        Task<ApiResponse<SchemeDto>> CloneAsync(string? schoolId, long userId, long schemeId, CloneSchemeRequest req, CancellationToken ct);
        Task<ApiResponse<SchemeDto>> SetClassesAsync(string? schoolId, long schemeId, SetSchemeClassesRequest req, CancellationToken ct);
        Task<ApiResponse<SchemeDto>> SaveTermsAsync(string? schoolId, long schemeId, SaveTermsRequest req, CancellationToken ct);
        Task<ApiResponse<SchemeDto>> SaveSubjectsAsync(string? schoolId, long schemeId, SaveSubjectsRequest req, CancellationToken ct);
        Task<ApiResponse<SchemeDto>> SaveMatrixAsync(string? schoolId, long schemeId, SaveMatrixRequest req, CancellationToken ct);
        Task<ApiResponse<SchemeDto>> SaveSkillsAsync(string? schoolId, long schemeId, SaveSkillsRequest req, CancellationToken ct);
        /// <summary>The three-step path: a preset + the classes (+ optional term dates) gives a complete, working scheme.</summary>
        Task<ApiResponse<SchemeDto>> FromPresetAsync(string? schoolId, long userId, SchemeFromPresetRequest req, CancellationToken ct);
        ApiResponse<List<SchemePresetDto>> Presets();
        ApiResponse<List<ReportTemplateDto>> Templates();
    }

    public interface IReportActivityAreaService
    {
        Task<ApiResponse<List<ActivityAreaDto>>> ListAsync(string? schoolId, bool includeInactive, CancellationToken ct);
        Task<ApiResponse<ActivityAreaDto>> CreateAsync(string? schoolId, SaveActivityAreaRequest req, CancellationToken ct);
        Task<ApiResponse<ActivityAreaDto>> UpdateAsync(string? schoolId, long areaId, SaveActivityAreaRequest req, CancellationToken ct);
        Task<ApiResponse<ActivityAreaDto>> DeleteAsync(string? schoolId, long areaId, CancellationToken ct);
        /// <summary>Adds the starter areas (Discipline, Cleanliness, ... ) that are not there yet, creating the A-B-C skill scale if the school has none.</summary>
        Task<ApiResponse<StarterAreasResultDto>> AddStarterAreasAsync(string? schoolId, CancellationToken ct);
    }

    public interface IReportScheduleService
    {
        Task<ApiResponse<List<ScheduleSchemeDto>>> GetAsync(string? schoolId, long? academicYearId, CancellationToken ct);
        Task<ApiResponse<ScheduleSchemeDto>> SaveAsync(string? schoolId, SaveScheduleRequest req, CancellationToken ct);
    }
}
