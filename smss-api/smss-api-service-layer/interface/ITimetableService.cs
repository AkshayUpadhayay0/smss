using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId is the caller's own school from the token; null / empty answers 400.
    public interface ITimetableService
    {
        /// <summary>Assign or reassign a slot in ONE transaction: the old Active entry (if any) is deactivated and the new one inserted. A teacher can't be in two sections at once.</summary>
        Task<ApiResponse<TimetableEntryDto>> AssignAsync(string? schoolId, long assignedByUserId, AssignTimetableRequest req, CancellationToken ct);
        Task<ApiResponse<TimetableEntryDto>> UnassignAsync(string? schoolId, UnassignTimetableRequest req, CancellationToken ct);
        Task<ApiResponse<TimetableLookupsDto>> GetLookupsAsync(string? schoolId, long sectionId, CancellationToken ct);
        /// <summary>The weekly grid of a section (year defaults to the school's current one).</summary>
        Task<ApiResponse<TimetableGridDto>> GetClassGridAsync(string? schoolId, long sectionId, long? academicYearId, CancellationToken ct);
        /// <summary>A teacher's weekly grid across all their sections: a School Admin may ask about anyone, anyone else only about themselves.</summary>
        Task<ApiResponse<TimetableGridDto>> GetTeacherGridAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, string employeeId, long? academicYearId, CancellationToken ct);
        Task<ApiResponse<TimetableGridDto>> GetMyGridAsync(string? schoolId, long callerUserId, long? academicYearId, CancellationToken ct);
    }
}
