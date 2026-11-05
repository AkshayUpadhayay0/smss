using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    public interface IStudentMyFeesService
    {
        /// <summary>The signed-in Student's OWN fees (the student is the caller's login id, never a client value). The controller restricts it to the Student role.</summary>
        Task<ApiResponse<StudentMyFeesDto>> GetMyFeesAsync(string? schoolId, long callerUserId, CancellationToken ct);
    }
}
