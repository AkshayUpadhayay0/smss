using smss_api_service_layer.dto;
using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_service_layer.@interface
{
    public interface ISystemSetupService
    {
        Task<ApiResponse<object>> BootstrapSuperAdminAsync(CreateSuperAdminRequest req);
    }
}
