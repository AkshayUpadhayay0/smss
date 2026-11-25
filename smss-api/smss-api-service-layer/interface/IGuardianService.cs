using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId is the caller's own school from the token; every read and write is scoped to it (another school's guardian is a 404).
    public interface IGuardianService
    {
        Task<ApiResponse<GuardianDto>> GetAsync(string? schoolId, long guardianId, CancellationToken ct);
        /// <summary>Edit the guardian's name, mobile, WhatsApp, email and occupation. The primary guardian of an active student cannot be saved without WhatsApp and email. Changes to mobile, WhatsApp and email are logged (source EDIT).</summary>
        Task<ApiResponse<GuardianDto>> UpdateAsync(string? schoolId, long guardianId, GuardianUpdateRequestDto request, long userId, CancellationToken ct);
        /// <summary>Primary guardians of active students that lack a WhatsApp number or an email (class and section from the current-flagged year), paged.</summary>
        Task<ApiResponse<IncompleteGuardiansDto>> ListIncompleteAsync(string? schoolId, long? classId, long? sectionId, string? search, int page, int pageSize, CancellationToken ct);
        /// <summary>
        /// The sibling pick: active students of this school (enrolled in the current academic year), optionally of one class, matching a name, with their guardians. A class or at least 2 letters of a name is
        /// required. At most <paramref name="limit"/> (default 20, never more than 30) students; mobile numbers are masked; only this school's families are ever returned.
        /// </summary>
        Task<ApiResponse<List<SiblingStudentDto>>> SearchSiblingsAsync(string? schoolId, long? classId, string? search, int limit, CancellationToken ct);
        /// <summary>How many guardians need their details completed (the dashboard tiles).</summary>
        Task<ApiResponse<int>> CountIncompleteAsync(string? schoolId, CancellationToken ct);
        /// <summary>For the selected guardians only: use the mobile number as the WhatsApp number. Skips those that already have one, never touches email, logs every change (source BULK), all or nothing.</summary>
        Task<ApiResponse<BulkWhatsAppResultDto>> UseMobileAsWhatsAppAsync(string? schoolId, IReadOnlyCollection<long> guardianIds, long userId, CancellationToken ct);
    }
}
