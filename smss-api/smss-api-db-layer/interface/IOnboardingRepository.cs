namespace smss_api_db_layer.@interface
{
    /// <summary>
    /// Read-only questions behind the first-login setup checklist. Everything is derived from existing tables (nothing is stored), and every
    /// method takes the school id: there is no way to ask about a school other than the one given.
    /// </summary>
    public interface IOnboardingRepository
    {
        Task<int?> ActiveStatusIdAsync(CancellationToken ct);
        /// <summary>The user's tb_users.is_first_login, only when that user belongs to the school; null when there is no such user.</summary>
        Task<bool?> IsFirstLoginAsync(string schoolId, long userId, CancellationToken ct);
        Task<bool> HasActiveStudentAsync(string schoolId, int activeStatusId, CancellationToken ct);
        /// <summary>The id of the school's current (is_current and Active) academic year, or null.</summary>
        Task<long?> CurrentYearIdAsync(string schoolId, int activeStatusId, CancellationToken ct);
        Task<List<OnboardingClassRow>> ActiveClassesAsync(string schoolId, int activeStatusId, CancellationToken ct);
        Task<HashSet<long>> ClassIdsWithActiveSectionAsync(string schoolId, int activeStatusId, CancellationToken ct);
        Task<bool> HasActiveFeeTypeAsync(string schoolId, CancellationToken ct);
        /// <summary>Classes that have an active fee structure with at least one line in the given academic year.</summary>
        Task<HashSet<long>> ClassIdsWithFeeStructureAsync(string schoolId, long academicYearId, CancellationToken ct);
    }

    public record OnboardingClassRow(long ClassId, string ClassName);
}
