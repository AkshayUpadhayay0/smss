using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId always comes from the caller's token (the controller guarantees it).
    public interface IStudentOnboardingService
    {
        Task<ApiResponse<StudentOnboardingResponseDto>> CreateDirectEntryAsync(string schoolId, DirectStudentEntryRequestDto req, CancellationToken ct);
    }
}
