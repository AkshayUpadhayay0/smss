using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using smss_api_db_layer.@interface;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>The webhook answers Meta's raw contract (a plain challenge / empty 200), not the API envelope.</summary>
    public sealed record WebhookVerifyResult(int StatusCode, string? Challenge);

    public interface IWhatsAppWebhookService
    {
        /// <summary>Meta's one-time subscription check (GET). 404 while the webhook is disabled, 403 for a wrong token, 200 + the challenge otherwise.</summary>
        WebhookVerifyResult Verify(string? mode, string? verifyToken, string? challenge);
        /// <summary>Delivery status events (POST). 404 while disabled, 401 for a bad or missing signature, 200 once the signed body was handled. Never throws for a malformed body.</summary>
        Task<int> HandleAsync(byte[] body, string? signatureHeader, CancellationToken ct);
    }

    /// <summary>
    /// Delivery status from Meta (sent / delivered / read / failed) written onto the delivery log row with the same provider message id. DISABLED (404) unless WhatsApp:Enabled and WhatsApp:AppSecret are set; every POST must carry a valid
    /// X-Hub-Signature-256 (HMAC-SHA256 of the raw body with the app secret, compared in constant time). Nothing from the body is logged except counts.
    /// </summary>
    public class WhatsAppWebhookService : IWhatsAppWebhookService
    {
        private readonly WhatsAppOptions _options;
        private readonly IMessagingRepository _repo;
        private readonly ILogger<WhatsAppWebhookService> _logger;

        public WhatsAppWebhookService(IOptions<WhatsAppOptions> options, IMessagingRepository repo, ILogger<WhatsAppWebhookService> logger)
        {
            _options = options.Value; _repo = repo; _logger = logger;
        }

        public WebhookVerifyResult Verify(string? mode, string? verifyToken, string? challenge)
        {
            if (!_options.WebhookEnabled || string.IsNullOrWhiteSpace(_options.WebhookVerifyToken)) return new(404, null);
            if (mode == "subscribe" && verifyToken != null && challenge != null && SameText(verifyToken, _options.WebhookVerifyToken!)) return new(200, challenge);
            return new(403, null);
        }

        public async Task<int> HandleAsync(byte[] body, string? signatureHeader, CancellationToken ct)
        {
            if (!_options.WebhookEnabled) return 404;
            if (!IsValidSignature(_options.AppSecret!, body, signatureHeader)) return 401;
            var applied = 0; var seen = 0;
            try
            {
                using var doc = JsonDocument.Parse(body);
                foreach (var status in Statuses(doc.RootElement))
                {
                    seen++;
                    if (await _repo.ApplyProviderStatusAsync(status.Id, status.Status, status.ErrorCode, status.ErrorMessage, ct)) applied++;
                }
            }
            catch (JsonException) { _logger.LogWarning("A WhatsApp webhook body with a valid signature was not valid JSON"); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning("A WhatsApp webhook status could not be applied ({Error})", ex.GetType().Name); }
            _logger.LogInformation("WhatsApp webhook: {Seen} status events, {Applied} matched a delivery", seen, applied);
            return 200;
        }

        /// <summary>"sha256=" + lower-case hex HMAC-SHA256(secret, body), compared without leaking where it differs.</summary>
        public static bool IsValidSignature(string appSecret, byte[] body, string? header)
        {
            if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)) return false;
            var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body)).ToLowerInvariant();
            return SameText(header[7..].Trim().ToLowerInvariant(), expected);
        }

        private static bool SameText(string a, string b) => CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));

        private sealed record Event(string Id, string Status, string? ErrorCode, string? ErrorMessage);

        private static IEnumerable<Event> Statuses(JsonElement root)
        {
            if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array) yield break;
            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) continue;
                foreach (var change in changes.EnumerateArray())
                {
                    if (!change.TryGetProperty("value", out var value) || !value.TryGetProperty("statuses", out var statuses) || statuses.ValueKind != JsonValueKind.Array) continue;
                    foreach (var s in statuses.EnumerateArray())
                    {
                        var id = s.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
                        var raw = s.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() : null;
                        var mapped = raw switch { "sent" => "SENT", "delivered" => "DELIVERED", "read" => "READ", "failed" => "FAILED", _ => null };
                        if (string.IsNullOrEmpty(id) || id.Length > 200 || mapped == null) continue;
                        string? code = null, message = null;
                        if (mapped == "FAILED")
                        {
                            int? n = s.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array && errs.GetArrayLength() > 0
                                     && errs[0].TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : null;
                            code = n != null ? "META_" + n : "PROVIDER_FAILED";
                            message = n != null ? $"WhatsApp reported that the message failed (code {n})." : "WhatsApp reported that the message failed.";   // fixed text: Meta's own wording is never stored
                        }
                        yield return new Event(id, mapped, code, message);
                    }
                }
            }
        }
    }
}
