using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    public interface IMasterImportService
    {
        /// <summary>The ready-made choices, each marked with whether the caller's school already has it.</summary>
        Task<ApiResponse<MasterImportCatalogDto>> GetCatalogAsync(string? schoolId, CancellationToken ct);

        /// <summary>Creates the selected masters (and section A for the classes) for the caller's school, all or nothing.</summary>
        Task<ApiResponse<ImportMastersResultDto>> ImportAsync(string? schoolId, ImportMastersRequest req, CancellationToken ct);
    }
}
