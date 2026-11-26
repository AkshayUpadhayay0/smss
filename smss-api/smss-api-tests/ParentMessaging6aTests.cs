using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using smss_api.Controllers;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_db_layer.repository;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Stage 6a: the foundations of parent accounts and messaging. One-time link tokens (hashed, expiring, single use), the delivery log, the guardian login id allocator, the WhatsApp sender (log-only default, Meta Cloud behind
    /// WhatsApp:Enabled; NO real Meta call anywhere: a stub HttpMessageHandler), the signed delivery-status webhook, the consent columns, and "no secret, token or link in any log or log row".
    /// The [DbFact] tests need SMSS_TEST_PG and a template with the 6a migration (smss_tpl_6a).
    /// </summary>
    public class ParentMessaging6aTests
    {
        private const string A = TaxDb.SchoolA;
        private const string B = TaxDb.SchoolB;
        private const long UserA = TaxDb.UserA;
        private const string AccessToken = "EAAG-ACCESS-TOKEN-SHOULD-NEVER-APPEAR-IN-ANY-LOG-OR-ROW";
        private const string AppSecret = "app-secret-should-never-appear-anywhere-0123456789";
        private const string Number = "+919876543210";

        // ------------------------------------------------------------------------------------------------ test doubles
        private sealed class CapLog<T> : ILogger<T>
        {
            public readonly List<string> Lines = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                Lines.Add(formatter(state, exception) + (exception == null ? "" : " | " + exception));
            public string All => string.Join("\n", Lines);
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _reply;
            public readonly List<(HttpRequestMessage Request, string Body)> Calls = new();
            public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) => _reply = reply;
            public static StubHandler Json(HttpStatusCode code, string json) => new((_, _) => Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") }));
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Calls.Add((request, request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct)));
                return await _reply(request, ct);
            }
        }

        private sealed class FakeSender : IWhatsAppSender
        {
            public readonly List<WhatsAppMessage> Sent = new();
            public Func<WhatsAppMessage, WhatsAppSendResult> Reply { get; set; } = _ => new WhatsAppSendResult(true, "LOG", null, null, null);
            public bool Throw { get; set; }
            public Task<WhatsAppSendResult> SendAsync(WhatsAppMessage message, CancellationToken ct)
            {
                Sent.Add(message);
                if (Throw) throw new InvalidOperationException("boom " + AccessToken);
                return Task.FromResult(Reply(message));
            }
        }

        private sealed class FakeMail : INotificationService
        {
            public string? LoginUrl => null;
            public bool Succeeds { get; set; } = true;
            public readonly List<string> To = new();
            public Task<string?> SendAsync(string? ownEmail, string? guardianEmail, (string Subject, string Html) message, string context, CancellationToken ct)
            {
                To.Add(ownEmail ?? "");
                return Task.FromResult(Succeeds ? ownEmail : null);
            }
        }

        private static WhatsAppOptions Opts(bool enabled = true, string sender = "Cloud", string? token = AccessToken, string? phoneId = "100200300400") =>
            new() { Enabled = enabled, Sender = sender, AccessToken = token, PhoneNumberId = phoneId, AppSecret = AppSecret, WebhookVerifyToken = "verify-me" };

        private static MetaCloudWhatsAppSender Meta(StubHandler h, WhatsAppOptions o, ILogger<MetaCloudWhatsAppSender>? log = null) =>
            new(new HttpClient(h), Options.Create(o), log ?? NullLogger<MetaCloudWhatsAppSender>.Instance);

        private static WhatsAppMessage Msg(string? senderPhoneId = null, string token = "Qm9X3kP0aT7vL2nR8sY4cE6wH1dJ5uZfG9bV-qA_MtI") =>
            new(Number, ParentTemplates.AccountReady("Asha Rao", "Ravi Rao", "Green Valley School", "GVS001GD001", 72, token), senderPhoneId);

        // ------------------------------------------------------------------------------------------------ tokens: hashing
        [Fact]
        public void A_token_is_43_url_safe_characters_random_each_time_and_only_its_sha256_hash_is_what_is_stored()
        {
            var seen = new HashSet<string>();
            for (var i = 0; i < 200; i++)
            {
                var t = UserActionTokens.NewToken();
                Assert.Equal(43, t.Length);
                Assert.Matches("^[A-Za-z0-9_-]{43}$", t);
                Assert.True(seen.Add(t));
                Assert.True(UserActionTokens.LooksValid(t));
            }
            var token = UserActionTokens.NewToken();
            var hash = UserActionTokens.Hash(token);
            Assert.Matches("^[0-9a-f]{64}$", hash);
            Assert.Equal(hash, UserActionTokens.Hash(token));                                   // deterministic
            Assert.NotEqual(hash, UserActionTokens.Hash(UserActionTokens.NewToken()));
            Assert.DoesNotContain(token, hash);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant(), hash);   // a plain SHA-256 of the token
        }

        [Theory]
        [InlineData(null)] [InlineData("")] [InlineData("short")] [InlineData("Qm9X3kP0aT7vL2nR8sY4cE6wH1dJ5uZfG9bV-qA_MtIx")] [InlineData("Qm9X3kP0aT7vL2nR8sY4cE6wH1dJ5uZfG9bV-qA_Mt!")] [InlineData("' OR 1=1 --")]
        public void Anything_that_cannot_be_a_token_we_issued_is_refused_before_it_is_even_hashed(string? text) => Assert.False(UserActionTokens.LooksValid(text));

        [Fact]
        public void The_link_puts_the_token_in_the_query_of_the_website_page_and_needs_the_configured_web_address()
        {
            Assert.Equal("https://site.example/parent/set-password?t=abc", UserActionTokens.BuildLink("https://site.example/", ActionTokenPurposes.SetPassword, "abc"));
            Assert.Equal("https://site.example/parent/reset-password?t=abc", UserActionTokens.BuildLink("https://site.example", ActionTokenPurposes.ResetPassword, "abc"));
            var none = new UserActionTokenService(Fake.Of<IMessagingRepository>(), Options.Create(new ParentAccountsOptions()));
            Assert.Null(none.BuildLink(ActionTokenPurposes.SetPassword, "abc"));                // no WebBaseUrl: no link
            var set = new UserActionTokenService(Fake.Of<IMessagingRepository>(), Options.Create(new ParentAccountsOptions { WebBaseUrl = "https://site.example" }));
            Assert.StartsWith("https://site.example/parent/set-password?t=", set.BuildLink(ActionTokenPurposes.SetPassword, "abc"));
        }

        // ------------------------------------------------------------------------------------------------ guardian login ids, delivery text
        [Fact]
        public void A_guardian_login_id_is_short_code_GD_number_and_can_never_look_like_a_student_id()
        {
            Assert.Equal("GHS001GD001", GuardianLoginIds.Build("GHS001", 1));
            Assert.Equal("GHS001GD1234", GuardianLoginIds.Build("GHS001", 1234));
            Assert.True(GuardianLoginIds.LooksLikeGuardianId("GHS001GD001"));
            Assert.False(GuardianLoginIds.LooksLikeGuardianId(StudentIdHelper.Build("GHS001", 1)));   // GHS001ST001
            Assert.False(GuardianLoginIds.LooksLikeGuardianId("GHS001GD"));
        }

        [Theory]
        [InlineData("+919876543210", "+91******3210")]
        [InlineData("9876543210", "******3210")]
        [InlineData("asha@gmail.com", "a***@gmail.com")]
        [InlineData("12", "***")]
        public void The_recipient_is_stored_masked(string raw, string masked) => Assert.Equal(masked, DeliveryText.MaskRecipient(raw));

        [Fact]
        public void An_error_text_loses_links_token_like_strings_and_numbers_and_is_capped()
        {
            var t = DeliveryText.Sanitise($"failed for +919876543210 at https://site.example/parent/set-password?t=Qm9X3kP0aT7vL2nR8sY4cE6wH1dJ5uZfG9bV-qA_MtI with {AccessToken}")!;
            Assert.DoesNotContain("9876543210", t); Assert.DoesNotContain("https", t); Assert.DoesNotContain("Qm9X3", t); Assert.DoesNotContain(AccessToken, t);
            Assert.True(DeliveryText.Sanitise(new string('a', 1000) + " x")!.Length <= DeliveryText.MaxError);
            Assert.Null(DeliveryText.Sanitise("  "));
        }

        // ------------------------------------------------------------------------------------------------ configuration: off by default, secrets empty
        [Fact]
        public void Everything_is_off_and_every_secret_is_empty_in_the_committed_settings()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "smss-api", "smss-api", "appsettings.json"))) dir = dir.Parent;
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "smss-api", "smss-api", "appsettings.json")));
            var w = doc.RootElement.GetProperty("WhatsApp");
            Assert.False(w.GetProperty("Enabled").GetBoolean());
            Assert.Equal("Log", w.GetProperty("Sender").GetString());
            foreach (var key in new[] { "AccessToken", "AppSecret", "WebhookVerifyToken", "PhoneNumberId", "BusinessAccountId" }) Assert.Equal("", w.GetProperty(key).GetString());
            Assert.Equal("", doc.RootElement.GetProperty("ParentAccounts").GetProperty("WebBaseUrl").GetString());
            var o = new WhatsAppOptions();   // the code defaults agree
            Assert.False(o.Enabled); Assert.Equal("Log", o.Sender); Assert.False(o.IsCloudSender); Assert.False(o.WebhookEnabled);
            var p = new ParentAccountsOptions();
            Assert.Equal(72, p.SetPasswordLinkHours); Assert.Equal(60, p.ResetLinkMinutes); Assert.Equal(3, p.ResendLimitPer24Hours);
        }

        [Theory]
        [InlineData(true, "Cloud", "tok", "123456", true)]
        [InlineData(false, "Cloud", "tok", "123456", false)]
        [InlineData(true, "Log", "tok", "123456", false)]
        [InlineData(true, "Cloud", "", "123456", false)]
        [InlineData(true, "Cloud", "tok", "", false)]
        [InlineData(true, "cloud", "tok", "123456", true)]
        public void Real_sending_needs_the_switch_the_Cloud_sender_the_token_and_the_phone_number_id(bool enabled, string sender, string token, string phoneId, bool cloud) =>
            Assert.Equal(cloud, new WhatsAppOptions { Enabled = enabled, Sender = sender, AccessToken = token, PhoneNumberId = phoneId }.IsCloudSender);

        [Fact]
        public void The_webhook_needs_the_switch_and_the_app_secret()
        {
            Assert.False(new WhatsAppOptions { Enabled = true, AppSecret = "" }.WebhookEnabled);
            Assert.False(new WhatsAppOptions { Enabled = false, AppSecret = "s" }.WebhookEnabled);
            Assert.True(new WhatsAppOptions { Enabled = true, AppSecret = "s" }.WebhookEnabled);
        }

        // ------------------------------------------------------------------------------------------------ the log-only sender and the choice of sender
        [Fact]
        public async Task The_log_only_sender_sends_nothing_and_logs_the_template_code_only()
        {
            var log = new CapLog<LogOnlyWhatsAppSender>();
            var r = await new LogOnlyWhatsAppSender(log).SendAsync(Msg(), default);
            Assert.True(r.Success); Assert.Equal("LOG", r.Kind); Assert.Null(r.ProviderMessageId);
            Assert.Contains("parent_account_ready", log.All);
            foreach (var secret in new[] { "9876543210", "Asha", "Ravi", "GVS001GD001", "Qm9X3kP0aT7vL2nR8sY4cE6wH1dJ5uZfG9bV-qA_MtI" }) Assert.DoesNotContain(secret, log.All);   // no number, name, id or token
        }

        [Theory]
        [InlineData(false, "Cloud", AccessToken, "1234567", 0)]      // switch off
        [InlineData(true, "Log", AccessToken, "1234567", 0)]         // Log sender chosen
        [InlineData(true, "Cloud", "", "1234567", 0)]                // token missing: half configured never sends
        [InlineData(true, "Cloud", AccessToken, "1234567", 1)]       // fully configured
        public async Task The_configured_sender_uses_Meta_only_when_it_is_fully_switched_on_and_otherwise_the_log_only_sender(bool enabled, string sender, string token, string phoneId, int metaCalls)
        {
            var handler = StubHandler.Json(HttpStatusCode.OK, "{\"messages\":[{\"id\":\"wamid.X1\"}]}");
            var o = Options.Create(Opts(enabled, sender, token, phoneId));
            var chosen = new ConfiguredWhatsAppSender(o, new LogOnlyWhatsAppSender(NullLogger<LogOnlyWhatsAppSender>.Instance), Meta(handler, o.Value));
            var r = await chosen.SendAsync(Msg(), default);
            Assert.Equal(metaCalls, handler.Calls.Count);
            Assert.Equal(metaCalls == 1 ? "CLOUD" : "LOG", r.Kind);
        }

        // ------------------------------------------------------------------------------------------------ the Meta Cloud sender (stub handler: never a real call)
        [Fact]
        public async Task The_Meta_sender_posts_the_template_with_the_body_variables_and_the_button_token_to_the_right_phone_number()
        {
            var handler = StubHandler.Json(HttpStatusCode.OK, "{\"messaging_product\":\"whatsapp\",\"messages\":[{\"id\":\"wamid.ABC\"}]}");
            var r = await Meta(handler, Opts()).SendAsync(Msg(), default);
            Assert.True(r.Success); Assert.Equal("CLOUD", r.Kind); Assert.Equal("wamid.ABC", r.ProviderMessageId);
            var (req, body) = Assert.Single(handler.Calls);
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://graph.facebook.com/v21.0/100200300400/messages", req.RequestUri!.ToString());
            Assert.Equal("Bearer " + AccessToken, req.Headers.Authorization!.ToString());
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            Assert.Equal("919876543210", root.GetProperty("to").GetString());                  // no plus sign
            Assert.Equal("template", root.GetProperty("type").GetString());
            var t = root.GetProperty("template");
            Assert.Equal("parent_account_ready", t.GetProperty("name").GetString());
            Assert.Equal("en", t.GetProperty("language").GetProperty("code").GetString());
            var comps = t.GetProperty("components");
            var texts = comps[0].GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("text").GetString()).ToArray();
            Assert.Equal(new[] { "Asha Rao", "Ravi Rao", "Green Valley School", "GVS001GD001", "72" }, texts);
            Assert.Equal("url", comps[1].GetProperty("sub_type").GetString());
            Assert.Equal("Qm9X3kP0aT7vL2nR8sY4cE6wH1dJ5uZfG9bV-qA_MtI", comps[1].GetProperty("parameters")[0].GetProperty("text").GetString());
        }

        [Fact]
        public async Task A_school_whose_own_sender_is_active_is_sent_from_its_own_phone_number_id_and_a_template_name_can_be_overridden()
        {
            var handler = StubHandler.Json(HttpStatusCode.OK, "{\"messages\":[{\"id\":\"wamid.S\"}]}");
            var o = Opts(); o.TemplateParentAccountReady = "parent_account_ready_v2";
            await Meta(handler, o).SendAsync(Msg("555666777"), default);
            Assert.Contains("/555666777/messages", handler.Calls[0].Request.RequestUri!.ToString());
            Assert.Contains("parent_account_ready_v2", handler.Calls[0].Body);
        }

        [Theory]
        [InlineData(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"(#131026) Message undeliverable to +919876543210 see https://x.example/y?t=Qm9X3kP0aT7vL2nR8sY4cE6wH1dJ5uZfG9bV\",\"code\":131026}}", "META_131026")]
        [InlineData(HttpStatusCode.Unauthorized, "{\"error\":{\"message\":\"Invalid OAuth access token EAAG-ACCESS-TOKEN-SHOULD-NEVER-APPEAR-IN-ANY-LOG-OR-ROW\",\"code\":190}}", "AUTH")]
        [InlineData(HttpStatusCode.TooManyRequests, "{\"error\":{\"code\":130429}}", "RATE_LIMIT")]
        [InlineData(HttpStatusCode.BadGateway, "<html>bad gateway</html>", "HTTP_502")]
        public async Task A_refusal_comes_back_as_a_short_code_and_text_never_Metas_own_answer_and_nothing_secret_is_logged(HttpStatusCode status, string body, string code)
        {
            var log = new CapLog<MetaCloudWhatsAppSender>();
            var r = await Meta(StubHandler.Json(status, body), Opts(), log).SendAsync(Msg(), default);
            Assert.False(r.Success); Assert.Equal(code, r.ErrorCode);
            var shown = r.ErrorMessage + "\n" + log.All;
            foreach (var secret in new[] { AccessToken, "9876543210", "https://", "Qm9X3kP0aT7vL2nR8sY4cE6wH1dJ5uZfG9bV", "undeliverable", "Invalid OAuth" }) Assert.DoesNotContain(secret, shown);
            Assert.True(DeliveryText.Sanitise(r.ErrorMessage) == r.ErrorMessage);   // already safe to store
        }

        [Fact]
        public async Task A_timeout_a_dead_network_and_a_missing_setting_are_results_not_exceptions_and_a_missing_setting_makes_no_call()
        {
            var o = Opts(); o.TimeoutSeconds = 1;
            var slow = new StubHandler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); });
            var t = await Meta(slow, o).SendAsync(Msg(), default);
            Assert.Equal("TIMEOUT", t.ErrorCode); Assert.False(t.Success);

            var log = new CapLog<MetaCloudWhatsAppSender>();
            var dead = new StubHandler((_, _) => throw new HttpRequestException("could not connect to https://graph.facebook.com with " + AccessToken));
            var n = await Meta(dead, Opts(), log).SendAsync(Msg(), default);
            Assert.Equal("NETWORK", n.ErrorCode);
            Assert.DoesNotContain(AccessToken, log.All + n.ErrorMessage);

            var none = StubHandler.Json(HttpStatusCode.OK, "{}");
            Assert.Equal("NOT_CONFIGURED", (await Meta(none, Opts(token: "")).SendAsync(Msg(), default)).ErrorCode);
            Assert.Equal("NOT_CONFIGURED", (await Meta(none, Opts()).SendAsync(Msg("12/../34"), default)).ErrorCode);   // a phone number id that is not digits is refused (no URL tricks)
            Assert.Equal("NOT_CONFIGURED", (await Meta(none, new WhatsAppOptions { Enabled = true, AccessToken = "t", PhoneNumberId = "1234567", ApiVersion = "../x" }).SendAsync(Msg(), default)).ErrorCode);
            Assert.Empty(none.Calls);
            Assert.Equal("NO_MESSAGE_ID", (await Meta(StubHandler.Json(HttpStatusCode.OK, "{}"), Opts()).SendAsync(Msg(), default)).ErrorCode);
        }

        [Fact]
        public async Task A_caller_who_cancels_gets_the_cancellation_not_a_timeout_result()
        {
            using var cts = new CancellationTokenSource();
            var slow = new StubHandler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); });
            var task = Meta(slow, Opts()).SendAsync(Msg(), cts.Token);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        }

        // ------------------------------------------------------------------------------------------------ templates and their documentation
        [Fact]
        public void The_templates_document_and_the_code_agree_on_the_names_and_the_number_of_variables()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Docs", "whatsapp-templates.md"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var doc = File.ReadAllText(Path.Combine(dir!.FullName, "Docs", "whatsapp-templates.md"));
            foreach (var code in ParentTemplateCodes.All)
            {
                var start = doc.IndexOf("## " + (Array.IndexOf(ParentTemplateCodes.All, code) + 1) + ". `" + code + "`", StringComparison.Ordinal);
                Assert.True(start >= 0, code + " is not documented");
                var next = doc.IndexOf("\n## ", start + 5, StringComparison.Ordinal);
                var section = doc.Substring(start, next < 0 ? doc.Length - start : next - start);
                var bodyBlock = Regex.Match(section, "\\*\\*Body:\\*\\*\\s*```\\s*(.*?)\\s*```", RegexOptions.Singleline).Groups[1].Value;
                var max = Regex.Matches(bodyBlock, @"\{\{(\d+)\}\}").Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(0).Max();
                Assert.Equal(ParentTemplates.BodyVariableCount(code), max);
                Assert.DoesNotMatch(@"^\s*\{\{|\}\}\s*[.!?]?\s*$", bodyBlock);           // a variable never starts or ends the body
                Assert.DoesNotContain("}}{{", bodyBlock);
                Assert.True(bodyBlock.Length <= 1024);
                Assert.Contains("Category:** Utility", section);
                Assert.DoesNotContain("password is", bodyBlock, StringComparison.OrdinalIgnoreCase);
            }
            // the variable builders fill exactly that many variables
            var built = new[]
            {
                ParentTemplates.AccountReady("g", "c", "s", "id", 72, "t"), ParentTemplates.ChildAdded("g", "c", "s", "id"),
                ParentTemplates.LoginHelp("g", "s", "id", 60, "t"), ParentTemplates.StudentLoginReady("g", "st", "s", "id", 72, "t"),
            };
            foreach (var c in built) Assert.Equal(ParentTemplates.BodyVariableCount(c.TemplateCode), c.BodyParameters.Count);
            Assert.Null(built[1].ButtonParameter);                                        // the child-added button is a fixed link
            Assert.All(new[] { built[0], built[2], built[3] }, c => Assert.Equal("t", c.ButtonParameter));
        }

        [Fact]
        public void No_template_variable_or_email_carries_a_password()
        {
            var html = EmailTemplates.ParentAccountReady("Asha", "Ravi", "School", "GVS001GD001", "https://s.example/parent/set-password?t=abc", 72).Html;
            Assert.Contains("GVS001GD001", html); Assert.Contains("set-password?t=abc", html);
            Assert.DoesNotContain("password:", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Temporary", html);
            var evil = EmailTemplates.ParentChildAdded("<script>x</script>", "A&B", "S", "id", null).Html;
            Assert.DoesNotContain("<script>", evil);
        }

        // ------------------------------------------------------------------------------------------------ the webhook (signature, switch, status)
        private static string Sign(byte[] body, string secret = AppSecret) => "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant();
        private static byte[] StatusBody(string id, string status, int? error = null) => Encoding.UTF8.GetBytes(
            "{\"entry\":[{\"changes\":[{\"value\":{\"statuses\":[{\"id\":\"" + id + "\",\"status\":\"" + status + "\"" + (error == null ? "" : ",\"errors\":[{\"code\":" + error + ",\"title\":\"secret title\"}]") + "}]}}]}]}");

        [Fact]
        public async Task The_webhook_is_off_without_the_switch_and_the_app_secret_and_every_post_needs_a_valid_signature()
        {
            var body = StatusBody("wamid.1", "delivered");
            Assert.Equal(404, await new WhatsAppWebhookService(Options.Create(Opts(enabled: false)), Fake.Of<IMessagingRepository>(), NullLogger<WhatsAppWebhookService>.Instance).HandleAsync(body, Sign(body), default));
            var noSecret = Opts(); noSecret.AppSecret = "";
            Assert.Equal(404, await new WhatsAppWebhookService(Options.Create(noSecret), Fake.Of<IMessagingRepository>(), NullLogger<WhatsAppWebhookService>.Instance).HandleAsync(body, Sign(body), default));
            Assert.Equal(404, new WhatsAppWebhookService(Options.Create(noSecret), Fake.Of<IMessagingRepository>(), NullLogger<WhatsAppWebhookService>.Instance).Verify("subscribe", "verify-me", "c").StatusCode);

            var repo = Fake.Of<IMessagingRepository>();
            var svc = new WhatsAppWebhookService(Options.Create(Opts()), repo, NullLogger<WhatsAppWebhookService>.Instance);
            Assert.Equal(401, await svc.HandleAsync(body, null, default));
            Assert.Equal(401, await svc.HandleAsync(body, "", default));
            Assert.Equal(401, await svc.HandleAsync(body, "sha256=" + new string('0', 64), default));
            Assert.Equal(401, await svc.HandleAsync(body, Sign(body, "another-secret"), default));
            Assert.Equal(401, await svc.HandleAsync(body, Sign(body).Replace("sha256=", "sha1="), default));
            Assert.Equal(401, await svc.HandleAsync(Encoding.UTF8.GetBytes("{}"), Sign(body), default));                       // a signature of a different body
            Assert.DoesNotContain("ApplyProviderStatusAsync", Fake.ProxyOf(repo).Calls);                                       // nothing was touched
        }

        [Fact]
        public void The_subscription_check_answers_the_challenge_only_for_the_right_token()
        {
            var svc = new WhatsAppWebhookService(Options.Create(Opts()), Fake.Of<IMessagingRepository>(), NullLogger<WhatsAppWebhookService>.Instance);
            var ok = svc.Verify("subscribe", "verify-me", "12345");
            Assert.Equal(200, ok.StatusCode); Assert.Equal("12345", ok.Challenge);
            Assert.Equal(403, svc.Verify("subscribe", "wrong", "12345").StatusCode);
            Assert.Equal(403, svc.Verify("unsubscribe", "verify-me", "12345").StatusCode);
            Assert.Equal(403, svc.Verify("subscribe", null, "12345").StatusCode);
            var noVerify = Opts(); noVerify.WebhookVerifyToken = "";
            Assert.Equal(404, new WhatsAppWebhookService(Options.Create(noVerify), Fake.Of<IMessagingRepository>(), NullLogger<WhatsAppWebhookService>.Instance).Verify("subscribe", "", "x").StatusCode);
        }

        // ------------------------------------------------------------------------------------------------ database: links
        private static IMessagingRepository Repo(TaxDb db) => new MessagingRepository(db.Ctx());
        private static UserActionTokenService Tokens(TaxDb db) => new(Repo(db), Options.Create(new ParentAccountsOptions { WebBaseUrl = "https://site.example" }));
        private static long NewGuardianUser(TaxDb db, string id, string school = A) =>
            db.Scalar<long>($"insert into tb_users (school_id, user_type, org_user_id, display_name) values ('{school}', 'Guardian', '{id}', 'Parent') returning user_id");

        [DbFact]
        public async Task A_link_works_once_and_a_second_use_with_the_same_token_is_refused()
        {
            using var db = new TaxDb();
            var u = NewGuardianUser(db, "GHS001GD001");
            var link = await Tokens(db).IssueAsync(u, A, ActionTokenPurposes.SetPassword, UserA, default);
            Assert.True(await Tokens(db).IsUsableAsync(link.Token, ActionTokenPurposes.SetPassword, default));   // checking does not use it
            Assert.True(await Tokens(db).IsUsableAsync(link.Token, ActionTokenPurposes.SetPassword, default));
            var first = await Tokens(db).ConsumeAsync(link.Token, ActionTokenPurposes.SetPassword, default);
            Assert.Equal(u, first!.UserId); Assert.Equal(A, first.SchoolId);
            Assert.Null(await Tokens(db).ConsumeAsync(link.Token, ActionTokenPurposes.SetPassword, default));     // single use
            Assert.False(await Tokens(db).IsUsableAsync(link.Token, ActionTokenPurposes.SetPassword, default));
            Assert.Equal(1, db.Scalar<int>("select count(*) from tb_user_action_tokens where used_at is not null"));
        }

        [DbFact]
        public async Task Two_requests_with_the_same_token_at_once_let_exactly_one_through()
        {
            using var db = new TaxDb();
            var u = NewGuardianUser(db, "GHS001GD001");
            var link = await Tokens(db).IssueAsync(u, A, ActionTokenPurposes.SetPassword, UserA, default);
            var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => Tokens(db).ConsumeAsync(link.Token, ActionTokenPurposes.SetPassword, default))));
            Assert.Equal(1, results.Count(r => r != null));
        }

        [DbFact]
        public async Task A_link_is_for_its_purpose_only_and_an_unknown_or_malformed_token_is_refused_the_same_way()
        {
            using var db = new TaxDb();
            var u = NewGuardianUser(db, "GHS001GD001");
            var link = await Tokens(db).IssueAsync(u, A, ActionTokenPurposes.SetPassword, UserA, default);
            Assert.Null(await Tokens(db).ConsumeAsync(link.Token, ActionTokenPurposes.ResetPassword, default));       // a set-password link cannot reset
            Assert.Null(await Tokens(db).ConsumeAsync(UserActionTokens.NewToken(), ActionTokenPurposes.SetPassword, default));
            Assert.Null(await Tokens(db).ConsumeAsync("not a token", ActionTokenPurposes.SetPassword, default));
            Assert.Null(await Tokens(db).ConsumeAsync(null, ActionTokenPurposes.SetPassword, default));
            Assert.Null(await Tokens(db).ConsumeAsync(link.Token, "SOMETHING_ELSE", default));
            Assert.NotNull(await Tokens(db).ConsumeAsync(link.Token, ActionTokenPurposes.SetPassword, default));      // none of the refusals used it up
        }

        [DbFact]
        public async Task An_expired_link_is_refused_set_password_lasts_72_hours_and_reset_one_hour()
        {
            using var db = new TaxDb();
            var u = NewGuardianUser(db, "GHS001GD001");
            var set = await Tokens(db).IssueAsync(u, A, ActionTokenPurposes.SetPassword, UserA, default);
            var reset = await Tokens(db).IssueAsync(u, A, ActionTokenPurposes.ResetPassword, null, default);
            Assert.Equal(72 * 3600, db.Scalar<int>("select extract(epoch from expires_at - created_at)::int from tb_user_action_tokens where purpose='SET_PASSWORD'"));
            Assert.Equal(3600, db.Scalar<int>("select extract(epoch from expires_at - created_at)::int from tb_user_action_tokens where purpose='RESET_PASSWORD'"));
            Assert.Equal(1, db.Scalar<int>("select count(*) from tb_user_action_tokens where purpose='RESET_PASSWORD' and created_by is null"));   // the parent asked: nobody else did

            db.Exec("update tb_user_action_tokens set created_at = now() - interval '5 hours', expires_at = now() - interval '1 second' where purpose='RESET_PASSWORD'");
            Assert.False(await Tokens(db).IsUsableAsync(reset.Token, ActionTokenPurposes.ResetPassword, default));
            Assert.Null(await Tokens(db).ConsumeAsync(reset.Token, ActionTokenPurposes.ResetPassword, default));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_user_action_tokens where purpose='RESET_PASSWORD' and used_at is not null"));   // an expired link is not marked used
            Assert.NotNull(await Tokens(db).ConsumeAsync(set.Token, ActionTokenPurposes.SetPassword, default));
        }

        [DbFact]
        public async Task A_newer_link_cancels_the_older_unused_ones_of_that_purpose_only_for_that_user()
        {
            using var db = new TaxDb();
            var u1 = NewGuardianUser(db, "GHS001GD001"); var u2 = NewGuardianUser(db, "GHS001GD002");
            var old = await Tokens(db).IssueAsync(u1, A, ActionTokenPurposes.SetPassword, UserA, default);
            var other = await Tokens(db).IssueAsync(u2, A, ActionTokenPurposes.SetPassword, UserA, default);
            var reset = await Tokens(db).IssueAsync(u1, A, ActionTokenPurposes.ResetPassword, null, default);
            var fresh = await Tokens(db).IssueAsync(u1, A, ActionTokenPurposes.SetPassword, UserA, default);
            Assert.Null(await Tokens(db).ConsumeAsync(old.Token, ActionTokenPurposes.SetPassword, default));          // replaced
            Assert.NotNull(await Tokens(db).ConsumeAsync(fresh.Token, ActionTokenPurposes.SetPassword, default));
            Assert.NotNull(await Tokens(db).ConsumeAsync(other.Token, ActionTokenPurposes.SetPassword, default));     // another user's link is untouched
            Assert.NotNull(await Tokens(db).ConsumeAsync(reset.Token, ActionTokenPurposes.ResetPassword, default));   // another purpose is untouched
            Assert.Equal(1, db.Scalar<int>("select count(*) from tb_user_action_tokens where revoked_at is not null"));
        }

        [DbFact]
        public async Task Revoking_a_users_links_kills_the_unused_ones_and_leaves_the_used_one_alone()
        {
            using var db = new TaxDb();
            var u = NewGuardianUser(db, "GHS001GD001");
            var used = await Tokens(db).IssueAsync(u, A, ActionTokenPurposes.SetPassword, UserA, default);
            await Tokens(db).ConsumeAsync(used.Token, ActionTokenPurposes.SetPassword, default);
            var live = await Tokens(db).IssueAsync(u, A, ActionTokenPurposes.ResetPassword, null, default);
            Assert.Equal(1, await Repo(db).RevokeTokensAsync(u, null, default));
            Assert.Null(await Tokens(db).ConsumeAsync(live.Token, ActionTokenPurposes.ResetPassword, default));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_user_action_tokens where used_at is not null and revoked_at is not null"));
        }

        [DbFact]
        public async Task Only_the_hash_is_stored_never_the_token_and_the_table_has_no_column_that_could_hold_one()
        {
            using var db = new TaxDb();
            var u = NewGuardianUser(db, "GHS001GD001");
            var link = await Tokens(db).IssueAsync(u, A, ActionTokenPurposes.SetPassword, UserA, default);
            Assert.Equal(UserActionTokens.Hash(link.Token), db.Scalar<string>("select token_hash from tb_user_action_tokens").Trim());
            Assert.DoesNotContain(link.Token, db.Scalar<string>("select row_to_json(t)::text from tb_user_action_tokens t"));
            Assert.Equal(0, db.Scalar<int>("select count(*) from information_schema.columns where table_name = 'tb_user_action_tokens' and column_name in ('token', 'raw_token', 'link', 'url', 'password')"));
            Assert.ThrowsAny<Exception>(() => db.Exec("update tb_user_action_tokens set token_hash = 'not-a-sha256-hex'"));                      // the check refuses a non-hash
            Assert.ThrowsAny<Exception>(() => db.Exec("update tb_user_action_tokens set used_at = now(), revoked_at = now()"));                 // used and revoked at once
            Assert.ThrowsAny<Exception>(() => db.Exec("update tb_user_action_tokens set purpose = 'ANYTHING'"));
        }

        // ------------------------------------------------------------------------------------------------ database: guardian login ids
        private static GuardianLoginIdService Ids(TaxDb db) => new(Repo(db));

        [DbFact]
        public async Task Login_ids_count_up_per_school_GHS001GD001_GHS001GD002_and_another_school_has_its_own_numbers()
        {
            using var db = new TaxDb();
            Assert.Equal("GHS001GD001", await Ids(db).AllocateAsync(A, default));
            Assert.Equal("GHS001GD002", await Ids(db).AllocateAsync(A, default));
            Assert.Equal("APS001GD001", await Ids(db).AllocateAsync(B, default));
            Assert.Equal("GHS001GD003", await Ids(db).AllocateAsync(A, default));
        }

        [DbFact]
        public async Task Twenty_requests_at_once_get_twenty_different_ids()
        {
            using var db = new TaxDb();
            var ids = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => Ids(db).AllocateAsync(A, default))));
            Assert.Equal(20, ids.Select(i => i!.ToLowerInvariant()).Distinct().Count());
            Assert.All(ids, i => Assert.True(GuardianLoginIds.LooksLikeGuardianId(i)));
            Assert.Equal(20, db.Scalar<int>($"select last_number from tb_guardian_login_counters where school_id='{A}'"));
        }

        [DbFact]
        public async Task An_id_that_already_exists_in_any_case_as_a_login_or_a_student_id_is_skipped()
        {
            using var db = new TaxDb();
            NewGuardianUser(db, "ghs001gd001");                                                                        // lower case: the same id
            db.Exec($"insert into tb_users (school_id, user_type, org_user_id) values ('{B}', 'Employee', 'GHS001GD002')");   // any user type blocks it, in any school
            var id = await Ids(db).AllocateAsync(A, default);
            Assert.Equal("GHS001GD003", id);
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_students where lower(student_id) = lower('" + id + "')"));
        }

        [DbFact]
        public async Task A_school_without_a_short_code_gets_no_id_and_two_guardian_logins_cannot_share_an_id_in_any_case()
        {
            using var db = new TaxDb();
            db.Exec($"update tb_schools set school_short_code = null where school_id = '{A}'");
            Assert.Null(await Ids(db).AllocateAsync(A, default));
            NewGuardianUser(db, "GHS001GD009");
            Assert.ThrowsAny<Exception>(() => NewGuardianUser(db, "ghs001gd009", B));                                  // unique index, platform wide, case-insensitive
        }

        // ------------------------------------------------------------------------------------------------ database: the delivery log and the dispatcher
        private static ParentMessageDispatcher Dispatcher(TaxDb db, IWhatsAppSender sender, INotificationService mail, ILogger<ParentMessageDispatcher>? log = null, IMessagingRepository? repo = null) =>
            new(repo ?? Repo(db), new SchoolWhatsAppSenderRepository(db.Ctx()), sender, mail, log ?? NullLogger<ParentMessageDispatcher>.Instance);

        private static ParentMessage Message(string? number = Number, string? email = "asha@gmail.com", string school = A, long guardian = 191, string token = "Qm9X3kP0aT7vL2nR8sY4cE6wH1dJ5uZfG9bV-qA_MtI") =>
            new(school, guardian, null, ParentTemplateCodes.AccountReady, MessagePurposes.SetPassword, number,
                ParentTemplates.AccountReady("Asha Rao", "Ravi Rao", "Green Valley School", "GVS001GD001", 72, token), email,
                EmailTemplates.ParentAccountReady("Asha Rao", "Ravi Rao", "Green Valley School", "GVS001GD001", "https://site.example/parent/set-password?t=" + token, 72), UserA);

        /// <summary>WhatsApp content only: the email channel is not asked for.</summary>
        private static ParentMessage WaOnly() => Message(email: null) with { Email = null };

        [DbFact]
        public async Task With_the_default_log_only_sender_a_message_is_logged_as_LOGGED_with_the_recipient_masked_and_no_token_link_or_text()
        {
            using var db = new TaxDb();
            var token = UserActionTokens.NewToken();
            var outcomes = await Dispatcher(db, new LogOnlyWhatsAppSender(NullLogger<LogOnlyWhatsAppSender>.Instance), new FakeMail()).SendAsync(Message(token: token), default);
            Assert.Equal(new[] { "WHATSAPP:LOGGED", "EMAIL:SENT" }, outcomes.Select(o => o.Channel + ":" + o.Status));
            var rows = db.Scalar<string>("select string_agg(row_to_json(d)::text, ' ') from tb_message_deliveries d");
            Assert.Contains("+91******3210", rows); Assert.Contains("a***@gmail.com", rows);
            foreach (var leak in new[] { token, "9876543210", "asha@gmail.com", "https://", "set-password", "Asha Rao", "Ravi Rao", "GVS001GD001", "Green Valley" }) Assert.DoesNotContain(leak, rows);
            Assert.Equal("LOG", db.Scalar<string>("select sender_kind from tb_message_deliveries where channel='WHATSAPP'"));
            Assert.Equal("PLATFORM", db.Scalar<string>("select sender_scope from tb_message_deliveries where channel='WHATSAPP'"));
            Assert.Equal("SMTP", db.Scalar<string>("select sender_kind from tb_message_deliveries where channel='EMAIL'"));
            Assert.Equal(1, db.Scalar<int>("select count(*) from tb_message_deliveries where created_by = " + UserA + " and school_id = '" + A + "' and guardian_id = 191 and purpose = 'SET_PASSWORD' and template_code = 'parent_account_ready'" + " and channel = 'WHATSAPP'"));
        }

        [DbFact]
        public async Task The_log_table_has_no_column_for_a_token_link_password_or_message_text_and_a_failure_must_say_why()
        {
            using var db = new TaxDb();
            Assert.Equal(0, db.Scalar<int>("select count(*) from information_schema.columns where table_name = 'tb_message_deliveries' and (column_name ~ '(token|link|url|password|secret|body|text|content|payload|param)')"));
            await Dispatcher(db, new FakeSender(), new FakeMail()).SendAsync(Message(), default);
            Assert.ThrowsAny<Exception>(() => db.Exec("update tb_message_deliveries set status = 'FAILED', error_code = null where channel = 'EMAIL'"));
            Assert.ThrowsAny<Exception>(() => db.Exec("update tb_message_deliveries set channel = 'SMS'"));
            Assert.ThrowsAny<Exception>(() => db.Exec("update tb_message_deliveries set status = 'DONE'"));
            Assert.ThrowsAny<Exception>(() => db.Exec("update tb_message_deliveries set guardian_id = null, user_id = null"));
        }

        [DbFact]
        public async Task A_missing_number_or_address_is_SKIPPED_with_a_reason_and_a_failing_email_is_FAILED()
        {
            using var db = new TaxDb();
            var mail = new FakeMail { Succeeds = false };
            var none = await Dispatcher(db, new FakeSender(), mail).SendAsync(Message(number: null, email: null), default);
            Assert.Equal(new[] { "WHATSAPP:SKIPPED:NO_NUMBER", "EMAIL:SKIPPED:NO_EMAIL" }, none.Select(o => $"{o.Channel}:{o.Status}:{o.ErrorCode}"));
            Assert.Empty(mail.To);                                                                                      // nothing was attempted
            var failed = await Dispatcher(db, new FakeSender(), mail).SendAsync(Message(number: "  ", email: "asha@gmail.com"), default);
            Assert.Equal("EMAIL:FAILED:EMAIL_FAILED", failed.Select(o => $"{o.Channel}:{o.Status}:{o.ErrorCode}").Last());
            Assert.Single(mail.To);
        }

        [DbFact]
        public async Task A_channel_with_no_content_is_not_logged_at_all()
        {
            using var db = new TaxDb();
            var onlyMail = Message() with { WhatsApp = null };
            var outcomes = await Dispatcher(db, new FakeSender(), new FakeMail()).SendAsync(onlyMail, default);
            Assert.Equal(new[] { "EMAIL" }, outcomes.Select(o => o.Channel));
            Assert.Equal(1, db.Scalar<int>("select count(*) from tb_message_deliveries"));
        }

        [DbFact]
        public async Task Until_the_school_sender_is_ACTIVE_the_platform_number_sends_then_the_schools_own_phone_number_id()
        {
            using var db = new TaxDb();
            var sender = new FakeSender();
            await Dispatcher(db, sender, new FakeMail()).SendAsync(WaOnly(), default);                      // NOT_REGISTERED
            db.Exec($"update tb_school_whatsapp_senders set status = 'PENDING', whatsapp_number = '+919000000001' where school_id = '{A}'");
            await Dispatcher(db, sender, new FakeMail()).SendAsync(WaOnly(), default);                      // PENDING
            db.Exec($"update tb_school_whatsapp_senders set status = 'ACTIVE', phone_number_id = '777888999', verified_at = now() where school_id = '{A}'");
            await Dispatcher(db, sender, new FakeMail()).SendAsync(WaOnly(), default);                      // ACTIVE
            await Dispatcher(db, sender, new FakeMail()).SendAsync(Message(email: null, school: B, guardian: db.Scalar<long>($"select guardian_id from tb_guardians where school_id='{B}' limit 1")) with { Email = null }, default);   // another school: still the platform
            Assert.Equal(new string?[] { null, null, "777888999", null }, sender.Sent.Select(m => m.SenderPhoneNumberId));
            Assert.Equal("PLATFORM,PLATFORM,SCHOOL,PLATFORM", db.Scalar<string>("select string_agg(sender_scope, ',' order by delivery_id) from tb_message_deliveries"));
        }

        [DbFact]
        public async Task A_send_that_fails_or_throws_is_a_FAILED_row_with_a_safe_short_error_and_never_an_exception()
        {
            using var db = new TaxDb();
            var log = new CapLog<ParentMessageDispatcher>();
            var failing = new FakeSender { Reply = _ => new WhatsAppSendResult(false, "CLOUD", null, "META_131026", $"undeliverable to +919876543210 https://x.example/?t=Qm9X3kP0aT7vL2nR8sY4cE6wH1dJ5uZfG9bV {AccessToken}") };
            var o1 = await Dispatcher(db, failing, new FakeMail(), log).SendAsync(WaOnly(), default);
            Assert.Equal("FAILED", o1.Single().Status);
            var throwing = new FakeSender { Throw = true };
            var o2 = await Dispatcher(db, throwing, new FakeMail(), log).SendAsync(WaOnly(), default);
            Assert.Equal("UNEXPECTED", o2.Single().ErrorCode);
            var text = db.Scalar<string>("select string_agg(row_to_json(d)::text, ' ') from tb_message_deliveries d") + "\n" + log.All;
            foreach (var leak in new[] { AccessToken, "9876543210", "https://", "Qm9X3kP0aT7vL2nR8sY4cE6wH1dJ5uZfG9bV", "boom" }) Assert.DoesNotContain(leak, text);
            Assert.Equal("CLOUD", db.Scalar<string>("select sender_kind from tb_message_deliveries order by delivery_id limit 1"));
        }

        [DbFact]
        public async Task A_log_row_that_cannot_be_written_never_fails_the_caller()
        {
            using var db = new TaxDb();
            var log = new CapLog<ParentMessageDispatcher>();
            var broken = Fake.Of<IMessagingRepository>(new() { ["AddDeliveryAsync"] = new InvalidOperationException("db down: " + AccessToken) });
            var outcomes = await Dispatcher(db, new FakeSender(), new FakeMail(), log, broken).SendAsync(Message(), default);
            Assert.Equal(2, outcomes.Count);
            Assert.All(outcomes, o => Assert.Null(o.DeliveryId));
            Assert.Contains("could not be written", log.All);
            Assert.DoesNotContain("9876543210", log.All);
            Assert.DoesNotContain(AccessToken, log.All);
        }

        [DbFact]
        public async Task The_resend_limit_can_count_a_guardians_deliveries_per_purpose_in_a_window_and_a_resend_points_at_the_first()
        {
            using var db = new TaxDb();
            var d = Dispatcher(db, new FakeSender(), new FakeMail());
            var first = (await d.SendAsync(WaOnly(), default)).Single();
            await d.SendAsync(WaOnly() with { ResendOfDeliveryId = first.DeliveryId }, default);
            await d.SendAsync(WaOnly() with { Purpose = MessagePurposes.ResetPassword, TemplateCode = ParentTemplateCodes.LoginHelp }, default);
            var since = DateTime.UtcNow.AddHours(-24);
            Assert.Equal(2, await Repo(db).CountDeliveriesSinceAsync(A, 191, MessagePurposes.SetPassword, since, default));
            Assert.Equal(1, await Repo(db).CountDeliveriesSinceAsync(A, 191, MessagePurposes.ResetPassword, since, default));
            Assert.Equal(0, await Repo(db).CountDeliveriesSinceAsync(A, 192, MessagePurposes.SetPassword, since, default));      // another guardian
            Assert.Equal(0, await Repo(db).CountDeliveriesSinceAsync(B, 191, MessagePurposes.SetPassword, since, default));      // another school
            Assert.Equal(0, await Repo(db).CountDeliveriesSinceAsync(A, 191, MessagePurposes.SetPassword, DateTime.UtcNow.AddMinutes(1), default));
            Assert.Equal(first.DeliveryId, db.Scalar<long?>("select resend_of_delivery_id from tb_message_deliveries where resend_of_delivery_id is not null"));
            Assert.Null(await Repo(db).GetDeliveryAsync(B, first.DeliveryId!.Value, default));                                    // another school cannot read it
            Assert.NotNull(await Repo(db).GetDeliveryAsync(A, first.DeliveryId!.Value, default));
        }

        // ------------------------------------------------------------------------------------------------ database: provider status and the webhook end to end
        private static async Task<long> SentRow(TaxDb db, string providerId)
        {
            var sender = new FakeSender { Reply = _ => new WhatsAppSendResult(true, "CLOUD", providerId, null, null) };
            return (await Dispatcher(db, sender, new FakeMail()).SendAsync(WaOnly(), default)).Single().DeliveryId!.Value;
        }

        [DbFact]
        public async Task A_provider_status_only_moves_a_delivery_forward_and_an_unknown_id_is_ignored()
        {
            using var db = new TaxDb();
            await SentRow(db, "wamid.1");
            var r = Repo(db);
            Assert.False(await r.ApplyProviderStatusAsync("wamid.unknown", "DELIVERED", null, null, default));
            Assert.True(await r.ApplyProviderStatusAsync("wamid.1", "DELIVERED", null, null, default));
            Assert.Equal("DELIVERED", db.Scalar<string>("select status from tb_message_deliveries"));
            Assert.NotNull(db.Scalar<DateTime?>("select delivered_at from tb_message_deliveries"));
            await r.ApplyProviderStatusAsync("wamid.1", "SENT", null, null, default);                                   // late, out of order
            Assert.Equal("DELIVERED", db.Scalar<string>("select status from tb_message_deliveries"));
            await r.ApplyProviderStatusAsync("wamid.1", "FAILED", "META_1", "x", default);                              // a delivered message cannot fail afterwards
            Assert.Equal("DELIVERED", db.Scalar<string>("select status from tb_message_deliveries"));
            await r.ApplyProviderStatusAsync("wamid.1", "READ", null, null, default);
            Assert.Equal("READ", db.Scalar<string>("select status from tb_message_deliveries"));
            await r.ApplyProviderStatusAsync("wamid.1", "DELIVERED", null, null, default);
            Assert.Equal("READ", db.Scalar<string>("select status from tb_message_deliveries"));
        }

        [DbFact]
        public async Task A_signed_webhook_call_updates_the_delivery_and_a_failed_status_keeps_only_a_fixed_text_never_Metas_wording()
        {
            using var db = new TaxDb();
            await SentRow(db, "wamid.OK"); await SentRow(db, "wamid.BAD");
            var svc = new WhatsAppWebhookService(Options.Create(Opts()), Repo(db), NullLogger<WhatsAppWebhookService>.Instance);
            var ok = StatusBody("wamid.OK", "read");
            Assert.Equal(200, await svc.HandleAsync(ok, Sign(ok), default));
            var bad = StatusBody("wamid.BAD", "failed", 131026);
            Assert.Equal(200, await svc.HandleAsync(bad, Sign(bad), default));
            Assert.Equal("READ", db.Scalar<string>("select status from tb_message_deliveries where provider_message_id = 'wamid.OK'"));
            Assert.Equal("FAILED", db.Scalar<string>("select status from tb_message_deliveries where provider_message_id = 'wamid.BAD'"));
            Assert.Equal("META_131026", db.Scalar<string>("select error_code from tb_message_deliveries where provider_message_id = 'wamid.BAD'"));
            Assert.DoesNotContain("secret title", db.Scalar<string>("select error_message from tb_message_deliveries where provider_message_id = 'wamid.BAD'"));
            var junk = Encoding.UTF8.GetBytes("not json at all");
            Assert.Equal(200, await svc.HandleAsync(junk, Sign(junk), default));                                         // a signed but malformed body is ignored quietly
        }

        [DbFact]
        public async Task The_webhook_controller_answers_Metas_raw_contract_404_when_off_401_unsigned_200_signed_and_the_challenge_text()
        {
            using var db = new TaxDb();
            await SentRow(db, "wamid.C");
            var on = new WhatsAppWebhookController(new WhatsAppWebhookService(Options.Create(Opts()), Repo(db), NullLogger<WhatsAppWebhookService>.Instance));
            var off = new WhatsAppWebhookController(new WhatsAppWebhookService(Options.Create(Opts(enabled: false)), Repo(db), NullLogger<WhatsAppWebhookService>.Instance));
            var body = StatusBody("wamid.C", "delivered");

            static T With<T>(T c, byte[] bytes, string? signature) where T : ControllerBase
            {
                var ctx = new DefaultHttpContext();
                ctx.Request.Body = new MemoryStream(bytes); ctx.Request.ContentLength = bytes.Length;
                if (signature != null) ctx.Request.Headers["X-Hub-Signature-256"] = signature;
                c.ControllerContext = new ControllerContext { HttpContext = ctx };
                return c;
            }
            Assert.Equal(404, ((StatusCodeResult)await With(off, body, Sign(body)).Receive(default)).StatusCode);
            Assert.Equal(401, ((StatusCodeResult)await With(on, body, null).Receive(default)).StatusCode);
            Assert.Equal(200, ((StatusCodeResult)await With(on, body, Sign(body)).Receive(default)).StatusCode);
            Assert.Equal("DELIVERED", db.Scalar<string>("select status from tb_message_deliveries"));
            var big = new byte[300 * 1024];
            Assert.Equal(413, ((StatusCodeResult)await With(on, big, Sign(big)).Receive(default)).StatusCode);

            var challenge = Assert.IsType<ContentResult>(on.Verify("subscribe", "verify-me", "98765"));
            Assert.Equal("98765", challenge.Content); Assert.Equal("text/plain", challenge.ContentType);
            Assert.Equal(403, ((StatusCodeResult)on.Verify("subscribe", "nope", "98765")).StatusCode);
            Assert.Equal(404, ((StatusCodeResult)off.Verify("subscribe", "verify-me", "98765")).StatusCode);
            var attrs = typeof(WhatsAppWebhookController).GetCustomAttributes(true).Select(a => a.GetType().Name);
            Assert.Contains("AllowAnonymousAttribute", attrs);
        }

        // ------------------------------------------------------------------------------------------------ database: consent and nothing changed for existing rows
        [DbFact]
        public void Consent_is_all_or_nothing_and_no_existing_guardian_or_admission_has_any()
        {
            using var db = new TaxDb();
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_guardians where consent_given_at is not null or consent_recorded_by is not null or consent_source is not null"));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_admissions where guardian_consent_at is not null or guardian_consent_recorded_by is not null"));
            Assert.ThrowsAny<Exception>(() => db.Exec("update tb_guardians set consent_given_at = now() where guardian_id = 191"));                                  // a date without who and how
            Assert.ThrowsAny<Exception>(() => db.Exec($"update tb_guardians set consent_given_at = now(), consent_recorded_by = {UserA}, consent_source = 'SHOUTED' where guardian_id = 191"));
            db.Exec($"update tb_guardians set consent_given_at = now(), consent_recorded_by = {UserA}, consent_source = 'BULK_CONFIRMED' where guardian_id = 191");
            Assert.Equal(1, db.Scalar<int>("select count(*) from tb_guardians where consent_source = 'BULK_CONFIRMED'"));
            Assert.ThrowsAny<Exception>(() => db.Exec("update tb_admissions set guardian_consent_at = now() where admission_id = (select min(admission_id) from tb_admissions)"));
            // the entity maps the columns
            using var ctx = db.Ctx();
            var g = ctx.Set<TbGuardian>().Single(x => x.GuardianId == 191);
            Assert.Equal("BULK_CONFIRMED", g.ConsentSource); Assert.Equal(UserA, g.ConsentRecordedBy); Assert.NotNull(g.ConsentGivenAt);
        }

        [DbFact]
        public void The_database_work_leaves_the_tables_it_does_not_own_exactly_as_they_were_in_the_template()
        {
            using var db = new TaxDb();
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_users where user_type = 'Guardian'"));            // no account was created
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_user_action_tokens"));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_message_deliveries"));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_guardian_login_counters"));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_guardians where user_id is not null"));
        }
    }
}
