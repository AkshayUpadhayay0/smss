using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>
    /// Firebase settings. CredentialsPath is read ONLY from configuration (key Firebase:CredentialsPath, environment variable Firebase__CredentialsPath)
    /// and is never logged or stored. DryRun defaults to TRUE: FCM validates every message and delivers nothing until it is switched off on purpose.
    /// </summary>
    public class FirebaseOptions
    {
        public const string Section = "Firebase";
        public string? CredentialsPath { get; set; }
        public bool DryRun { get; set; } = true;
    }

    public class FcmSender : IFcmSender
    {
        private readonly FirebaseOptions _options;
        private readonly ILogger<FcmSender> _logger;
        private readonly object _gate = new();
        private bool _ready;

        public FcmSender(IOptions<FirebaseOptions> options, ILogger<FcmSender> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public bool TryEnsureReady(out string reason)
        {
            if (_ready) { reason = string.Empty; return true; }
            lock (_gate)
            {
                if (_ready) { reason = string.Empty; return true; }
                if (string.IsNullOrWhiteSpace(_options.CredentialsPath))
                {
                    reason = "Firebase:CredentialsPath is not configured.";
                    return false;
                }
                try
                {
                    // Only the SDK reads the file. A failure is reported by exception TYPE alone: its message could quote the path or the file.
                    FirebaseApp.Create(new AppOptions { Credential = CredentialFactory.FromFile<ServiceAccountCredential>(_options.CredentialsPath).ToGoogleCredential() });
                    _ready = true;
                    reason = string.Empty;
                    return true;
                }
                catch (ArgumentException) when (FirebaseApp.DefaultInstance != null)
                {
                    // The default app already exists (created earlier in this process)
                    _ready = true;
                    reason = string.Empty;
                    return true;
                }
                catch (Exception ex)
                {
                    reason = $"The Firebase credentials could not be loaded ({ex.GetType().Name}). Check Firebase:CredentialsPath.";
                    return false;
                }
            }
        }

        public async Task<IReadOnlyList<FcmResult>> SendAsync(IReadOnlyList<FcmMessage> messages, CancellationToken ct)
        {
            if (messages.Count == 0) return Array.Empty<FcmResult>();
            if (!TryEnsureReady(out var reason)) throw new InvalidOperationException(reason);

            // Message.Token is marked obsolete in this SDK version in favour of Fid (a Firebase installation id), but the mobile app registers the FCM
            // REGISTRATION token (messaging().getToken()), which is still what Token carries.
#pragma warning disable CS0618
            var payload = messages.Select(m => new Message
            {
                Token = m.Token,
                Notification = new Notification { Title = m.Title, Body = m.Body },
                Data = new Dictionary<string, string> { ["type"] = m.DataType },
                Android = new AndroidConfig { Priority = Priority.High }
            }).ToList();
#pragma warning restore CS0618

            var results = new List<FcmResult>(messages.Count);
            // SendEachAsync, never SendAllAsync (the old batch endpoint is disabled). 500 messages per call is the SDK limit.
            for (var i = 0; i < payload.Count; i += 500)
            {
                var chunk = payload.Skip(i).Take(500).ToList();
                BatchResponse batch;
                try
                {
                    batch = await FirebaseMessaging.DefaultInstance.SendEachAsync(chunk, _options.DryRun, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // The call as a whole failed (network, auth): every message of the chunk is a transient failure; nothing about the call is logged beyond the type
                    _logger.LogWarning("FCM send call failed ({ErrorType}) for a batch of {Count}", ex.GetType().Name, chunk.Count);
                    results.AddRange(chunk.Select(_ => new FcmResult(FcmResultKind.Transient, ex.GetType().Name)));
                    continue;
                }
                results.AddRange(batch.Responses.Select(Classify));
            }
            return results;
        }

        private static FcmResult Classify(SendResponse r)
        {
            if (r.IsSuccess) return new FcmResult(FcmResultKind.Success);
            var code = r.Exception?.MessagingErrorCode;
            var name = code?.ToString() ?? r.Exception?.ErrorCode.ToString();
            return code switch
            {
                MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument or MessagingErrorCode.SenderIdMismatch => new FcmResult(FcmResultKind.InvalidToken, name),
                MessagingErrorCode.Unavailable or MessagingErrorCode.Internal or MessagingErrorCode.QuotaExceeded => new FcmResult(FcmResultKind.Transient, name),
                _ => new FcmResult(FcmResultKind.Permanent, name)
            };
        }
    }
}
