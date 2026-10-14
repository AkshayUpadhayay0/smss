using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId is always the caller's own school from the token; null/empty (e.g. a Super Admin) answers 400.
    public interface IFeeTypeService
    {
        Task<ApiResponse<object>> GetAllAsync(string? schoolId);
        Task<ApiResponse<object>> GetByIdAsync(string? schoolId, long id);
        Task<ApiResponse<object>> CreateAsync(string? schoolId, CreateFeeTypeRequest req);
        Task<ApiResponse<object>> UpdateAsync(string? schoolId, long id, UpdateFeeTypeRequest req);
        Task<ApiResponse<object>> ToggleStatusAsync(string? schoolId, long id);
    }

    public interface IFeeStructureService
    {
        Task<ApiResponse<object>> GetAllAsync(string? schoolId, long? academicYearId, long? classId);
        /// <summary>The header plus its lines (fee type and frequency names joined).</summary>
        Task<ApiResponse<object>> GetByIdAsync(string? schoolId, long id);
        Task<ApiResponse<object>> GetFrequenciesAsync();
        Task<ApiResponse<object>> CreateAsync(string? schoolId, CreateFeeStructureRequest req);
        Task<ApiResponse<object>> UpdateAsync(string? schoolId, long id, UpdateFeeStructureRequest req);
        Task<ApiResponse<object>> ToggleStatusAsync(string? schoolId, long id);
    }

    public interface IFeeConcessionService
    {
        Task<ApiResponse<object>> GetAllAsync(string? schoolId);
        Task<ApiResponse<object>> GetByIdAsync(string? schoolId, long id);
        Task<ApiResponse<object>> CreateAsync(string? schoolId, CreateFeeConcessionRequest req);
        Task<ApiResponse<object>> UpdateAsync(string? schoolId, long id, UpdateFeeConcessionRequest req);
        Task<ApiResponse<object>> ToggleStatusAsync(string? schoolId, long id);
    }
}
