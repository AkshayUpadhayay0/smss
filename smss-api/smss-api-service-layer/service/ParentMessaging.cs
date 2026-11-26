using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class UserActionTokenService : IUserActionTokenService
    {
        private readonly IMessagingRepository _repo;
        private readonly ParentAccountsOptions _options;
        public UserActionTokenService(IMessagingRepository repo, IOptions<ParentAccountsOptions> options)
        {
            _repo = repo; _options = options.Value;
        }

        public async Task<IssuedLink> IssueAsync(long userId, string? schoolId, string purpose, long? createdBy, CancellationToken ct)
        {
            if (!ActionTokenPurposes.IsKnown(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose));
            var lifetime = purpose == ActionTokenPurposes.ResetPassword ? TimeSpan.FromMinutes(Math.Max(1, _options.ResetLinkMinutes)) : TimeSpan.FromHours(Math.Max(1, _options.SetPasswordLinkHours));
            var token = UserActionTokens.NewToken();
            await _repo.IssueTokenAsync(userId, schoolId, purpose, UserActionTokens.Hash(token), lifetime, createdBy, ct);
            return new IssuedLink(token, DateTime.UtcNow + lifetime);
        }

        public async Task<ConsumedLink?> ConsumeAsync(string? token, string purpose, CancellationToken ct)
        {
            if (!UserActionTokens.LooksValid(token) || !ActionTokenPurposes.IsKnown(purpose)) return null;
            var row = await _repo.ConsumeTokenAsync(UserActionTokens.Hash(token!), purpose, ct);
            return row == null ? null : new ConsumedLink(row.UserId, row.SchoolId);
        }

        public async Task<bool> IsUsableAsync(string? token, string purpose, CancellationToken ct)
        {
            if (!UserActionTokens.LooksValid(token) || !ActionTokenPurposes.IsKnown(purpose)) return false;
            var row = await _repo.PeekTokenAsync(UserActionTokens.Hash(token!), ct);
            return row != null && row.Purpose == purpose && row.UsedAt == null && row.RevokedAt == null && row.ExpiresAt > row.DbNow;
        }

        public string? BuildLink(string purpose, string token) =>
            string.IsNullOrWhiteSpace(_options.WebBaseUrl) ? null : UserActionTokens.BuildLink(_options.WebBaseUrl, purpose, token);
    }

    public class GuardianLoginIdService : IGuardianLoginIdService
    {
        private const int MaxAttempts = 50;
        private readonly IMessagingRepository _repo;
        public GuardianLoginIdService(IMessagingRepository repo) => _repo = repo;

        public async Task<string?> AllocateAsync(string schoolId, CancellationToken ct)
        {
            var code = await _repo.GetSchoolShortCodeAsync(schoolId, ct);
            if (string.IsNullOrWhiteSpace(code)) return null;
            for (var i = 0; i < MaxAttempts; i++)
            {
                var candidate = GuardianLoginIds.Build(code, await _repo.NextGuardianNumberAsync(schoolId, ct));   // atomic: two requests never get the same number
                if (!await _repo.LoginIdTakenAsync(candidate, ct)) return candidate;                              // skip any number that already equals a login id or a student id
            }
            throw new InvalidOperationException("No free guardian login id could be allocated.");
        }
    }

    public class ParentMessageDispatcher : IParentMessageDispatcher
    {
        private readonly IMessagingRepository _repo;
        private readonly ISchoolWhatsAppSenderRepository _senders;
        private readonly IWhatsAppSender _whatsApp;
        private readonly INotificationService _email;
        private readonly ILogger<ParentMessageDispatcher> _logger;

        public ParentMessageDispatcher(IMessagingRepository repo, ISchoolWhatsAppSenderRepository senders, IWhatsAppSender whatsApp, INotificationService email, ILogger<ParentMessageDispatcher> logger)
        {
            _repo = repo; _senders = senders; _whatsApp = whatsApp; _email = email; _logger = logger;
        }

        public async Task<IReadOnlyList<DeliveryOutcome>> SendAsync(ParentMessage m, CancellationToken ct)
        {
            var outcomes = new List<DeliveryOutcome>();
            if (m.WhatsApp != null) outcomes.Add(await SendWhatsAppAsync(m, ct));
            if (m.Email != null) outcomes.Add(await SendEmailAsync(m, ct));
            return outcomes;
        }

        private async Task<DeliveryOutcome> SendWhatsAppAsync(ParentMessage m, CancellationToken ct)
        {
            var row = Row(m, "WHATSAPP", m.WhatsAppNumber);
            try
            {
                if (string.IsNullOrWhiteSpace(m.WhatsAppNumber))
                {
                    row.Status = "SKIPPED"; row.SenderKind = "LOG"; row.ErrorCode = "NO_NUMBER"; row.ErrorMessage = "The guardian has no WhatsApp number on file.";
                }
                else
                {
                    string? senderPhoneId = null;
                    row.SenderScope = "PLATFORM";
                    try
                    {
                        var sender = await _senders.GetSenderAsync(m.SchoolId, false, ct);
                        if (sender is { Status: "ACTIVE", PhoneNumberId: not null }) { senderPhoneId = sender.PhoneNumberId; row.SenderScope = "SCHOOL"; }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning("Could not read the school's WhatsApp sender ({Error}); the platform number is used", ex.GetType().Name);
                    }
                    var result = await _whatsApp.SendAsync(new WhatsAppMessage(m.WhatsAppNumber, m.WhatsApp!, senderPhoneId), ct);
                    row.SenderKind = result.Kind == "CLOUD" ? "CLOUD" : "LOG";
                    row.ProviderMessageId = result.ProviderMessageId;
                    if (result.Success)
                    {
                        row.Status = result.Kind == "CLOUD" ? "SENT" : "LOGGED";
                        if (row.Status == "SENT") row.SentAt = DateTime.UtcNow;
                    }
                    else
                    {
                        row.Status = "FAILED"; row.ErrorCode = Code(result.ErrorCode); row.ErrorMessage = DeliveryText.Sanitise(result.ErrorMessage);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("WhatsApp send for template {Template} failed unexpectedly ({Error})", m.TemplateCode, ex.GetType().Name);
                row.Status = "FAILED"; row.SenderKind = "LOG"; row.ErrorCode = "UNEXPECTED"; row.ErrorMessage = "The message could not be sent.";
            }
            return await SaveAsync(row, ct);
        }

        private async Task<DeliveryOutcome> SendEmailAsync(ParentMessage m, CancellationToken ct)
        {
            var row = Row(m, "EMAIL", m.EmailAddress);
            row.SenderKind = "SMTP";
            if (string.IsNullOrWhiteSpace(m.EmailAddress))
            {
                row.Status = "SKIPPED"; row.ErrorCode = "NO_EMAIL"; row.ErrorMessage = "The guardian has no email address on file.";
            }
            else
            {
                // the existing NotificationService: never throws, returns the address when the mail was sent and null when it was not
                var sentTo = await _email.SendAsync(m.EmailAddress, null, m.Email!.Value, "parent:" + m.TemplateCode, ct);
                if (sentTo != null) { row.Status = "SENT"; row.SentAt = DateTime.UtcNow; }
                else { row.Status = "FAILED"; row.ErrorCode = "EMAIL_FAILED"; row.ErrorMessage = "The email could not be sent."; }
            }
            return await SaveAsync(row, ct);
        }

        private static TbMessageDelivery Row(ParentMessage m, string channel, string? recipient) => new()
        {
            SchoolId = m.SchoolId, GuardianId = m.GuardianId, UserId = m.UserId, TemplateCode = m.TemplateCode, Purpose = m.Purpose, Channel = channel,
            RecipientMasked = DeliveryText.MaskRecipient(recipient), Attempts = 1, CreatedBy = m.CreatedBy, ResendOfDeliveryId = m.ResendOfDeliveryId,
            Status = "FAILED", SenderKind = "LOG",
        };

        private static string Code(string? code) => string.IsNullOrWhiteSpace(code) ? "SEND_FAILED" : code.Length <= 40 ? code : code[..40];

        private async Task<DeliveryOutcome> SaveAsync(TbMessageDelivery row, CancellationToken ct)
        {
            try
            {
                var id = await _repo.AddDeliveryAsync(row, ct);
                _logger.LogInformation("Delivery {DeliveryId}: {Channel} {Template} -> {Status} ({Kind})", id, row.Channel, row.TemplateCode, row.Status, row.SenderKind);
                return new DeliveryOutcome(id, row.Channel, row.Status, row.ErrorCode);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // the log row could not be written: the message already went (or did not); the caller is never failed by this
                _logger.LogWarning("The delivery log row for {Channel} {Template} could not be written ({Error})", row.Channel, row.TemplateCode, ex.GetType().Name);
                return new DeliveryOutcome(null, row.Channel, row.Status, row.ErrorCode);
            }
        }
    }
}
