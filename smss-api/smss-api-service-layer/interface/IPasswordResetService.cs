using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    public interface IPasswordResetService
    {
        Task<ApiResponse<PasswordResetResultDto>> ResetAsync(PasswordResetCaller caller, ResetPasswordRequest request, CancellationToken ct);
    }
}
