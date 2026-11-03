using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    /// <summary>
    /// The Teacher's own, narrow view of students: only those in sections the caller is the Active class teacher of, or is timetabled to teach a subject in
    /// (the Class-Teacher and Timetable modules decide both). It never takes a section id, so it cannot be pointed at a section the caller has no part in.
    /// </summary>
    public interface IMyStudentsService
    {
        Task<ApiResponse<MyClassDto>> GetMyClassAsync(string? schoolId, long callerUserId, CancellationToken ct);

        /// <summary>One student's read-only profile, only if they are in one of the caller's sections (404 otherwise, so nothing can be probed). Aadhaar and the login id are dropped.</summary>
        Task<ApiResponse<StudentProfileDto>> GetMyClassStudentAsync(string? schoolId, long callerUserId, string studentId, CancellationToken ct);
    }
}
