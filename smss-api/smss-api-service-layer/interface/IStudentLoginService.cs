using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    public interface IStudentLoginService
    {
        /// <summary>Creates the student's login: the login id is the student's own id (e.g. DPS001ST001), the password a temporary name+date-of-birth one.</summary>
        Task<ApiResponse<StudentLoginCreatedDto>> CreateStudentLoginAsync(string schoolId, string studentId, CancellationToken ct);
    }
}
