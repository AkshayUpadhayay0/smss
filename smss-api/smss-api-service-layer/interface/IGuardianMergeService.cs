using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId is the caller's own school from the token; every read and write is scoped to it (another school's guardian is a 404). School Admin and Principal only (the controller checks).
    // A merge is NEVER run by a migration or a startup task: only an admin, through the screen, one pair at a time.
    public interface IGuardianMergeService
    {
        /// <summary>Active guardians of the school that share a phone number, grouped, with a name-similarity flag and their students.</summary>
        Task<ApiResponse<DuplicateReportDto>> ListDuplicatesAsync(string? schoolId, CancellationToken ct);
        /// <summary>Exactly what a merge would do (links, fields copied, applications repointed, account), changing NOTHING.</summary>
        Task<ApiResponse<MergePreviewDto>> PreviewAsync(string? schoolId, MergeRequestDto request, CancellationToken ct);
        /// <summary>Merge the absorbed guardian into the survivor, in one transaction, after the explicit confirmation. The absorbed guardian is deactivated and kept; the merge is audited.</summary>
        Task<ApiResponse<MergeResultDto>> MergeAsync(string? schoolId, MergeRequestDto request, long userId, CancellationToken ct);
    }
}
