using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    /// <summary>Who is calling: decided by the controller from the token. <paramref name="CanManage"/> = School Admin or Principal; <paramref name="IsSchoolAdmin"/> = School Admin.</summary>
    public record NoticeCaller(long UserId, bool CanManage, bool IsSchoolAdmin);

    // schoolId is the caller's own school from the token; null / empty answers 400.
    public interface INoticeService
    {
        /// <summary>
        /// The audience tiers a user falls under, reusable by anything that needs to ask "is this notice for this person?": always ALL; STAFF for the School Admin's
        /// school-id login and for an employee with an Active record and an Active role; STUDENTS_GUARDIANS for an Active student and for any Guardian login
        /// (matched by login type / role, so a Guardian login is covered the day it exists, with no code change). Empty when the user is not an Active user of the school.
        /// </summary>
        Task<IReadOnlyCollection<string>> ResolveAudienceTiersAsync(string? schoolId, long userId, CancellationToken ct);

        /// <summary>Publishes immediately (published_at = now).</summary>
        Task<ApiResponse<NoticeDto>> CreateAsync(string? schoolId, NoticeCaller caller, SaveNoticeRequest req, CancellationToken ct);
        /// <summary>The creating Principal or a School Admin; a different Principal gets 403.</summary>
        Task<ApiResponse<NoticeDto>> UpdateAsync(string? schoolId, NoticeCaller caller, long id, SaveNoticeRequest req, CancellationToken ct);
        /// <summary>Soft delete (status Inactive). Same authorization as update.</summary>
        Task<ApiResponse<NoticeDto>> DeleteAsync(string? schoolId, NoticeCaller caller, long id, CancellationToken ct);
        /// <summary>One notice: for its audience, or for a School Admin / Principal. Opening it marks it read for the caller.</summary>
        Task<ApiResponse<NoticeDto>> GetAsync(string? schoolId, NoticeCaller caller, long id, CancellationToken ct);
        /// <summary>The notices addressed to the caller's tiers, most recently published first, each with the caller's isRead.</summary>
        Task<ApiResponse<List<NoticeDto>>> GetMyAsync(string? schoolId, NoticeCaller caller, bool unreadOnly, DateOnly? from, DateOnly? to, CancellationToken ct);
        Task<ApiResponse<UnreadCountDto>> GetUnreadCountAsync(string? schoolId, NoticeCaller caller, CancellationToken ct);
        /// <summary>School Admin / Principal: every notice of the school whatever its audience, filterable.</summary>
        Task<ApiResponse<List<NoticeDto>>> ListAsync(string? schoolId, NoticeCaller caller, string? audience, DateOnly? from, DateOnly? to, CancellationToken ct);
    }
}
