using smss_api_service_layer.dto;
using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_service_layer.@interface
{
    public interface ISchoolRegistrationService
    {
        Task<ApiResponse<object>> GetSchoolsAsync();
        Task<ApiResponse<object>> GetSchoolByIdAsync(string schoolId);
        Task<ApiResponse<object>> RegisterSchoolAsync(CreateSchoolRequest request, CancellationToken ct = default);
        Task<ApiResponse<object>> UpdateSchoolAsync(string schoolId, UpdateSchoolRequest request);
        Task<ApiResponse<object>> ToggleSchoolStatusAsync(string schoolId);

        // "My school": schoolId is taken from the caller's token; null/empty means the account has no school (400)
        /// <summary>The caller's own school. <paramref name="full"/> (School Admin / Super Admin) = the whole record; otherwise only the public profile subset (no tax / registration numbers, no contact persons).</summary>
        Task<ApiResponse<object>> GetMySchoolAsync(string? schoolId, bool full);
        /// <summary>Just the school's name and logo, for the avatar every role of the school sees. The school comes from the token.</summary>
        Task<ApiResponse<object>> GetMyBrandAsync(string? schoolId);
        Task<ApiResponse<object>> UpdateMySchoolAsync(string? schoolId, UpdateMySchoolProfileRequest request);
        Task<ApiResponse<object>> UploadMyLogoAsync(string? schoolId, Stream content, string fileName, long length, CancellationToken ct);
        Task<ApiResponse<object>> RemoveMyLogoAsync(string? schoolId);

        Task<ApiResponse<object>> UploadLogoAsync(string schoolId, Stream content, string fileName, long length, CancellationToken ct);
        Task<ApiResponse<object>> RemoveLogoAsync(string schoolId);
    }
}
