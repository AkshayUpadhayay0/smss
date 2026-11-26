using smss_api_service_layer.helper;

namespace smss_api_service_layer.@interface
{
    /// <summary>A link that was just issued. Token exists only here: put it in the message, never log it or store it.</summary>
    public sealed record IssuedLink(string Token, DateTime ExpiresAtUtc);

    /// <summary>Who a used link belonged to. The only success answer; every failure (unknown, malformed, expired, used, revoked, wrong purpose) is the same null.</summary>
    public sealed record ConsumedLink(long UserId, string? SchoolId);

    /// <summary>One-time set-password / reset links. Single use, expiring, stored hashed.</summary>
    public interface IUserActionTokenService
    {
        /// <summary>Makes a new link for the user (older unused links of that purpose are revoked) and returns the raw token once. Lifetime: SET_PASSWORD 72 hours, RESET_PASSWORD 1 hour (ParentAccounts settings).</summary>
        Task<IssuedLink> IssueAsync(long userId, string? schoolId, string purpose, long? createdBy, CancellationToken ct);
        /// <summary>Uses the link: marks it used in one atomic statement and returns who it was for, or null for ANY reason it cannot be used.</summary>
        Task<ConsumedLink?> ConsumeAsync(string? token, string purpose, CancellationToken ct);
        /// <summary>True when the link could still be used right now (for a page that checks before it shows the form). Does not use it.</summary>
        Task<bool> IsUsableAsync(string? token, string purpose, CancellationToken ct);
        /// <summary>The link a message carries, or null when ParentAccounts:WebBaseUrl is not configured.</summary>
        string? BuildLink(string purpose, string token);
    }

    /// <summary>What to send for one event, on whichever channels the caller has content for. A channel with content but no number / address is logged as SKIPPED.</summary>
    public sealed record ParentMessage(
        string SchoolId, long? GuardianId, long? UserId, string TemplateCode, string Purpose,
        string? WhatsAppNumber, WhatsAppContent? WhatsApp,
        string? EmailAddress, (string Subject, string Html)? Email,
        long? CreatedBy = null, long? ResendOfDeliveryId = null);

    public sealed record DeliveryOutcome(long? DeliveryId, string Channel, string Status, string? ErrorCode);

    /// <summary>
    /// Sends one message by WhatsApp and/or email and writes one delivery-log row per channel. NEVER throws and never blocks the caller: a failure is a FAILED row (or, if even the log cannot be written, a warning with the error type).
    /// WhatsApp goes from the school's own number once its sender status is ACTIVE, otherwise from the platform number (the templates all name the school).
    /// </summary>
    public interface IParentMessageDispatcher
    {
        Task<IReadOnlyList<DeliveryOutcome>> SendAsync(ParentMessage message, CancellationToken ct);
    }

    /// <summary>Allocates guardian login ids: school short code + GD + number.</summary>
    public interface IGuardianLoginIdService
    {
        /// <summary>The next free login id for the school (platform-unique ignoring case, never equal to a student id), or null when the school has no short code. Call it inside the transaction that creates the account.</summary>
        Task<string?> AllocateAsync(string schoolId, CancellationToken ct);
    }
}
