using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    /// <summary>Device tokens. The school and the user are the caller's own, from the token; nothing about them is accepted from the client.</summary>
    public interface IDeviceService
    {
        Task<ApiResponse<DeviceResultDto>> RegisterAsync(string? schoolId, long callerUserId, RegisterDeviceRequest request, CancellationToken ct);
        /// <summary>Only deactivates a token the caller owns; anything else answers the same success (no hint whether the token exists).</summary>
        Task<ApiResponse<DeviceResultDto>> UnregisterAsync(string? schoolId, long callerUserId, UnregisterDeviceRequest request, CancellationToken ct);
    }

    /// <summary>A school's push settings (School Admin only; the controller checks the role).</summary>
    public interface INotificationSettingsService
    {
        Task<ApiResponse<NotificationSettingsDto>> GetAsync(string? schoolId, CancellationToken ct);
        Task<ApiResponse<NotificationSettingsDto>> SaveAsync(string? schoolId, long callerUserId, NotificationSettingsRequest request, CancellationToken ct);
    }

    /// <summary>The send audit (School Admin only; the controller checks the role).</summary>
    public interface INotificationLogService
    {
        Task<ApiResponse<List<PushLogItemDto>>> ListAsync(string? schoolId, string? studentId, DateOnly? from, DateOnly? to, CancellationToken ct);
    }
}
