namespace smss_api_service_layer.@interface
{
    /// <summary>
    /// The one place that decides who gets an admission / student notification email and sends it.
    /// Sending never throws and never fails the operation that triggered it.
    /// </summary>
    public interface INotificationService
    {
        /// <summary>The login page URL from configuration (Email:LoginUrl), for templates that link to it.</summary>
        string? LoginUrl { get; }

        /// <summary>
        /// Sends the email to the person's own address, else the guardian's; skips silently when neither exists.
        /// Returns the address it was sent to, or null when nothing was sent (no address, or the send failed).
        /// </summary>
        Task<string?> SendAsync(string? ownEmail, string? guardianEmail, (string Subject, string Html) message, string context, CancellationToken ct);
    }
}
