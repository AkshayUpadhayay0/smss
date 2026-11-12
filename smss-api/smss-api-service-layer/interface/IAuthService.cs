using smss_api_service_layer.dto;
using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_service_layer.@interface
{
    public interface IAuthService
    {
        /// <param name="clientKey">The caller address bucket (ClientAddress.PartitionKey), used only by the login throttle.</param>
        Task<ApiResponse<object>> LoginAsync(LoginRequest request, string? clientKey = null);
        Task<ApiResponse<object>> RefreshAsync(RefreshTokenRequest request);
        Task<ApiResponse<object>> LogoutAsync(RefreshTokenRequest request);
        Task<ApiResponse<object>> GetCurrentUserAsync(long userId);
        Task<ApiResponse<object>> ChangePasswordAsync(long userId, ChangePasswordRequest request);
    }
}
