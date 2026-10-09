using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId always comes from the caller's token; null/empty means the account has no school (400).
    public interface IClassSubjectService
    {
        Task<ApiResponse<object>> GetByClassAsync(string? schoolId, long? classId);
        Task<ApiResponse<object>> GetByIdAsync(string? schoolId, long id);
        Task<ApiResponse<object>> CreateAsync(string? schoolId, CreateClassSubjectRequest request);
        Task<ApiResponse<object>> DeleteAsync(string? schoolId, long id);
        Task<ApiResponse<object>> BulkSetAsync(string? schoolId, BulkSetClassSubjectsRequest request);
    }
}
