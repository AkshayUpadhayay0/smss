using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId is the caller's own school from the token; null / empty answers 400. `callerIsSchoolAdmin` comes from the token's roles.
    public interface IAttendanceService
    {
        /// <summary>Marks a whole section for TODAY (server date). The caller must be the section's class teacher for that year, or a School Admin. Resubmitting overwrites.</summary>
        Task<ApiResponse<SubmitAttendanceResultDto>> SubmitAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, SubmitAttendanceRequest req, CancellationToken ct);
        /// <summary>School Admin only (the controller checks): changes one existing mark, or ADDS one for a day that has none (a correction with nothing to overwrite), and records who, when and why.</summary>
        Task<ApiResponse<AttendanceCorrectionDto>> CorrectAsync(string? schoolId, long adminUserId, CorrectAttendanceRequest req, CancellationToken ct);
        Task<ApiResponse<SectionAttendanceDto>> GetSectionAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, long sectionId, DateOnly? date, CancellationToken ct);
        Task<ApiResponse<StudentAttendanceHistoryDto>> GetStudentAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, string studentId, DateOnly from, DateOnly to, CancellationToken ct);
        /// <summary>The caller's OWN marks for the school's current academic year (start to today) with the per-status summary: the same computation as <see cref="GetStudentAsync"/>, scoped to the caller. The student is the caller; there is no id to pass.</summary>
        Task<ApiResponse<StudentAttendanceHistoryDto>> GetOwnYearAsync(string? schoolId, long callerUserId, CancellationToken ct);
        Task<ApiResponse<MissingAttendanceDto>> GetMissingAsync(string? schoolId, DateOnly? date, long? academicYearId, CancellationToken ct);
        Task<ApiResponse<MySectionsDto>> GetMySectionsAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, CancellationToken ct);
    }
}
