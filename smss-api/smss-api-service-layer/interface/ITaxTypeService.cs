using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId is always the caller's own school from the token; null/empty answers 400.
    public interface ITaxTypeService
    {
        /// <summary>Active types only, unless includeInactive (the controller passes it only for a School Admin).</summary>
        Task<ApiResponse<List<TaxTypeDto>>> GetAllAsync(string? schoolId, bool includeInactive, CancellationToken ct);
        Task<ApiResponse<TaxTypeDto>> CreateAsync(string? schoolId, TaxTypeRequest req, CancellationToken ct);
        Task<ApiResponse<TaxTypeDto>> UpdateAsync(string? schoolId, long id, TaxTypeRequest req, CancellationToken ct);
        Task<ApiResponse<TaxTypeDto>> SetActiveAsync(string? schoolId, long id, bool active, CancellationToken ct);
    }
}
