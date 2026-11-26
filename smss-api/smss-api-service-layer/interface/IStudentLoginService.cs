using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    public interface IStudentLoginService
    {
        /// <summary>
        /// Creates the student's login (the login id is the student's own id, e.g. DPS001ST001) WITHOUT a password and sends the primary guardian a one-time set-password link. A School Admin may do this for any student of
        /// the school; a class teacher only for students of a section they are class teacher of (403 otherwise). Another school's student is a 404.
        /// </summary>
        Task<ApiResponse<StudentLoginCreatedDto>> CreateStudentLoginAsync(string schoolId, string studentId, long actorUserId, bool actorIsSchoolAdmin, CancellationToken ct);
        /// <summary>Sends a new set-password link for a student login whose password was never chosen (the older links stop working). Same who-may-do-it rule; 429 past the limit.</summary>
        Task<ApiResponse<StudentLoginCreatedDto>> ResendLoginLinkAsync(string schoolId, string studentId, long actorUserId, bool actorIsSchoolAdmin, CancellationToken ct);
    }
}
