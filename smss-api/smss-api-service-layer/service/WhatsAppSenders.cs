using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>The default sender: nothing leaves the server. It writes one log line (the template code only: no number, no variables, no link) and reports Success with Kind "LOG", so the delivery log shows LOGGED.</summary>
    public class LogOnlyWhatsAppSender : IWhatsAppSender
    {
        private readonly ILogger<LogOnlyWhatsAppSender> _logger;
        public LogOnlyWhatsAppSender(ILogger<LogOnlyWhatsAppSender> logger) => _logger = logger;

        public Task<WhatsAppSendResult> SendAsync(WhatsAppMessage message, CancellationToken ct)
        {
            _logger.LogInformation("WhatsApp log-only sender: template {Template} was NOT sent (WhatsApp sending is off)", message.Content.TemplateCode);
            return Task.FromResult(new WhatsAppSendResult(true, "LOG", null, null, null));
        }
    }

    /// <summary>
    /// The Meta WhatsApp Cloud API. POST {BaseUrl}/{ApiVersion}/{phone number id}/messages with a bearer token. The access token is put in the request header only; nothing about the request or the response body is logged
    /// (only the HTTP status and Meta's numeric error code). A network or provider problem comes back as Success = false with a short sanitised error, never as an exception.
    /// </summary>
    public class MetaCloudWhatsAppSender : IWhatsAppSender
    {
        private static readonly Regex Version = new(@"^v[0-9]{1,2}\.[0-9]{1,2}$", RegexOptions.Compiled);
        private static readonly Regex PhoneId = new(@"^[0-9]{5,50}$", RegexOptions.Compiled);
        private readonly HttpClient _http;
        private readonly WhatsAppOptions _options;
        private readonly ILogger<MetaCloudWhatsAppSender> _logger;

        public MetaCloudWhatsAppSender(HttpClient http, IOptions<WhatsAppOptions> options, ILogger<MetaCloudWhatsAppSender> logger)
        {
            _http = http; _options = options.Value; _logger = logger;
        }

        public async Task<WhatsAppSendResult> SendAsync(WhatsAppMessage message, CancellationToken ct)
        {
            var phoneId = string.IsNullOrWhiteSpace(message.SenderPhoneNumberId) ? _options.PhoneNumberId : message.SenderPhoneNumberId;
            if (string.IsNullOrWhiteSpace(_options.AccessToken) || string.IsNullOrWhiteSpace(phoneId) || !PhoneId.IsMatch(phoneId) || !Version.IsMatch(_options.ApiVersion ?? ""))
                return Fail("NOT_CONFIGURED", "WhatsApp sending is not configured.");
            var to = new string(message.ToNumber.Where(char.IsDigit).ToArray());   // Meta wants the number without the plus sign
            if (to.Length < 8) return Fail("BAD_NUMBER", "The WhatsApp number is not valid.");

            var components = new List<object>
            {
                new { type = "body", parameters = message.Content.BodyParameters.Select(p => new { type = "text", text = p }).ToArray() },
            };
            if (message.Content.ButtonParameter != null)
                components.Add(new { type = "button", sub_type = "url", index = "0", parameters = new[] { new { type = "text", text = message.Content.ButtonParameter } } });
            var payload = new
            {
                messaging_product = "whatsapp", to, type = "template",
                template = new { name = _options.MetaNameOf(message.Content.TemplateCode), language = new { code = _options.Language }, components },
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/{_options.ApiVersion}/{phoneId}/messages")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 60)));
                using var response = await _http.SendAsync(request, timeout.Token);
                var body = await response.Content.ReadAsStringAsync(timeout.Token);
                if (response.IsSuccessStatusCode)
                {
                    var id = ReadMessageId(body);
                    return id == null ? Fail("NO_MESSAGE_ID", "WhatsApp accepted the request but gave no message id.") : new WhatsAppSendResult(true, "CLOUD", id, null, null);
                }
                var metaCode = ReadErrorCode(body);
                _logger.LogWarning("WhatsApp Cloud API refused a message: HTTP {Status}, Meta error code {MetaCode}, template {Template}", (int)response.StatusCode, metaCode, message.Content.TemplateCode);
                return response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => Fail("AUTH", "WhatsApp refused our access token."),
                    HttpStatusCode.TooManyRequests => Fail("RATE_LIMIT", "WhatsApp is limiting how fast we can send."),
                    _ => Fail(metaCode != null ? "META_" + metaCode : "HTTP_" + (int)response.StatusCode, metaCode != null ? $"WhatsApp could not send the message (code {metaCode})." : $"WhatsApp could not send the message (HTTP {(int)response.StatusCode})."),
                };
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("WhatsApp Cloud API call timed out (template {Template})", message.Content.TemplateCode);
                return Fail("TIMEOUT", "WhatsApp did not answer in time.");
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException)
            {
                // the exception TYPE only: its message could quote the address
                _logger.LogWarning("WhatsApp Cloud API call failed ({Error}, template {Template})", ex.GetType().Name, message.Content.TemplateCode);
                return Fail("NETWORK", "WhatsApp could not be reached.");
            }
        }

        private static WhatsAppSendResult Fail(string code, string message) => new(false, "CLOUD", null, code, message);

        private static string? ReadMessageId(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                return doc.RootElement.TryGetProperty("messages", out var m) && m.ValueKind == JsonValueKind.Array && m.GetArrayLength() > 0 && m[0].TryGetProperty("id", out var id) ? id.GetString() : null;
            }
            catch (JsonException) { return null; }
        }

        private static int? ReadErrorCode(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                return doc.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : null;
            }
            catch (JsonException) { return null; }
        }
    }

    /// <summary>
    /// What the rest of the API injects as IWhatsAppSender. It picks per call: the Meta Cloud sender when WhatsApp:Enabled + Sender "Cloud" + access token + phone number id are all present, otherwise the log-only sender.
    /// A half-configured Cloud setting (for example the token is missing) therefore never sends and never throws; the delivery log shows sender kind LOG.
    /// </summary>
    public class ConfiguredWhatsAppSender : IWhatsAppSender
    {
        private readonly IOptions<WhatsAppOptions> _options;
        private readonly LogOnlyWhatsAppSender _log;
        private readonly MetaCloudWhatsAppSender _cloud;
        public ConfiguredWhatsAppSender(IOptions<WhatsAppOptions> options, LogOnlyWhatsAppSender log, MetaCloudWhatsAppSender cloud)
        {
            _options = options; _log = log; _cloud = cloud;
        }

        public Task<WhatsAppSendResult> SendAsync(WhatsAppMessage message, CancellationToken ct) =>
            _options.Value.IsCloudSender ? _cloud.SendAsync(message, ct) : _log.SendAsync(message, ct);
    }
}
