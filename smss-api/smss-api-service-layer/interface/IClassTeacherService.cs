using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId is the caller's own school from the token; null / empty answers 400.
    public interface IClassTeacherService
    {
        /// <summary>Assign or reassign in ONE transaction: the previous Active row (if any) is deactivated and the new one inserted.</summary>
        Task<ApiResponse<ClassTeacherAssignmentDto>> AssignAsync(string? schoolId, long assignedByUserId, AssignClassTeacherRequest req, CancellationToken ct);
        Task<ApiResponse<ClassTeacherAssignmentDto>> UnassignAsync(string? schoolId, UnassignClassTeacherRequest req, CancellationToken ct);
        /// <summary>The current class teacher of a section for a year (Data is null when there is none).</summary>
        Task<ApiResponse<ClassTeacherAssignmentDto>> GetCurrentAsync(string? schoolId, long academicYearId, long? classId, long sectionId, CancellationToken ct);
        Task<ApiResponse<ClassTeacherListDto>> ListAsync(string? schoolId, long academicYearId, CancellationToken ct);
        Task<ApiResponse<List<ClassTeacherAssignmentDto>>> GetHistoryAsync(string? schoolId, long academicYearId, long sectionId, CancellationToken ct);
        Task<ApiResponse<List<EligibleTeacherDto>>> GetEligibleTeachersAsync(string? schoolId, CancellationToken ct);

        /// <summary>
        /// For Attendance: is <paramref name="employeeId"/> the Active class teacher of the section in the academic year (the school's current
        /// year when none is given), AND still an eligible teacher today (Active record, Teaching, login enabled with the Teacher role). No access check:
        /// in-process callers decide who may ask.
        /// </summary>
        Task<ApiResponse<ClassTeacherCheckDto>> IsClassTeacherAsync(string? schoolId, string employeeId, long sectionId, long? academicYearId, CancellationToken ct);
        /// <summary>The HTTP form of the check: a School Admin may ask about anyone; anyone else only about themselves.</summary>
        Task<ApiResponse<ClassTeacherCheckDto>> CheckAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, string employeeId, long sectionId, long? academicYearId, CancellationToken ct);
    }
}
