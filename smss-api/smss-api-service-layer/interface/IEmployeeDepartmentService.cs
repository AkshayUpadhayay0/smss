using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId always comes from the caller's token; null/empty means the account has no school (400).
    public interface IEmployeeDepartmentService
    {
        Task<ApiResponse<object>> GetAllAsync(string? schoolId);
        Task<ApiResponse<object>> GetByIdAsync(string? schoolId, long id);
        Task<ApiResponse<object>> CreateAsync(string? schoolId, CreateEmployeeDepartmentRequest request);
        Task<ApiResponse<object>> UpdateAsync(string? schoolId, long id, UpdateEmployeeDepartmentRequest request);
        Task<ApiResponse<object>> ToggleStatusAsync(string? schoolId, long id);
    }
}
