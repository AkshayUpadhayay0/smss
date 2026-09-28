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

        Task<ApiResponse<object>> UploadLogoAsync(string schoolId, Stream content, string fileName, long length, CancellationToken ct);
        Task<ApiResponse<object>> RemoveLogoAsync(string schoolId);
    }
}
