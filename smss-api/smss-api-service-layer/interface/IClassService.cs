using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId always comes from the caller's token; null/empty means the account has no school (400).
    public interface IClassService
    {
        /// <summary>academicYearId given: only the classes offered in that year (another school's year simply matches nothing).</summary>
        Task<ApiResponse<object>> GetAllAsync(string? schoolId, long? academicYearId = null);
        Task<ApiResponse<object>> GetByIdAsync(string? schoolId, long id);
        Task<ApiResponse<object>> CreateAsync(string? schoolId, CreateClassRequest request);
        Task<ApiResponse<object>> UpdateAsync(string? schoolId, long id, UpdateClassRequest request);
        Task<ApiResponse<object>> ToggleStatusAsync(string? schoolId, long id);
    }
}
