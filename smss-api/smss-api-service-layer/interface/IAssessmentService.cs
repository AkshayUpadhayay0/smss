using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    /// <summary>Who is calling, decided by the controller from the token (role names) and passed in.</summary>
    public record AssessmentCaller(long UserId, bool IsSchoolAdmin, bool IsTeacher, bool IsStudent);

    // schoolId is the caller's own school from the token; null / empty answers 400.
    public interface IAssessmentService
    {
        Task<ApiResponse<List<AssessmentTypeDto>>> GetTypesAsync(CancellationToken ct);
        /// <summary>The active students of a section (the roster a test has to cover). Same eligibility and Class-Subject Mapping rules as creating a test for that section + subject.</summary>
        Task<ApiResponse<List<AssessmentRosterStudentDto>>> GetRosterAsync(string? schoolId, AssessmentCaller caller, long academicYearId, long sectionId, long subjectId, CancellationToken ct);

        /// <summary>A School Admin may record for any section (naming the owning teacher) but the subject must be mapped to the section's class; a Teacher only where the Timetable says they teach it.</summary>
        Task<ApiResponse<AssessmentDetailDto>> CreateAsync(string? schoolId, AssessmentCaller caller, CreateAssessmentRequest req, CancellationToken ct);
        /// <summary>Only the owning teacher or a School Admin. Re-submits the whole roster.</summary>
        Task<ApiResponse<AssessmentDetailDto>> UpdateAsync(string? schoolId, AssessmentCaller caller, long id, UpdateAssessmentRequest req, CancellationToken ct);
        /// <summary>Soft delete (status Inactive). Same authorization as update.</summary>
        Task<ApiResponse<AssessmentDto>> DeleteAsync(string? schoolId, AssessmentCaller caller, long id, CancellationToken ct);
        /// <summary>One test with all its marks: its owning teacher or a School Admin.</summary>
        Task<ApiResponse<AssessmentDetailDto>> GetAsync(string? schoolId, AssessmentCaller caller, long id, CancellationToken ct);

        Task<ApiResponse<List<AssessmentDto>>> GetMyAsync(string? schoolId, AssessmentCaller caller, long? academicYearId, long? sectionId, long? subjectId, DateOnly? from, DateOnly? to, CancellationToken ct);
        Task<ApiResponse<List<AssessmentDto>>> ListAsync(string? schoolId, long? academicYearId, long? sectionId, long? subjectId, DateOnly? from, DateOnly? to, CancellationToken ct);
        Task<ApiResponse<List<AssessmentDto>>> GetSectionAsync(string? schoolId, long sectionId, long? academicYearId, DateOnly? from, DateOnly? to, CancellationToken ct);
        /// <summary>A student: their own results in tests of their current section, most recent first.</summary>
        Task<ApiResponse<List<StudentAssessmentResultDto>>> GetStudentMyAsync(string? schoolId, AssessmentCaller caller, DateOnly? from, DateOnly? to, CancellationToken ct);
    }
}
