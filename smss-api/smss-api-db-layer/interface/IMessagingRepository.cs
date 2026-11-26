using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    /// <summary>What a link row says, read without using it (the reason a link is refused stays inside the server: the caller always gives the same answer).</summary>
    public sealed record ActionTokenInfo(long TokenId, long UserId, string? SchoolId, string Purpose, DateTime ExpiresAt, DateTime? UsedAt, DateTime? RevokedAt, DateTime DbNow);

    /// <summary>The raw SQL the one-time links, the delivery log and the guardian login numbers need. Every method is one atomic statement; none of them logs.</summary>
    public interface IMessagingRepository
    {
        // ---- one-time links (only the SHA-256 hash is ever stored)
        /// <summary>Stores a new link for the user and revokes the user's older unused links of the same purpose, in one statement. Times come from the database clock.</summary>
        Task<long> IssueTokenAsync(long userId, string? schoolId, string purpose, string tokenHash, TimeSpan lifetime, long? createdBy, CancellationToken ct);
        /// <summary>Marks the link used if (and only if) it is still unused, unrevoked and unexpired, in ONE statement, so two requests with the same token cannot both succeed. Null = not usable.</summary>
        Task<ActionTokenInfo?> ConsumeTokenAsync(string tokenHash, string purpose, CancellationToken ct);
        /// <summary>The link's row, or null when the hash is unknown. Does not use the link.</summary>
        Task<ActionTokenInfo?> PeekTokenAsync(string tokenHash, CancellationToken ct);
        /// <summary>Revokes every unused link of the user (optionally of one purpose): for example after the user was made Inactive. Returns how many.</summary>
        Task<int> RevokeTokensAsync(long userId, string? purpose, CancellationToken ct);
        /// <summary>How many links of this purpose were issued to the user since the time (the resend limit counts these: one link is issued per send).</summary>
        Task<int> CountTokensSinceAsync(long userId, string purpose, DateTime sinceUtc, CancellationToken ct);

        // ---- the delivery log
        Task<long> AddDeliveryAsync(TbMessageDelivery row, CancellationToken ct);
        /// <summary>Moves a delivery forward from a provider status (sent / delivered / read / failed). Never moves it backwards (READ stays READ). Returns false when no delivery has that provider message id.</summary>
        Task<bool> ApplyProviderStatusAsync(string providerMessageId, string status, string? errorCode, string? errorMessage, CancellationToken ct);
        /// <summary>How many deliveries of this purpose the guardian got since the time (the resend limit counts these).</summary>
        Task<int> CountDeliveriesSinceAsync(string schoolId, long guardianId, string purpose, DateTime sinceUtc, CancellationToken ct);
        Task<TbMessageDelivery?> GetDeliveryAsync(string schoolId, long deliveryId, CancellationToken ct);

        // ---- guardian login numbers
        /// <summary>The school's short code (the first part of every id), or null when it has none.</summary>
        Task<string?> GetSchoolShortCodeAsync(string schoolId, CancellationToken ct);
        /// <summary>The school's next login number (1, 2, 3 ...), taken atomically: two requests never get the same one.</summary>
        Task<int> NextGuardianNumberAsync(string schoolId, CancellationToken ct);
        /// <summary>True when any login id or student id already equals the candidate, ignoring case.</summary>
        Task<bool> LoginIdTakenAsync(string candidate, CancellationToken ct);
    }
}
