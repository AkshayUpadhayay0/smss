using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    /// <summary>What decides a user's audience tiers: their login type and the records behind it.</summary>
    public record NoticeAudienceFacts(string UserType, bool HasActiveEmployee, bool HasActiveRole, bool HasActiveStudent, IReadOnlyCollection<string> ActiveRoleCodes);

    /// <summary>A notice with its creator's name and whether the asking user has opened it.</summary>
    public record NoticeRow(long NoticeId, string Title, string Body, string Audience, long CreatedBy, string? CreatedByName, DateTime PublishedAt, DateTime UpdatedAt, int StatusId, bool IsRead);

    /// <summary>Dates filter on the day the notice was published (inclusive, in the school's time zone, already converted to UTC instants). Inactive (deleted) notices are never returned.</summary>
    public record NoticeFilter(IReadOnlyCollection<string>? Audiences = null, DateTime? PublishedFromUtc = null, DateTime? PublishedToExclusiveUtc = null, bool UnreadOnly = false);

    // Every method takes the school id: another school's notices and people can never be reached through this repository.
    public interface INoticeRepository
    {
        Task<int> SaveChangesAsync(CancellationToken ct);
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct);
        /// <summary>Null when the user is not an Active user of this school.</summary>
        Task<NoticeAudienceFacts?> GetAudienceFactsAsync(string schoolId, long userId, int activeStatusId, CancellationToken ct);

        Task AddAsync(TbNotice notice, CancellationToken ct);
        Task<TbNotice?> GetActiveForUpdateAsync(string schoolId, long noticeId, int activeStatusId, CancellationToken ct);
        Task<NoticeRow?> GetRowAsync(string schoolId, long noticeId, long readerUserId, CancellationToken ct);
        Task<List<NoticeRow>> ListAsync(string schoolId, long readerUserId, NoticeFilter filter, int activeStatusId, CancellationToken ct);
        Task<int> CountUnreadAsync(string schoolId, long readerUserId, IReadOnlyCollection<string> audiences, int activeStatusId, CancellationToken ct);
        /// <summary>Records that the user opened the notice. Safe to repeat and to run concurrently (ON CONFLICT DO NOTHING).</summary>
        Task MarkReadAsync(long noticeId, long userId, CancellationToken ct);
    }
}
