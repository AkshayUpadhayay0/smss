using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId is the caller's own school from the token; null / empty answers 400. The teacher is ALWAYS the caller: there is no employee id to pass.
    public interface ITeacherDashboardService
    {
        /// <summary>
        /// The Teacher dashboard in one call, composed from the existing services (class teacher, timetable, attendance, employee). The caller's employee id is
        /// resolved exactly as Timetable/teacher/my does (their login id). 403 when the login is not an employee record. The controller restricts it to the Teacher role.
        /// </summary>
        Task<ApiResponse<TeacherDashboardDto>> GetAsync(string? schoolId, long callerUserId, CancellationToken ct);
    }
}
