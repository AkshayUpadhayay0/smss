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
        Task<ApiResponse<object>> RegisterSchoolAsync(CreateSchoolRequest request);
        Task<ApiResponse<object>> UpdateSchoolAsync(string schoolId, UpdateSchoolRequest request);
        Task<ApiResponse<object>> ToggleSchoolStatusAsync(string schoolId);

        // "My school": schoolId is taken from the caller's token; null/empty means the account has no school (400)
        Task<ApiResponse<object>> GetMySchoolAsync(string? schoolId);
        Task<ApiResponse<object>> UpdateMySchoolAsync(string? schoolId, UpdateMySchoolProfileRequest request);
        Task<ApiResponse<object>> UploadMyLogoAsync(string? schoolId, Stream content, string fileName, long length, CancellationToken ct);
        Task<ApiResponse<object>> RemoveMyLogoAsync(string? schoolId);

        Task<ApiResponse<object>> UploadLogoAsync(string schoolId, Stream content, string fileName, long length, CancellationToken ct);
        Task<ApiResponse<object>> RemoveLogoAsync(string schoolId);
    }
}
