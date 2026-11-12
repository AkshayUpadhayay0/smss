using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    /// <summary>The target of a reset with what the authorization rules need to know about it.</summary>
    public record PasswordResetTarget(TbUsers User, List<string> RoleNames, bool IsActive);

    public interface IPasswordResetRepository
    {
        /// <summary>
        /// The login with this id (case-insensitive), TRACKED. With <paramref name="onlySchoolId"/> the search is limited to that school, so another school's login is simply "not found".
        /// <c>IsActive</c> = the login is Active AND its school (if any) is Active, the same test the sign-in uses.
        /// </summary>
        Task<PasswordResetTarget?> FindTargetAsync(string loginId, string? onlySchoolId, CancellationToken ct);

        /// <summary>
        /// ONE transaction: the user's new hash / first-login flag (already set on the tracked entity), the audit row, and every unrevoked refresh token of the user revoked.
        /// </summary>
        Task SaveResetAsync(TbPasswordResetAudit audit, long userId, DateTime now, CancellationToken ct);
    }
}
