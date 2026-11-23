using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId always comes from the caller's token; null/empty means the account has no school (400). A year id of another school answers 404.
    public interface IYearCopyService
    {
        /// <summary>What a copy WOULD do, per part: will-create, already-there, skipped with the reason. Nothing is saved.</summary>
        Task<ApiResponse<YearCopyResultDto>> PreviewAsync(string? schoolId, long targetYearId, long sourceYearId, YearCopyRequest request, CancellationToken ct);
        /// <summary>Runs the copy (needs Confirm = true) in one transaction under the per-school lock and writes one log row. Merges: skips what exists, never overwrites or deletes.</summary>
        Task<ApiResponse<YearCopyResultDto>> ApplyAsync(string? schoolId, long userId, long targetYearId, long sourceYearId, YearCopyRequest request, CancellationToken ct);
        /// <summary>The latest copy runs into a year.</summary>
        Task<ApiResponse<List<YearCopyLogDto>>> GetLogAsync(string? schoolId, long targetYearId, CancellationToken ct);
    }
}
