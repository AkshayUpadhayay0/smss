using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId is the caller's own school from the token; null / empty answers 400. The student is ALWAYS the caller: there is no student id to pass.
    public interface IStudentDashboardService
    {
        /// <summary>
        /// The Student dashboard in one call, composed from the existing services (attendance, events, notices, timetable, assessments, student leave): none of
        /// their query logic is repeated here. 403 when the caller's login is not a student record. The controller restricts it to the Student role.
        /// </summary>
        Task<ApiResponse<StudentDashboardDto>> GetAsync(string? schoolId, long callerUserId, CancellationToken ct);
    }
}
