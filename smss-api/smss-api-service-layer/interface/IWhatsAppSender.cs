using smss_api_service_layer.helper;

namespace smss_api_service_layer.@interface
{
    /// <summary>One template message. ToNumber is the stored form ("+919876543210"); SenderPhoneNumberId overrides the platform sender when the school's own number is ACTIVE.</summary>
    public sealed record WhatsAppMessage(string ToNumber, WhatsAppContent Content, string? SenderPhoneNumberId);

    /// <summary>
    /// The outcome of one send. Kind is "LOG" (nothing was sent: the log-only sender) or "CLOUD". Success = accepted (CLOUD) or written to the log (LOG). ErrorCode / ErrorMessage are short and sanitised: never the provider's
    /// raw answer, a token, a link or a number.
    /// </summary>
    public sealed record WhatsAppSendResult(bool Success, string Kind, string? ProviderMessageId, string? ErrorCode, string? ErrorMessage);

    /// <summary>
    /// The one way anything in the API sends a WhatsApp template message. The default is the log-only sender (writes nothing but a log line with the template code, sends nothing); the Meta Cloud sender is used only when
    /// WhatsApp:Enabled is true, WhatsApp:Sender is "Cloud" and the access token and phone number id are configured. SendAsync never throws for a provider problem: it returns Success = false.
    /// </summary>
    public interface IWhatsAppSender
    {
        Task<WhatsAppSendResult> SendAsync(WhatsAppMessage message, CancellationToken ct);
    }
}
