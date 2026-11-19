namespace smss_api_service_layer.@interface
{
    /// <summary>One push to one device token. Title and body are the neutral lock-screen text; the data payload carries only the type.</summary>
    public record FcmMessage(string Token, string Title, string Body, string DataType);

    public enum FcmResultKind
    {
        Success,
        /// <summary>The token is dead or malformed: deactivate it.</summary>
        InvalidToken,
        /// <summary>Unavailable / Internal / QuotaExceeded: worth retrying on a later tick.</summary>
        Transient,
        /// <summary>Anything else: not retried.</summary>
        Permanent
    }

    /// <summary>The outcome for one message. <paramref name="ErrorCode"/> is a short FCM error code name, never a token or message text.</summary>
    public record FcmResult(FcmResultKind Kind, string? ErrorCode = null);

    /// <summary>The only door to Firebase Cloud Messaging. Tests use a fake; the real one is <c>FcmSender</c>.</summary>
    public interface IFcmSender
    {
        /// <summary>
        /// Whether sending can work (credentials present and valid). When false, <paramref name="reason"/> is a short, secret-free explanation that is safe to log.
        /// The API starts either way: the job asks this first, logs one error per run and sends nothing.
        /// </summary>
        bool TryEnsureReady(out string reason);

        /// <summary>Sends up to 500 messages (the caller batches) and returns one result per message, in the same order. Never throws for a per-message failure.</summary>
        Task<IReadOnlyList<FcmResult>> SendAsync(IReadOnlyList<FcmMessage> messages, CancellationToken ct);
    }
}
