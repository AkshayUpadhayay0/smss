using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class NotificationService : INotificationService
    {
        private readonly IEmailService _email;
        private readonly IConfiguration _config;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(IEmailService email, IConfiguration config, ILogger<NotificationService> logger)
        {
            _email = email;
            _config = config;
            _logger = logger;
        }

        public string? LoginUrl => _config["Email:LoginUrl"];

        /// <summary>The person's own email when they have one, else their guardian's, else null.</summary>
        public static string? ResolveNotificationEmail(string? ownEmail, string? guardianEmail)
        {
            if (!string.IsNullOrWhiteSpace(ownEmail)) return ownEmail.Trim();
            if (!string.IsNullOrWhiteSpace(guardianEmail)) return guardianEmail.Trim();
            return null;
        }

        public async Task<string?> SendAsync(string? ownEmail, string? guardianEmail, (string Subject, string Html) message, string context, CancellationToken ct)
        {
            var recipient = ResolveNotificationEmail(ownEmail, guardianEmail);
            if (recipient == null)
            {
                _logger.LogInformation("No email address available for notification '{Context}'; skipped", context);
                return null;
            }

            try
            {
                await _email.SendAsync(recipient, message.Subject, message.Html, ct);
                return recipient;
            }
            catch (Exception ex)
            {
                // The operation that triggered this has already succeeded — a mail problem must not undo or fail it
                _logger.LogWarning(ex, "Failed to send '{Context}' email to {Email}", context, recipient);
                return null;
            }
        }
    }
}
