using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using smss_api.Controllers;
using smss_api_db_layer.context;
using smss_api_db_layer.@interface;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Stage 6b (no migration): parent accounts, activation, recovery. Accounts are made at Confirm Admission and in bulk, only with consent on record and at least one channel; the parent chooses a password through a one-time
    /// link (no password is ever sent, shown or stored in a log); resend and admin reset are limited; the public endpoints give identical answers; a Guardian login signs in with its generated id and carries only the guardian id.
    /// No real message and no real Meta call: the WhatsApp sender and the mailer are fakes. [DbFact]: needs SMSS_TEST_PG and a template with the 6a migration (smss_tpl_6a).
    /// </summary>
    public class ParentAccounts6bTests
    {
        private const string A = TaxDb.SchoolA;
        private const string B = TaxDb.SchoolB;
        private const long UserA = TaxDb.UserA;

        // ------------------------------------------------------------------------------------------------ doubles
        private sealed class CapLog : ILoggerFactory
        {
            public readonly List<string> Lines = new();
            public string All => string.Join("\n", Lines);
            private sealed class L : ILogger
            {
                private readonly CapLog _o; public L(CapLog o) => _o = o;
                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
                public bool IsEnabled(LogLevel logLevel) => true;
                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => _o.Lines.Add(formatter(state, exception) + (exception == null ? "" : " | " + exception));
            }
            public ILogger CreateLogger(string categoryName) => new L(this);
            public ILogger<T> For<T>() => new Typed<T>(new L(this));
            private sealed class Typed<T> : ILogger<T>
            {
                private readonly ILogger _i; public Typed(ILogger i) => _i = i;
                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
                public bool IsEnabled(LogLevel logLevel) => true;
                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => _i.Log(logLevel, eventId, state, exception, formatter);
            }
            public void AddProvider(ILoggerProvider provider) { }
            public void Dispose() { }
        }

        private sealed class Sender : IWhatsAppSender
        {
            public readonly List<WhatsAppMessage> Sent = new();
            public bool Throw;
            public Task<WhatsAppSendResult> SendAsync(WhatsAppMessage message, CancellationToken ct)
            {
                Sent.Add(message);
                if (Throw) throw new InvalidOperationException("sender down");
                return Task.FromResult(new WhatsAppSendResult(true, "LOG", null, null, null));
            }
        }

        private sealed class Mail : INotificationService
        {
            public string? LoginUrl => "https://site.example/login";
            public bool Succeeds = true;
            public readonly List<(string To, string Subject, string Html)> Sent = new();
            public Task<string?> SendAsync(string? ownEmail, string? guardianEmail, (string Subject, string Html) message, string context, CancellationToken ct)
            {
                Sent.Add((ownEmail ?? "", message.Subject, message.Html));
                return Task.FromResult(Succeeds ? ownEmail : null);
            }
        }

        /// <summary>One test's collaborators. Every call to Svc() is a NEW request (a new DbContext), like production.</summary>
        private sealed class Env
        {
            public readonly TaxDb Db; public readonly Sender Wa = new(); public readonly Mail Email = new(); public readonly CapLog Log = new();
            public ParentAccountsOptions Opt = new() { WebBaseUrl = "https://site.example" };
            public readonly ContactThrottle Throttle = new();
            /// <summary>When set, "forgot" queues its sending like production; null = inline (the older tests).</summary>
            public IBackgroundTaskQueue? Queue;
            public Env(TaxDb db) => Db = db;

            public ParentMessageDispatcher Dispatcher(dbContext ctx) => new(new MessagingRepository(ctx), new SchoolWhatsAppSenderRepository(ctx), Wa, Email, Log.For<ParentMessageDispatcher>(), Options.Create(Opt));
            public UserActionTokenService Tokens(dbContext ctx) => new(new MessagingRepository(ctx), Options.Create(Opt));
            public ParentAccountService Svc()
            {
                var ctx = Db.Ctx(); var messaging = new MessagingRepository(ctx);
                return new ParentAccountService(new GuardianRepository(ctx), new ParentAccountRepository(ctx), messaging, new GuardianLoginIdService(messaging), Tokens(ctx), Dispatcher(ctx), new AuthRepository(ctx), Email, Throttle, Options.Create(Opt), Log.For<ParentAccountService>(), Queue);
            }
            public StudentLoginService Students(IMyStudentsService? mine = null)
            {
                var ctx = Db.Ctx();
                return new StudentLoginService(new StudentLoginRepository(ctx), Email, Log.For<StudentLoginService>(), new ParentAccountRepository(ctx), new MessagingRepository(ctx), Tokens(ctx), Dispatcher(ctx), mine, Options.Create(Opt));
            }
            public AuthService Auth()
            {
                var ctx = Db.Ctx();
                return new AuthService(new AuthRepository(ctx), new JwtTokenGenerator(Options.Create(new JwtSettings { Key = new string('k', 48) })), NullLogger<AuthService>.Instance, new LoginThrottle(new LoginThrottleOptions()), new ParentAccountRepository(ctx));
            }
        }

        private static string Q(string? s) => s == null ? "null" : "'" + s.Replace("'", "''") + "'";

        private static string AnyStudent(TaxDb db, string school) => db.Scalar<string>($"select min(student_id) from tb_students where school_id='{school}'");

        /// <summary>A guardian linked (not as primary) to a student of the school, with whatever contact and consent the test needs.</summary>
        private static long Guardian(TaxDb db, string school = A, string name = "Asha Rao", string mobile = "9876543210", string? wa = "+919876543210", string? email = "asha@example.com", bool consent = true, string? student = null, string status = "Active", bool link = true)
        {
            var id = db.Scalar<long>($@"insert into tb_guardians (school_id, first_name, relationship, mobile_number, email, whatsapp_number, status_id, consent_given_at, consent_recorded_by, consent_source)
                values ({Q(school)}, {Q(name)}, 'MOTHER', {Q(mobile)}, {Q(email)}, {Q(wa)}, (select sid from lut_status where sname = '{status}' and stype = 'general status'),
                        {(consent ? "now()" : "null")}, {(consent ? UserA.ToString() : "null")}, {(consent ? "'ADMISSION_FORM'" : "null")}) returning guardian_id");
            if (link) db.Exec($"insert into tb_student_guardians (student_id, guardian_id, relationship) values ({Q(student ?? AnyStudent(db, school))}, {id}, 'MOTHER')");
            return id;
        }

        private static long UserOf(TaxDb db, long guardianId) => db.Scalar<long>($"select user_id from tb_guardians where guardian_id = {guardianId}");

        /// <summary>Makes the account the way the real flow does, and returns the login id and the token that went into the WhatsApp message.</summary>
        private static async Task<(long GuardianId, string LoginId, string Token)> Account(Env e, string school = A, string? name = null, string? wa = "+919876543210", string? email = "asha@example.com", string? student = null)
        {
            var g = Guardian(e.Db, school, name ?? "Asha Rao", wa: wa, email: email, student: student);
            var r = await e.Svc().OnAdmissionConfirmedAsync(school, g, student ?? AnyStudent(e.Db, school), UserA, default);
            Assert.Equal("CREATED", r.Outcome);
            var token = e.Wa.Sent.LastOrDefault()?.Content.ButtonParameter ?? TokenFromMail(e);
            return (g, r.LoginId!, token);
        }

        private static string TokenFromMail(Env e)
        {
            var html = e.Email.Sent.Last().Html;
            var i = html.IndexOf("?t=", StringComparison.Ordinal) + 3;
            return html.Substring(i, 43);
        }

        private const string Strong = "Sunny-Day-2026";

        // ------------------------------------------------------------------------------------------------ creation
        [DbFact]
        public async Task An_account_is_made_with_a_generated_id_the_Guardian_role_and_no_password_and_the_link_goes_out_on_both_channels()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var g = Guardian(db);
            var r = await e.Svc().OnAdmissionConfirmedAsync(A, g, AnyStudent(db, A), UserA, default);
            Assert.Equal("CREATED", r.Outcome); Assert.Equal("GHS001GD001", r.LoginId);
            Assert.Equal(new[] { "WHATSAPP:LOGGED", "EMAIL:SENT" }, r.Deliveries.Select(d => d.Channel + ":" + d.Status));
            Assert.True(r.CanResend);
            var u = UserOf(db, g);
            Assert.Equal("Guardian", db.Scalar<string>($"select user_type from tb_users where user_id = {u}"));
            Assert.Equal("GHS001GD001", db.Scalar<string>($"select org_user_id from tb_users where user_id = {u}"));
            Assert.Null(db.Scalar<string>($"select password_hash from tb_users where user_id = {u}"));                                  // nothing to sign in with yet
            Assert.Equal("GUARDIAN", db.Scalar<string>($"select r.role_code from tb_user_roles ur join lut_roles r on r.role_id = ur.role_id where ur.user_id = {u}"));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_user_action_tokens where user_id = {u} and purpose = 'SET_PASSWORD' and used_at is null"));
            var wa = Assert.Single(e.Wa.Sent);
            Assert.Equal(ParentTemplateCodes.AccountReady, wa.Content.TemplateCode);
            Assert.Contains("GHS001GD001", wa.Content.BodyParameters);
            Assert.Equal(43, wa.Content.ButtonParameter!.Length);
            Assert.Contains("https://site.example/parent/set-password?t=" + wa.Content.ButtonParameter, Assert.Single(e.Email.Sent).Html);
            Assert.DoesNotContain("password:", e.Email.Sent[0].Html, StringComparison.OrdinalIgnoreCase);
        }

        [DbFact]
        public async Task Without_consent_no_account_and_no_message_the_reason_says_what_to_do()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var g = Guardian(db, consent: false);
            var r = await e.Svc().OnAdmissionConfirmedAsync(A, g, AnyStudent(db, A), UserA, default);
            Assert.Equal("NOT_CREATED", r.Outcome); Assert.Contains("consent", r.Reason!, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_users where user_type = 'Guardian'"));
            Assert.Empty(e.Wa.Sent); Assert.Empty(e.Email.Sent);
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_message_deliveries"));
            Assert.Null(db.Scalar<long?>($"select user_id from tb_guardians where guardian_id = {g}"));
        }

        [DbFact]
        public async Task A_guardian_with_no_channel_gets_no_account_and_one_channel_is_enough()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var none = Guardian(db, wa: null, email: null);
            Assert.Equal("NOT_CREATED", (await e.Svc().OnAdmissionConfirmedAsync(A, none, AnyStudent(db, A), UserA, default)).Outcome);
            var waOnly = Guardian(db, name: "Only WhatsApp", email: null);
            var r1 = await e.Svc().OnAdmissionConfirmedAsync(A, waOnly, AnyStudent(db, A), UserA, default);
            Assert.Equal(new[] { "WHATSAPP" }, r1.Deliveries.Select(d => d.Channel));
            var mailOnly = Guardian(db, name: "Only Email", wa: null);
            var r2 = await e.Svc().OnAdmissionConfirmedAsync(A, mailOnly, AnyStudent(db, A), UserA, default);
            Assert.Equal(new[] { "EMAIL" }, r2.Deliveries.Select(d => d.Channel));
            Assert.Equal(2, db.Scalar<int>("select count(*) from tb_users where user_type = 'Guardian'"));
        }

        [DbFact]
        public async Task A_merged_or_inactive_guardian_gets_no_account()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var inactive = Guardian(db, status: "Inactive");
            Assert.Equal("NOT_CREATED", (await e.Svc().OnAdmissionConfirmedAsync(A, inactive, AnyStudent(db, A), UserA, default)).Outcome);
            var survivor = Guardian(db, name: "Survivor"); var merged = Guardian(db, name: "Merged");
            db.Exec($"update tb_guardians set merged_into_guardian_id = {survivor} where guardian_id = {merged}");
            Assert.Equal("NOT_CREATED", (await e.Svc().OnAdmissionConfirmedAsync(A, merged, AnyStudent(db, A), UserA, default)).Outcome);
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_users where user_type = 'Guardian'"));
        }

        [DbFact]
        public async Task A_sibling_gets_the_child_added_message_and_no_second_account_and_no_new_link()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var (g, loginId, _) = await Account(e);
            e.Wa.Sent.Clear(); e.Email.Sent.Clear();
            var r = await e.Svc().OnAdmissionConfirmedAsync(A, g, "GHS001ST002", UserA, default);
            Assert.Equal("CHILD_ADDED", r.Outcome); Assert.Equal(loginId, r.LoginId);
            Assert.Equal(1, db.Scalar<int>("select count(*) from tb_users where user_type = 'Guardian'"));
            Assert.Equal(1, db.Scalar<int>("select count(*) from tb_user_action_tokens"));                                           // no new link
            Assert.Equal(ParentTemplateCodes.ChildAdded, Assert.Single(e.Wa.Sent).Content.TemplateCode);
            Assert.Null(e.Wa.Sent[0].Content.ButtonParameter);
            Assert.Contains(loginId, Assert.Single(e.Email.Sent).Html);
            Assert.DoesNotContain("?t=", e.Email.Sent[0].Html);
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_message_deliveries where purpose = 'CHILD_ADDED' and channel = 'WHATSAPP'"));
        }

        [DbFact]
        public async Task A_guardian_who_already_has_an_account_never_gets_a_second_one_even_when_asked_twice_at_once()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var g = Guardian(db);
            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => Task.Run(() => e.Svc().OnAdmissionConfirmedAsync(A, g, AnyStudent(db, A), UserA, default))));
            Assert.Equal(1, db.Scalar<int>("select count(*) from tb_users where user_type = 'Guardian'"));
            Assert.Equal(1, results.Count(r => r.Outcome == "CREATED"));
            Assert.All(results.Where(r => r.Outcome != "CREATED"), r => Assert.Equal("CHILD_ADDED", r.Outcome));
        }

        [DbFact]
        public async Task A_failed_send_never_undoes_the_account_and_is_shown_with_a_resend()
        {
            using var db = new TaxDb(); var e = new Env(db);
            e.Wa.Throw = true; e.Email.Succeeds = false;
            var g = Guardian(db);
            var r = await e.Svc().OnAdmissionConfirmedAsync(A, g, AnyStudent(db, A), UserA, default);
            Assert.Equal("CREATED", r.Outcome); Assert.True(r.CanResend);
            Assert.Equal(new[] { "WHATSAPP:FAILED", "EMAIL:FAILED" }, r.Deliveries.Select(d => d.Channel + ":" + d.Status));
            Assert.NotNull(db.Scalar<long?>($"select user_id from tb_guardians where guardian_id = {g}"));
            e.Wa.Throw = false; e.Email.Succeeds = true;
            var again = await e.Svc().ResendSetPasswordLinkAsync(A, g, UserA, default);
            Assert.True(again.Status); Assert.Equal(new[] { "LOGGED", "SENT" }, again.Data!.Deliveries.Select(d => d.Status));
        }

        [DbFact]
        public async Task Even_a_crash_inside_the_account_step_comes_back_as_a_result_not_an_exception()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var ctx = db.Ctx(); var messaging = new MessagingRepository(ctx);
            var broken = Fake.Of<IGuardianRepository>(new() { ["GetGuardianAsync"] = new InvalidOperationException("db down") });
            var svc = new ParentAccountService(broken, new ParentAccountRepository(ctx), messaging, new GuardianLoginIdService(messaging), e.Tokens(ctx), e.Dispatcher(ctx), new AuthRepository(ctx), e.Email, e.Throttle, Options.Create(e.Opt), e.Log.For<ParentAccountService>());
            var r = await svc.OnAdmissionConfirmedAsync(A, 1, "X", UserA, default);
            Assert.Equal("FAILED", r.Outcome); Assert.True(r.CanResend);
            Assert.DoesNotContain("db down", e.Log.All.Replace("InvalidOperationException", ""));   // only the error type is logged
        }

        // ------------------------------------------------------------------------------------------------ set password: single use, expiry, replay
        [DbFact]
        public async Task The_link_sets_the_password_once_ends_every_session_and_every_other_link_and_a_replay_gets_the_same_refusal()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var (g, loginId, token) = await Account(e);
            var u = UserOf(db, g);
            db.Exec($"insert into tb_refresh_tokens (user_id, token_hash, expires_at, created_at) values ({u}, 'abc', now() + interval '1 day', now())");
            var reset = await e.Tokens(db.Ctx()).IssueAsync(u, A, ActionTokenPurposes.ResetPassword, null, default);   // another unused link of the user
            var ok = await e.Svc().SetPasswordAsync(new SetParentPasswordRequest { Token = token, NewPassword = Strong }, default);
            Assert.True(ok.Status); Assert.Equal(loginId, ok.Data!.LoginId);
            Assert.NotNull(db.Scalar<string>($"select password_hash from tb_users where user_id = {u}"));
            Assert.DoesNotContain(Strong, db.Scalar<string>($"select password_hash from tb_users where user_id = {u}"));
            Assert.False(db.Scalar<bool>($"select is_first_login from tb_users where user_id = {u}"));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_refresh_tokens where user_id = {u} and revoked_at is not null"));
            Assert.False(await e.Svc().CheckLinkAsync(reset.Token, default).ContinueWith(t => t.Result.Data!.Valid));                // the other link is dead

            var replay = await e.Svc().SetPasswordAsync(new SetParentPasswordRequest { Token = token, NewPassword = "Another-Pass-9" }, default);
            Assert.False(replay.Status); Assert.Equal(400, replay.StatusCode);
            var garbage = await e.Svc().SetPasswordAsync(new SetParentPasswordRequest { Token = UserActionTokens.NewToken(), NewPassword = Strong }, default);
            Assert.Equal(replay.Message, garbage.Message);                                                                            // every kind of unusable link says the same
            Assert.True(PasswordHelper.Verify(Strong, db.Scalar<string>($"select password_hash from tb_users where user_id = {u}")));   // the replay changed nothing
        }

        [DbFact]
        public async Task An_expired_link_is_refused_and_a_weak_password_does_not_burn_the_link()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var (g, loginId, token) = await Account(e);
            var weak = await e.Svc().SetPasswordAsync(new SetParentPasswordRequest { Token = token, NewPassword = "short" }, default);
            Assert.Equal(400, weak.StatusCode);
            var same = await e.Svc().SetPasswordAsync(new SetParentPasswordRequest { Token = token, NewPassword = loginId }, default);
            Assert.Equal(400, same.StatusCode); Assert.Contains("login ID", same.Message);
            Assert.True((await e.Svc().CheckLinkAsync(token, default)).Data!.Valid);                                                 // still usable
            db.Exec("update tb_user_action_tokens set created_at = now() - interval '80 hours', expires_at = now() - interval '1 hour'");
            Assert.False((await e.Svc().CheckLinkAsync(token, default)).Data!.Valid);
            var late = await e.Svc().SetPasswordAsync(new SetParentPasswordRequest { Token = token, NewPassword = Strong }, default);
            Assert.Equal(400, late.StatusCode);
            Assert.Null(db.Scalar<string>($"select password_hash from tb_users where user_id = {UserOf(db, g)}"));
        }

        [DbFact]
        public async Task A_link_of_a_merged_or_inactive_guardian_or_of_a_user_who_is_neither_parent_nor_student_does_nothing()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var (g, _, token) = await Account(e);
            db.Exec($"update tb_guardians set merged_into_guardian_id = (select min(guardian_id) from tb_guardians where guardian_id <> {g}) where guardian_id = {g}");
            Assert.Equal(400, (await e.Svc().SetPasswordAsync(new SetParentPasswordRequest { Token = token, NewPassword = Strong }, default)).StatusCode);
            db.Exec($"update tb_guardians set merged_into_guardian_id = null where guardian_id = {g}");
            var employee = await e.Tokens(db.Ctx()).IssueAsync(UserA, A, ActionTokenPurposes.ResetPassword, null, default);       // a link for an Employee's user
            Assert.Equal(400, (await e.Svc().SetPasswordAsync(new SetParentPasswordRequest { Token = employee.Token, NewPassword = Strong }, default)).StatusCode);
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_users where user_id = {UserA} and password_hash is not null"));
            var before = db.Scalar<string>($"select password_hash from tb_users where user_id = {UserA}");
            Assert.False(PasswordHelper.Verify(Strong, before));                                                                    // the employee's password is untouched
        }

        [DbFact]
        public async Task Two_requests_with_the_same_link_set_the_password_once()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var (_, _, token) = await Account(e);
            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => e.Svc().SetPasswordAsync(new SetParentPasswordRequest { Token = token, NewPassword = "Pass-Word-" + i }, default))));
            Assert.Equal(1, results.Count(r => r.Status));
        }

        // ------------------------------------------------------------------------------------------------ signing in
        [DbFact]
        public async Task A_parent_signs_in_with_the_generated_id_only_after_choosing_a_password_and_the_token_carries_the_guardian_id_never_a_child_list()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var (g, loginId, token) = await Account(e);
            var before = await e.Auth().LoginAsync(new LoginRequest { LoginId = loginId, Password = Strong }, "ip");
            Assert.Equal(401, before.StatusCode);                                                                                      // no password yet
            await e.Svc().SetPasswordAsync(new SetParentPasswordRequest { Token = token, NewPassword = Strong }, default);
            Assert.Equal(401, (await e.Auth().LoginAsync(new LoginRequest { LoginId = "asha@example.com", Password = Strong }, "ip")).StatusCode);   // no login by email
            Assert.Equal(401, (await e.Auth().LoginAsync(new LoginRequest { LoginId = "+919876543210", Password = Strong }, "ip")).StatusCode);      // or by WhatsApp number
            var ok = await e.Auth().LoginAsync(new LoginRequest { LoginId = loginId.ToLowerInvariant(), Password = Strong }, "ip");
            Assert.Equal(200, ok.StatusCode);
            var login = Assert.IsType<LoginResponse>(ok.Data);
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(login.Token);
            Assert.Equal("Guardian", jwt.Claims.Single(c => c.Type == "user_type").Value);
            Assert.Equal(g.ToString(), jwt.Claims.Single(c => c.Type == "guardian_id").Value);
            Assert.Equal(A, jwt.Claims.Single(c => c.Type == "school_id").Value);
            Assert.Equal(new[] { "Guardian" }, jwt.Claims.Where(c => c.Type.EndsWith("/role")).Select(c => c.Value));
            Assert.DoesNotContain(jwt.Claims, c => c.Type.Contains("student", StringComparison.OrdinalIgnoreCase) || c.Type.Contains("child", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(jwt.Claims, c => c.Value.Contains("GHS001ST"));
            var refreshed = await e.Auth().RefreshAsync(new RefreshTokenRequest { RefreshToken = login.RefreshToken });
            Assert.Equal(200, refreshed.StatusCode);
            Assert.Equal(g.ToString(), new JwtSecurityTokenHandler().ReadJwtToken(Assert.IsType<LoginResponse>(refreshed.Data).Token).Claims.Single(c => c.Type == "guardian_id").Value);
        }

        [DbFact]
        public async Task A_merged_or_inactive_guardian_cannot_sign_in_and_their_sessions_end_on_refresh()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var (g, loginId, token) = await Account(e);
            await e.Svc().SetPasswordAsync(new SetParentPasswordRequest { Token = token, NewPassword = Strong }, default);
            var login = Assert.IsType<LoginResponse>((await e.Auth().LoginAsync(new LoginRequest { LoginId = loginId, Password = Strong }, "ip")).Data);
            db.Exec($"update tb_guardians set merged_into_guardian_id = (select min(guardian_id) from tb_guardians where guardian_id <> {g}) where guardian_id = {g}");
            Assert.Equal(403, (await e.Auth().LoginAsync(new LoginRequest { LoginId = loginId, Password = Strong }, "ip")).StatusCode);
            Assert.Equal(403, (await e.Auth().RefreshAsync(new RefreshTokenRequest { RefreshToken = login.RefreshToken })).StatusCode);
            db.Exec($"update tb_guardians set merged_into_guardian_id = null, status_id = (select sid from lut_status where sname = 'Inactive' and stype = 'general status') where guardian_id = {g}");
            Assert.Equal(403, (await e.Auth().LoginAsync(new LoginRequest { LoginId = loginId, Password = Strong }, "ip")).StatusCode);
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_refresh_tokens where user_id = {UserOf(db, g)} and revoked_at is null"));
        }

        [DbFact]
        public async Task A_non_guardian_login_still_works_exactly_as_before_and_has_no_guardian_claim()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var hash = PasswordHelper.Hash("Old-Pass-123");
            db.Exec($"update tb_users set password_hash = '{hash}', status_id = (select sid from lut_status where sname = 'Active' and stype = 'general status') where user_id = {UserA}");
            var login = db.Scalar<string>($"select org_user_id from tb_users where user_id = {UserA}");
            var ok = await e.Auth().LoginAsync(new LoginRequest { LoginId = login, Password = "Old-Pass-123" }, "ip");
            Assert.Equal(200, ok.StatusCode);
            Assert.DoesNotContain(new JwtSecurityTokenHandler().ReadJwtToken(Assert.IsType<LoginResponse>(ok.Data).Token).Claims, c => c.Type == "guardian_id");
        }

        // ------------------------------------------------------------------------------------------------ resend and admin reset
        [DbFact]
        public async Task A_resend_makes_a_new_link_the_older_one_stops_working_and_the_fourth_resend_in_a_day_is_refused()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var (g, _, first) = await Account(e);
            string last = first;
            for (var i = 1; i <= 3; i++)
            {
                var r = await e.Svc().ResendSetPasswordLinkAsync(A, g, UserA, default);
                Assert.True(r.Status, r.Message); Assert.Equal("RESENT", r.Data!.Outcome);
                last = e.Wa.Sent.Last().Content.ButtonParameter!;
            }
            Assert.False((await e.Svc().CheckLinkAsync(first, default)).Data!.Valid);                                                   // revoked by the newer ones
            Assert.True((await e.Svc().CheckLinkAsync(last, default)).Data!.Valid);
            var fourth = await e.Svc().ResendSetPasswordLinkAsync(A, g, UserA, default);
            Assert.Equal(429, fourth.StatusCode);
            Assert.Equal(4, db.Scalar<int>("select count(*) from tb_user_action_tokens"));                                              // the refusal issued nothing
            Assert.Equal(4, e.Wa.Sent.Count);
            var other = await Account(e, name: "Other Parent", wa: "+919800000002", email: "other@example.com", student: "GHS001ST003");
            Assert.True((await e.Svc().ResendSetPasswordLinkAsync(A, other.GuardianId, UserA, default)).Status);                         // another guardian has their own count
        }

        [DbFact]
        public async Task A_resend_is_refused_for_another_school_no_account_a_chosen_password_and_no_consent()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var (g, _, token) = await Account(e);
            Assert.Equal(404, (await e.Svc().ResendSetPasswordLinkAsync(B, g, UserA, default)).StatusCode);                              // another school's staff cannot even see it
            Assert.Equal(404, (await e.Svc().AdminResetAsync(B, g, UserA, default)).StatusCode);
            Assert.Equal(404, (await e.Svc().GetStatusAsync(B, g, default)).StatusCode);
            Assert.Equal(404, (await e.Svc().ResendSetPasswordLinkAsync(A, 99999999, UserA, default)).StatusCode);
            var noAccount = Guardian(db, name: "No Account");
            Assert.Equal(409, (await e.Svc().ResendSetPasswordLinkAsync(A, noAccount, UserA, default)).StatusCode);
            Assert.Equal(409, (await e.Svc().AdminResetAsync(A, g, UserA, default)).StatusCode);                                        // reset needs a password to reset
            await e.Svc().SetPasswordAsync(new SetParentPasswordRequest { Token = token, NewPassword = Strong }, default);
            Assert.Equal(409, (await e.Svc().ResendSetPasswordLinkAsync(A, g, UserA, default)).StatusCode);                              // already chosen
            db.Exec($"update tb_guardians set consent_given_at = null, consent_recorded_by = null, consent_source = null where guardian_id = {g}");
            Assert.Equal(409, (await e.Svc().AdminResetAsync(A, g, UserA, default)).StatusCode);
        }

        [DbFact]
        public async Task The_admin_reset_sends_a_reset_link_with_the_login_id_and_never_returns_a_link_token_or_password()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var (g, loginId, token) = await Account(e);
            await e.Svc().SetPasswordAsync(new SetParentPasswordRequest { Token = token, NewPassword = Strong }, default);
            e.Wa.Sent.Clear(); e.Email.Sent.Clear();
            var r = await e.Svc().AdminResetAsync(A, g, UserA, default);
            Assert.True(r.Status);
            var json = JsonSerializer.Serialize(r);
            var resetToken = e.Wa.Sent.Single().Content.ButtonParameter!;
            Assert.DoesNotContain(resetToken, json); Assert.DoesNotContain("?t=", json); Assert.DoesNotContain(Strong, json);
            Assert.Equal(ParentTemplateCodes.LoginHelp, e.Wa.Sent[0].Content.TemplateCode);
            Assert.Contains(loginId, e.Wa.Sent[0].Content.BodyParameters);
            Assert.Contains("/parent/reset-password?t=" + resetToken, e.Email.Sent.Single().Html);
            Assert.Equal("RESET_PASSWORD", db.Scalar<string>("select purpose from tb_user_action_tokens where used_at is null"));
            // the reset ends the old password and the old sessions only when the parent uses the link
            Assert.True(PasswordHelper.Verify(Strong, db.Scalar<string>($"select password_hash from tb_users where user_id = {UserOf(db, g)}")));
            await e.Svc().SetPasswordAsync(new SetParentPasswordRequest { Token = resetToken, NewPassword = "Brand-New-Pass-5" }, default);
            Assert.True(PasswordHelper.Verify("Brand-New-Pass-5", db.Scalar<string>($"select password_hash from tb_users where user_id = {UserOf(db, g)}")));
        }

        [DbFact]
        public async Task A_channel_can_be_switched_off_for_one_purpose_by_configuration_without_touching_the_others()
        {
            using var db = new TaxDb(); var e = new Env(db);
            e.Opt.Channels["RESET_PASSWORD"] = "EMAIL";
            var (g, _, token) = await Account(e);
            await e.Svc().SetPasswordAsync(new SetParentPasswordRequest { Token = token, NewPassword = Strong }, default);
            e.Wa.Sent.Clear();
            var r = await e.Svc().AdminResetAsync(A, g, UserA, default);
            Assert.Empty(e.Wa.Sent);
            Assert.Equal("WHATSAPP:SKIPPED:CHANNEL_OFF", r.Data!.Deliveries.Select(d => $"{d.Channel}:{d.Status}:{d.ErrorCode}").First());
            Assert.Contains(r.Data.Deliveries, d => d.Channel == "EMAIL" && d.Status == "SENT");
            Assert.True(e.Opt.ChannelAllowed("SET_PASSWORD", "WHATSAPP"));                                                               // an unlisted purpose uses both
            Assert.False(e.Opt.ChannelAllowed("RESET_PASSWORD", "WHATSAPP"));
            Assert.True(e.Opt.ChannelAllowed("reset_password", "email"));
        }

        // ------------------------------------------------------------------------------------------------ forgot id or password
        private static string Json<T>(ApiResponse<T> r) => JsonSerializer.Serialize(r);

        private static async Task<(long G, string LoginId)> ActiveAccount(Env e, string school = A, string name = "Asha Rao", string wa = "+919876543210", string email = "asha@example.com", string? student = null)
        {
            var a = await Account(e, school, name, wa, email, student);
            await e.Svc().SetPasswordAsync(new SetParentPasswordRequest { Token = a.Token, NewPassword = Strong }, default);
            e.Wa.Sent.Clear(); e.Email.Sent.Clear();
            return (a.GuardianId, a.LoginId);
        }

        [DbFact]
        public async Task Forgot_sends_the_id_and_a_reset_link_to_the_channel_typed_and_every_answer_is_identical()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var (g, loginId) = await ActiveAccount(e);
            var byNumber = await e.Svc().ForgotAsync(new ForgotLoginRequest { Contact = "98765 43210" }, "ip1", default);
            Assert.Single(e.Wa.Sent); Assert.Empty(e.Email.Sent);                                                                       // the number gets WhatsApp only
            Assert.Contains(loginId, e.Wa.Sent[0].Content.BodyParameters);
            Assert.Equal(ParentTemplateCodes.LoginHelp, e.Wa.Sent[0].Content.TemplateCode);
            var byEmail = await e.Svc().ForgotAsync(new ForgotLoginRequest { Contact = "ASHA@Example.com" }, "ip2", default);
            Assert.Single(e.Email.Sent); Assert.Single(e.Wa.Sent);                                                                       // case-insensitive; email gets email only
            Assert.Contains("/parent/reset-password?t=", e.Email.Sent[0].Html);
            var unknown = await e.Svc().ForgotAsync(new ForgotLoginRequest { Contact = "9000000001" }, "ip3", default);
            var junk = await e.Svc().ForgotAsync(new ForgotLoginRequest { Contact = "not a contact" }, "ip4", default);
            var emailUnknown = await e.Svc().ForgotAsync(new ForgotLoginRequest { Contact = "nobody@example.com" }, "ip5", default);
            Assert.All(new[] { byEmail, unknown, junk, emailUnknown }, r => Assert.Equal(Json(byNumber), Json(r)));
            Assert.Equal(200, byNumber.StatusCode);
            Assert.Equal(2, db.Scalar<int>("select count(*) from tb_user_action_tokens where purpose = 'RESET_PASSWORD'"));
            Assert.False(string.IsNullOrEmpty(loginId)); Assert.True(g > 0);
        }

        [DbFact]
        public async Task Forgot_answers_the_same_when_throttled_and_stops_sending_after_the_per_guardian_limit()
        {
            using var db = new TaxDb(); var e = new Env(db);
            await ActiveAccount(e);
            var answers = new List<string>();
            for (var i = 0; i < 9; i++) answers.Add(Json(await e.Svc().ForgotAsync(new ForgotLoginRequest { Contact = "9876543210" }, "ip" + i, default)));
            Assert.Single(answers.Distinct());                                                                                         // the 6th lookup (contact limit) and the 4th link (guardian limit) are invisible
            Assert.Equal(e.Opt.MaxLinksPer24Hours, e.Wa.Sent.Count);                                                                   // 4 sent, then nothing more
            Assert.Equal(e.Opt.MaxLinksPer24Hours, db.Scalar<int>("select count(*) from tb_user_action_tokens where purpose = 'RESET_PASSWORD'"));
            // a different contact from the same address is limited per address too
            var many = Enumerable.Range(0, 30).Select(i => e.Svc().ForgotAsync(new ForgotLoginRequest { Contact = $"90000000{i:00}" }, "same-ip", default).Result).Select(Json).Distinct().ToList();
            Assert.Single(many);
        }

        [DbFact]
        public async Task Forgot_does_nothing_for_an_account_with_no_password_yet_an_inactive_or_merged_guardian_or_a_guardian_without_an_account()
        {
            using var db = new TaxDb(); var e = new Env(db);
            await Account(e);                                                                                                           // never chose a password: the school resends that link
            e.Wa.Sent.Clear();
            await e.Svc().ForgotAsync(new ForgotLoginRequest { Contact = "9876543210" }, "a", default);
            Assert.Empty(e.Wa.Sent);
            var (g, _) = await ActiveAccount(e, name: "Second", wa: "+919800000003", email: "second@example.com", student: "GHS001ST002");
            db.Exec($"update tb_guardians set status_id = (select sid from lut_status where sname = 'Inactive' and stype = 'general status') where guardian_id = {g}");
            await e.Svc().ForgotAsync(new ForgotLoginRequest { Contact = "9800000003" }, "b", default);
            Assert.Empty(e.Wa.Sent);
            Guardian(db, name: "No account", wa: "+919800000004", email: "noaccount@example.com");
            await e.Svc().ForgotAsync(new ForgotLoginRequest { Contact = "9800000004" }, "c", default);
            Assert.Empty(e.Wa.Sent);
        }

        [DbFact]
        public async Task A_number_that_is_a_parent_in_two_schools_gets_one_message_for_each_with_that_schools_own_id()
        {
            using var db = new TaxDb(); var e = new Env(db);
            await ActiveAccount(e);                                                                                                     // school A
            await ActiveAccount(e, school: B);                                                                                          // the same number and email in school B
            await e.Svc().ForgotAsync(new ForgotLoginRequest { Contact = "9876543210" }, "x", default);
            Assert.Equal(2, e.Wa.Sent.Count);
            Assert.Equal(new[] { "APS001GD001", "GHS001GD001" }, e.Wa.Sent.Select(m => m.Content.BodyParameters[2]).OrderBy(x => x).ToArray());
            Assert.Equal(2, db.Scalar<int>("select count(distinct school_id) from tb_message_deliveries where purpose = 'RESET_PASSWORD'"));
        }

        // ------------------------------------------------------------------------------------------------ bulk creation
        [DbFact]
        public async Task The_bulk_preview_counts_only_complete_guardians_of_active_students_of_this_school()
        {
            using var db = new TaxDb(); var e = new Env(db);
            Guardian(db, name: "Complete 1"); Guardian(db, name: "Complete 2", consent: false);
            Guardian(db, name: "No email", email: null); Guardian(db, name: "No whatsapp", wa: null);
            Guardian(db, name: "Inactive", status: "Inactive"); Guardian(db, name: "No student", link: false);
            Guardian(db, school: B, name: "Other school");
            var merged = Guardian(db, name: "Merged"); db.Exec($"update tb_guardians set merged_into_guardian_id = (select min(guardian_id) from tb_guardians where guardian_id <> {merged}) where guardian_id = {merged}");
            await Account(e, name: "Has account", wa: "+919800000009", email: "has@example.com");
            var p = (await e.Svc().BulkPreviewAsync(A, default)).Data!;
            Assert.Equal(2, p.Eligible); Assert.Equal(2, p.WillCreate); Assert.Equal(100, p.MaxPerRun);
            Assert.Equal(1, p.WithoutConsentOnRecord);
            Assert.True(p.IncompleteDetails >= 2);                                                                                     // the two with a missing channel (and any seeded guardian in the school)
            Assert.Equal(0, (await e.Svc().BulkPreviewAsync("NO-SUCH-SCHOOL", default)).Data!.Eligible);
        }

        [DbFact]
        public async Task The_bulk_run_needs_the_school_admins_confirmation_records_it_as_consent_and_keeps_consent_already_on_record()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var withConsent = Guardian(db, name: "Has consent"); var without = Guardian(db, name: "No consent", consent: false);
            var no = await e.Svc().BulkCreateAsync(A, UserA, new BulkParentAccountRequest { ConfirmSchoolHoldsParentConsent = false }, default);
            Assert.Equal(400, no.StatusCode);
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_users where user_type = 'Guardian'"));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_message_deliveries"));
            var r = await e.Svc().BulkCreateAsync(A, UserA, new BulkParentAccountRequest { ConfirmSchoolHoldsParentConsent = true }, default);
            Assert.True(r.Status); Assert.Equal(2, r.Data!.Created); Assert.Equal(0, r.Data.Remaining);
            Assert.Equal("BULK_CONFIRMED", db.Scalar<string>($"select consent_source from tb_guardians where guardian_id = {without}"));
            Assert.Equal(UserA, db.Scalar<long>($"select consent_recorded_by from tb_guardians where guardian_id = {without}"));
            Assert.Equal("ADMISSION_FORM", db.Scalar<string>($"select consent_source from tb_guardians where guardian_id = {withConsent}"));  // an existing consent is never overwritten
            Assert.Equal(2, db.Scalar<int>("select count(*) from tb_users where user_type = 'Guardian'"));
            Assert.Equal(2, e.Wa.Sent.Count);
        }

        [DbFact]
        public async Task A_bulk_run_creates_at_most_100_and_the_next_run_does_the_rest()
        {
            using var db = new TaxDb(); var e = new Env(db);
            db.Exec($@"insert into tb_guardians (school_id, first_name, relationship, mobile_number, email, whatsapp_number, status_id)
                       select '{A}', 'Bulk ' || n, 'MOTHER', '98000' || lpad(n::text, 5, '0'), 'bulk' || n || '@example.com', '+9198000' || lpad(n::text, 5, '0'), (select sid from lut_status where sname = 'Active' and stype = 'general status')
                         from generate_series(1, 105) n");
            db.Exec("insert into tb_student_guardians (student_id, guardian_id, relationship) select 'GHS001ST001', guardian_id, 'MOTHER' from tb_guardians where first_name like 'Bulk %'");
            var preview = (await e.Svc().BulkPreviewAsync(A, default)).Data!;
            Assert.Equal(105, preview.Eligible); Assert.Equal(100, preview.WillCreate);
            var first = (await e.Svc().BulkCreateAsync(A, UserA, new BulkParentAccountRequest { ConfirmSchoolHoldsParentConsent = true }, default)).Data!;
            Assert.Equal(100, first.Attempted); Assert.Equal(100, first.Created); Assert.Equal(5, first.Remaining);
            Assert.Equal(100, db.Scalar<int>("select count(*) from tb_users where user_type = 'Guardian'"));
            Assert.Equal(100, db.Scalar<int>("select count(distinct lower(org_user_id)) from tb_users where user_type = 'Guardian'"));
            var second = (await e.Svc().BulkCreateAsync(A, UserA, new BulkParentAccountRequest { ConfirmSchoolHoldsParentConsent = true }, default)).Data!;
            Assert.Equal(5, second.Created); Assert.Equal(0, second.Remaining);
            var third = (await e.Svc().BulkCreateAsync(A, UserA, new BulkParentAccountRequest { ConfirmSchoolHoldsParentConsent = true }, default)).Data!;
            Assert.Equal(0, third.Attempted);
        }

        [DbFact]
        public async Task A_bulk_run_only_ever_touches_its_own_school()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var mine = Guardian(db); var other = Guardian(db, school: B, name: "Other school");
            await e.Svc().BulkCreateAsync(A, UserA, new BulkParentAccountRequest { ConfirmSchoolHoldsParentConsent = true }, default);
            Assert.NotNull(db.Scalar<long?>($"select user_id from tb_guardians where guardian_id = {mine}"));
            Assert.Null(db.Scalar<long?>($"select user_id from tb_guardians where guardian_id = {other}"));
        }

        // ------------------------------------------------------------------------------------------------ student logins by link
        private static string StudentWithoutLogin(TaxDb db) => db.Scalar<string>($"select min(s.student_id) from tb_students s where s.school_id = '{A}' and not exists (select 1 from tb_users u where u.school_id = '{A}' and u.org_user_id = s.student_id)");
        private static long PrimaryOf(TaxDb db, string student, bool consent = true, string? wa = "+919811111111", string? email = "mum@example.com")
        {
            var g = db.Scalar<long>($"select guardian_id from tb_student_guardians where student_id = '{student}' and is_primary");
            db.Exec($"update tb_guardians set whatsapp_number = {Q(wa)}, email = {Q(email)}, consent_given_at = {(consent ? "now()" : "null")}, consent_recorded_by = {(consent ? UserA.ToString() : "null")}, consent_source = {(consent ? "'MANUAL'" : "null")} where guardian_id = {g}");
            return g;
        }
        private static IMyStudentsService Mine(params string[] students) => Fake.Of<IMyStudentsService>(new()
        {
            ["GetMyClassAsync"] = Fake.Ok(new MyClassDto { Sections = new() { new MyClassSectionDto { IsClassTeacher = true, Students = students.Select(s => new MyClassStudentDto { StudentId = s }).ToList() } } }),
        });

        [DbFact]
        public async Task A_student_login_is_made_without_a_password_and_the_primary_guardian_gets_the_link_and_the_old_guessable_password_is_gone()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var s = StudentWithoutLogin(db); PrimaryOf(db, s);
            var r = await e.Students().CreateStudentLoginAsync(A, s, UserA, true, default);
            Assert.Equal(201, r.StatusCode); Assert.True(r.Data!.LinkSent);
            Assert.DoesNotContain(typeof(StudentLoginCreatedDto).GetProperties(), p => p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Token") || p.Name.Contains("Link") && p.PropertyType == typeof(string));
            Assert.Null(db.Scalar<string>($"select password_hash from tb_users where school_id = '{A}' and org_user_id = '{s}'"));
            Assert.Equal("Student", db.Scalar<string>($"select user_type from tb_users where school_id = '{A}' and org_user_id = '{s}'"));
            var wa = Assert.Single(e.Wa.Sent);
            Assert.Equal(ParentTemplateCodes.StudentLoginReady, wa.Content.TemplateCode); Assert.Equal("+919811111111", wa.ToNumber);
            Assert.Contains(s, wa.Content.BodyParameters);
            Assert.Contains("mum@example.com", Assert.Single(e.Email.Sent).To);
            Assert.Equal(1, db.Scalar<int>("select count(*) from tb_message_deliveries where purpose = 'STUDENT_LOGIN' and channel = 'WHATSAPP'"));
            // the parent chooses the password with that link, then the student signs in with the student id
            var done = await e.Svc().SetPasswordAsync(new SetParentPasswordRequest { Token = wa.Content.ButtonParameter!, NewPassword = "Student-Pass-77" }, default);
            Assert.True(done.Status); Assert.Equal(s, done.Data!.LoginId);
            Assert.Equal(200, (await e.Auth().LoginAsync(new LoginRequest { LoginId = s, Password = "Student-Pass-77" }, "ip")).StatusCode);
            var fn = db.Scalar<string>($"select first_name from tb_students where student_id = '{s}'");
            var ddmm = db.Scalar<string>($"select to_char(date_of_birth, 'DDMM') from tb_students where student_id = '{s}'");
            Assert.Equal(401, (await e.Auth().LoginAsync(new LoginRequest { LoginId = s, Password = (fn.ToUpperInvariant() + "XXXX")[..4] + ddmm }, "ip")).StatusCode);   // name + date of birth no longer works
        }

        [DbFact]
        public async Task A_student_login_without_consent_or_a_channel_is_still_made_but_no_link_goes_out_and_the_message_says_what_to_do()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var s = StudentWithoutLogin(db); PrimaryOf(db, s, consent: false);
            var r = await e.Students().CreateStudentLoginAsync(A, s, UserA, true, default);
            Assert.Equal(201, r.StatusCode); Assert.False(r.Data!.LinkSent); Assert.Contains("consent", r.Data.Message!, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(e.Wa.Sent); Assert.Empty(e.Email.Sent);
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_user_action_tokens"));
            PrimaryOf(db, s, consent: true, wa: null, email: null);
            var again = await e.Students().ResendLoginLinkAsync(A, s, UserA, true, default);
            Assert.False(again.Data!.LinkSent); Assert.Contains("WhatsApp number or email", again.Data.Message!);
            PrimaryOf(db, s);
            var fixedIt = await e.Students().ResendLoginLinkAsync(A, s, UserA, true, default);
            Assert.Equal(200, fixedIt.StatusCode); Assert.True(fixedIt.Data!.LinkSent);
        }

        [DbFact]
        public async Task A_class_teacher_may_make_logins_only_for_students_of_their_own_section_and_the_school_admin_for_any()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var s = StudentWithoutLogin(db); var other = db.Scalar<string>($"select min(s.student_id) from tb_students s where s.school_id = '{A}' and s.student_id > '{s}' and not exists (select 1 from tb_users u where u.school_id = '{A}' and u.org_user_id = s.student_id)");
            PrimaryOf(db, s); PrimaryOf(db, other);
            var teacherOf = Mine(s);
            Assert.Equal(403, (await e.Students(teacherOf).CreateStudentLoginAsync(A, other, 121, false, default)).StatusCode);       // not their section
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_users where org_user_id = '{other}'"));
            Assert.Equal(403, (await e.Students(null).CreateStudentLoginAsync(A, s, 121, false, default)).StatusCode);                // no way to know: refused
            Assert.Equal(201, (await e.Students(teacherOf).CreateStudentLoginAsync(A, s, 121, false, default)).StatusCode);          // their own
            Assert.Equal(201, (await e.Students(teacherOf).CreateStudentLoginAsync(A, other, UserA, true, default)).StatusCode);     // the School Admin: any student
            Assert.Equal(409, (await e.Students(teacherOf).CreateStudentLoginAsync(A, s, 121, false, default)).StatusCode);          // already has one
        }

        [DbFact]
        public async Task A_student_of_another_school_is_a_404_for_everyone_and_the_resend_is_limited()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var s = StudentWithoutLogin(db); PrimaryOf(db, s);
            var foreign = AnyStudent(db, B);
            Assert.Equal(404, (await e.Students().CreateStudentLoginAsync(A, foreign, UserA, true, default)).StatusCode);
            Assert.Equal(404, (await e.Students(Mine(foreign)).CreateStudentLoginAsync(A, foreign, 121, false, default)).StatusCode);
            Assert.Equal(404, (await e.Students().ResendLoginLinkAsync(A, foreign, UserA, true, default)).StatusCode);
            Assert.Equal(409, (await e.Students().ResendLoginLinkAsync(A, s, UserA, true, default)).StatusCode);                      // no login yet
            await e.Students().CreateStudentLoginAsync(A, s, UserA, true, default);
            for (var i = 0; i < 3; i++) Assert.Equal(200, (await e.Students().ResendLoginLinkAsync(A, s, UserA, true, default)).StatusCode);
            Assert.Equal(429, (await e.Students().ResendLoginLinkAsync(A, s, UserA, true, default)).StatusCode);
        }

        // ------------------------------------------------------------------------------------------------ admission: consent, account, failure
        private static AdmissionService Admissions(TaxDb db, Env e, IParentAccountService? accounts = null) =>
            new(new AdmissionRepository(db.Ctx()), Fake.Of<INotificationService>(), Fake.Of<IStudentFeeBillingService>(), NullLogger<AdmissionService>.Instance, parentAccounts: accounts ?? e.Svc());

        private static SubmitAdmissionRequestDto App(TaxDb db, string first, bool consent = true, string email = "mum@example.com", string wa = "+919876543210") => new()
        {
            AcademicYearId = TaxDb.YearA, AppliedClassId = db.Scalar<long>($"select min(class_id) from tb_classes where school_id='{A}'"),
            AdmissionTypeId = db.Scalar<long>("select min(admission_type_id) from lut_admission_type"),
            ApplicantFirstName = first, DateOfBirth = new DateTime(2018, 3, 3), GuardianName = "Meera Rao", GuardianRelationship = "MOTHER", GuardianMobileNumber = "9876543210",
            GuardianEmail = email, GuardianWhatsAppNumber = wa, GuardianConsent = consent,
        };

        private static long Section(TaxDb db) => db.Scalar<long>($"select min(section_id) from tb_academic_year_sections where academic_year_id={TaxDb.YearA} and status_id=1");

        [Fact]
        public void The_consent_tick_is_required_by_the_forms_validation_for_a_new_guardian_and_the_message_names_it()
        {
            var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
            var app = new SubmitAdmissionRequestDto { GuardianName = "x", GuardianRelationship = "MOTHER", GuardianMobileNumber = "9876543210", GuardianEmail = "a@b.com", GuardianWhatsAppSameAsMobile = true };
            Assert.False(System.ComponentModel.DataAnnotations.Validator.TryValidateObject(app, new(app), results, false) && results.Count == 0);
            Assert.Contains(app.Validate(new(app)), r => r.MemberNames.Contains(nameof(SubmitAdmissionRequestDto.GuardianConsent)));
            app.GuardianConsent = true;
            Assert.DoesNotContain(app.Validate(new(app)), r => r.MemberNames.Contains(nameof(SubmitAdmissionRequestDto.GuardianConsent)));
            var direct = new DirectStudentEntryRequestDto { GuardianName = "x", GuardianRelationship = "MOTHER", GuardianMobileNumber = "9876543210", GuardianEmail = "a@b.com", GuardianWhatsAppSameAsMobile = true };
            Assert.Contains(direct.Validate(new(direct)), r => r.MemberNames.Contains(nameof(DirectStudentEntryRequestDto.GuardianConsent)));
            direct.ExistingGuardianId = 5;   // an existing guardian is checked against the record by the service instead
            Assert.DoesNotContain(direct.Validate(new(direct)), r => r.MemberNames.Contains(nameof(DirectStudentEntryRequestDto.GuardianConsent)));
        }

        [DbFact]
        public async Task An_application_without_the_consent_tick_is_refused_and_with_it_the_tick_is_stored_with_who_and_when_and_survives_an_edit()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var no = await Admissions(db, e).SubmitAsync(A, App(db, "NoTick", consent: false), default, UserA);
            Assert.Equal(400, no.StatusCode); Assert.Contains("consent", no.Message, StringComparison.OrdinalIgnoreCase);
            var yes = await Admissions(db, e).SubmitAsync(A, App(db, "Ticked"), default, UserA);
            Assert.True(yes.Status, yes.Message); Assert.True(yes.Data!.GuardianConsentRecorded);
            Assert.Equal(UserA, db.Scalar<long>($"select guardian_consent_recorded_by from tb_admissions where admission_id = {yes.Data.AdmissionId}"));
            Assert.NotNull(db.Scalar<DateTime?>($"select guardian_consent_at from tb_admissions where admission_id = {yes.Data.AdmissionId}"));
            var edit = App(db, "Ticked"); edit.GuardianConsent = false;   // an edit that leaves the box blank does not remove a recorded consent... but a NEW guardian still needs the tick
            Assert.Equal(400, (await Admissions(db, e).UpdateAsync(A, yes.Data.AdmissionId, edit, default, UserA)).StatusCode);
            edit.GuardianConsent = true; edit.ApplicantFirstName = "Renamed";
            Assert.True((await Admissions(db, e).UpdateAsync(A, yes.Data.AdmissionId, edit, default, 121)).Status);
            Assert.Equal(UserA, db.Scalar<long>($"select guardian_consent_recorded_by from tb_admissions where admission_id = {yes.Data.AdmissionId}"));   // the first recording stands
        }

        [DbFact]
        public async Task Confirming_makes_the_parent_account_copies_the_consent_to_the_guardian_and_the_result_shows_the_deliveries()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var id = (await Admissions(db, e).SubmitAsync(A, App(db, "Confirmed"), default, UserA)).Data!.AdmissionId;
            var r = await Admissions(db, e).ConfirmAdmissionAsync(A, id, UserA, new ConfirmAdmissionRequestDto { ClassSectionId = Section(db) }, default);
            Assert.True(r.Status, r.Message);
            var pa = r.Data!.ParentAccount!;
            Assert.Equal("CREATED", pa.Outcome); Assert.Matches("^GHS001GD[0-9]{3}$", pa.LoginId!);
            Assert.Equal(new[] { "WHATSAPP:LOGGED", "EMAIL:SENT" }, pa.Deliveries.Select(d => d.Channel + ":" + d.Status));
            var g = db.Scalar<long>($"select g.guardian_id from tb_admissions a join tb_student_guardians sg on sg.student_id = a.student_id and sg.is_primary join tb_guardians g on g.guardian_id = sg.guardian_id where a.admission_id = {id}");
            Assert.Equal("ADMISSION_FORM", db.Scalar<string>($"select consent_source from tb_guardians where guardian_id = {g}"));
            Assert.Equal(UserA, db.Scalar<long>($"select consent_recorded_by from tb_guardians where guardian_id = {g}"));
            Assert.Equal(pa.LoginId, db.Scalar<string>($"select u.org_user_id from tb_users u join tb_guardians g on g.user_id = u.user_id where g.guardian_id = {g}"));
            var json = JsonSerializer.Serialize(r.Data);
            Assert.DoesNotContain(e.Wa.Sent[0].Content.ButtonParameter!, json);                                                        // no token or link in the response
        }

        [DbFact]
        public async Task A_failed_delivery_or_even_a_crashing_account_step_never_blocks_the_admission()
        {
            using var db = new TaxDb(); var e = new Env(db);
            e.Wa.Throw = true; e.Email.Succeeds = false;
            var id = (await Admissions(db, e).SubmitAsync(A, App(db, "FailedSend"), default, UserA)).Data!.AdmissionId;
            var r = await Admissions(db, e).ConfirmAdmissionAsync(A, id, UserA, new ConfirmAdmissionRequestDto { ClassSectionId = Section(db) }, default);
            Assert.True(r.Status, r.Message); Assert.Equal("ADMITTED", r.Data!.StatusName);
            Assert.Equal("CREATED", r.Data.ParentAccount!.Outcome); Assert.True(r.Data.ParentAccount.CanResend);
            Assert.All(r.Data.ParentAccount.Deliveries, d => Assert.Equal("FAILED", d.Status));

            var id2 = (await Admissions(db, e).SubmitAsync(A, App(db, "Crash", email: "crash@example.com", wa: "+919876500001"), default, UserA)).Data!.AdmissionId;
            var crashing = Fake.Of<IParentAccountService>(new() { ["OnAdmissionConfirmedAsync"] = new InvalidOperationException("boom") });
            var r2 = await Admissions(db, e, crashing).ConfirmAdmissionAsync(A, id2, UserA, new ConfirmAdmissionRequestDto { ClassSectionId = Section(db) }, default);
            Assert.True(r2.Status, r2.Message);
            Assert.Equal("FAILED", r2.Data!.ParentAccount!.Outcome);
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_students s join tb_admissions a on a.student_id = s.student_id where a.admission_id = {id2}"));   // the student exists
        }

        [DbFact]
        public async Task A_second_child_of_the_same_parent_adds_no_account_and_needs_no_new_tick()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var id = (await Admissions(db, e).SubmitAsync(A, App(db, "First"), default, UserA)).Data!.AdmissionId;
            await Admissions(db, e).ConfirmAdmissionAsync(A, id, UserA, new ConfirmAdmissionRequestDto { ClassSectionId = Section(db) }, default);
            var guardian = db.Scalar<long>("select max(guardian_id) from tb_guardians where user_id is not null");
            var sibling = App(db, "Second"); sibling.ExistingGuardianId = guardian; sibling.GuardianConsent = false;                    // consent is already on record
            var sub = await Admissions(db, e).SubmitAsync(A, sibling, default, UserA);
            Assert.True(sub.Status, sub.Message);
            e.Wa.Sent.Clear();
            var r = await Admissions(db, e).ConfirmAdmissionAsync(A, sub.Data!.AdmissionId, UserA, new ConfirmAdmissionRequestDto { ClassSectionId = Section(db) }, default);
            Assert.True(r.Status, r.Message);
            Assert.Equal("CHILD_ADDED", r.Data!.ParentAccount!.Outcome);
            Assert.Equal(1, db.Scalar<int>("select count(*) from tb_users where user_type = 'Guardian'"));
            Assert.Equal(ParentTemplateCodes.ChildAdded, Assert.Single(e.Wa.Sent).Content.TemplateCode);
            Assert.True(db.Scalar<bool>($"select portal_access from tb_student_guardians where guardian_id = {guardian} and student_id = '{r.Data.StudentId}'"));   // the child shows up by itself
        }

        // ------------------------------------------------------------------------------------------------ controllers: roles and public surface
        public static IEnumerable<object[]> RoleCases() => Fake.AllRoles.Concat(new[] { "Driver" }).Select(r => new object[] { r });

        [Theory, MemberData(nameof(RoleCases))]
        public async Task The_staff_endpoints_follow_the_role_matrix_status_and_resend_for_the_admission_roles_reset_and_bulk_for_the_School_Admin_only(string role)
        {
            var admission = role is "School Admin" or "Principal" or "Admission Staff";
            var admin = role == "School Admin";
            var svc = Fake.Of<IParentAccountService>();
            var c = Fake.WithUser(new ParentAccountsController(svc), Fake.User(A, 5, role));
            Assert.Equal(admission, Fake.Code(await c.Status(1, default)) != 403);
            Assert.Equal(admission, Fake.Code(await c.Resend(1, default)) != 403);
            Assert.Equal(admin, Fake.Code(await c.Reset(1, default)) != 403);
            Assert.Equal(admin, Fake.Code(await c.BulkPreview(default)) != 403);
            Assert.Equal(admin, Fake.Code(await c.BulkCreate(new BulkParentAccountRequest { ConfirmSchoolHoldsParentConsent = true }, default)) != 403);
            var calls = Fake.ProxyOf(svc).Calls;
            Assert.Equal(admission, calls.Contains("GetStatusAsync")); Assert.Equal(admission, calls.Contains("ResendSetPasswordLinkAsync"));
            Assert.Equal(admin, calls.Contains("AdminResetAsync")); Assert.Equal(admin, calls.Contains("BulkCreateAsync"));
        }

        [Fact]
        public async Task The_school_always_comes_from_the_token_and_a_user_without_a_school_is_refused()
        {
            var svc = Fake.Of<IParentAccountService>();
            var c = Fake.WithUser(new ParentAccountsController(svc), Fake.User(null, 5, "School Admin"));
            Assert.Equal(400, Fake.Code(await c.Status(1, default)));
            Assert.Empty(Fake.ProxyOf(svc).Calls);
        }

        [Theory, MemberData(nameof(RoleCases))]
        public async Task Student_login_creation_is_for_the_School_Admin_and_teachers_only(string role)
        {
            var svc = Fake.Of<IStudentLoginService>();
            var c = Fake.WithUser(new StudentController(Fake.Of<IStudentService>(), svc, Fake.Of<IMyStudentsService>(), Fake.Of<IStudentMyFeesService>(), Fake.Of<IStudentIdCardService>(), Fake.Of<IStudentPhotoService>()), Fake.User(A, 5, role));
            var allowed = role is "School Admin" or "Teacher";
            Assert.Equal(allowed, Fake.Code(await c.CreateLogin(A, "S1", default)) != 403);
            Assert.Equal(allowed, Fake.Code(await c.ResendLoginLink(A, "S1", default)) != 403);
            Assert.Equal(allowed, Fake.ProxyOf(svc).Calls.Contains("CreateStudentLoginAsync"));
            // another school's id in the route is refused before anything is done
            var other = Fake.WithUser(new StudentController(Fake.Of<IStudentService>(), Fake.Of<IStudentLoginService>(), Fake.Of<IMyStudentsService>(), Fake.Of<IStudentMyFeesService>(), Fake.Of<IStudentIdCardService>(), Fake.Of<IStudentPhotoService>()), Fake.User(A, 5, "School Admin"));
            Assert.Equal(403, Fake.Code(await other.CreateLogin(B, "S1", default)));
        }

        [Fact]
        public void The_public_endpoints_are_anonymous_rate_limited_post_only_and_take_the_secret_in_the_body()
        {
            var t = typeof(ParentAuthController);
            Assert.Contains(t.GetCustomAttributes(true), a => a.GetType().Name == "AllowAnonymousAttribute");
            Assert.Contains(t.GetCustomAttributes(true), a => a.GetType().Name == "EnableRateLimitingAttribute");
            foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly).Where(m => m.ReturnType == typeof(Task<IActionResult>)))
            {
                Assert.Contains(m.GetCustomAttributes(true), a => a is HttpPostAttribute);
                Assert.DoesNotContain(m.GetCustomAttributes(true), a => a is HttpGetAttribute);
                Assert.Contains(m.GetParameters(), p => p.GetCustomAttributes(true).Any(a => a is FromBodyAttribute));
                Assert.DoesNotContain(m.GetParameters(), p => p.GetCustomAttributes(true).Any(a => a is FromQueryAttribute || a is FromRouteAttribute));   // nothing secret in a URL
            }
        }

        // ------------------------------------------------------------------------------------------------ timing: the answer never waits for a send
        private sealed class TestQueue : IBackgroundTaskQueue
        {
            public readonly List<Func<IServiceProvider, CancellationToken, Task>> Work = new();
            public void Enqueue(Func<IServiceProvider, CancellationToken, Task> work) => Work.Add(work);
            private sealed class Sp : IServiceProvider
            {
                private readonly IParentAccountService _svc; public Sp(IParentAccountService svc) => _svc = svc;
                public object? GetService(Type serviceType) => serviceType == typeof(IParentAccountService) ? _svc : null;
            }
            public async Task RunAllAsync(IParentAccountService svc) { var sp = new Sp(svc); foreach (var w in Work.ToList()) await w(sp, default); Work.Clear(); }
        }

        private sealed class SlowSender : IWhatsAppSender
        {
            public int Calls; public TimeSpan Delay = TimeSpan.FromSeconds(8);
            public async Task<WhatsAppSendResult> SendAsync(WhatsAppMessage message, CancellationToken ct) { Calls++; await Task.Delay(Delay, ct); return new WhatsAppSendResult(true, "LOG", null, null, null); }
        }

        [DbFact]
        public async Task Forgot_answers_before_anything_is_made_or_sent_and_a_matching_and_a_non_matching_contact_do_the_same_work_before_answering()
        {
            using var db = new TaxDb(); var e = new Env(db);
            await ActiveAccount(e);
            var queue = new TestQueue(); e.Queue = queue;
            var match = await e.Svc().ForgotAsync(new ForgotLoginRequest { Contact = "9876543210" }, "ip1", default);
            Assert.Equal(1, queue.Work.Count);
            Assert.Empty(e.Wa.Sent);                                                                                                     // nothing sent yet
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_user_action_tokens where purpose = 'RESET_PASSWORD'"));              // not even the link is made yet
            var unknown = await e.Svc().ForgotAsync(new ForgotLoginRequest { Contact = "9000000001" }, "ip2", default);
            var junk = await e.Svc().ForgotAsync(new ForgotLoginRequest { Contact = "nonsense" }, "ip3", default);
            Assert.Equal(1, queue.Work.Count);                                                                                           // a non-match queues nothing
            Assert.Equal(Json(match), Json(unknown)); Assert.Equal(Json(match), Json(junk));
            Assert.Contains("contact your school", match.Message);
            await queue.RunAllAsync(e.Svc());
            Assert.Single(e.Wa.Sent);
            Assert.Equal(1, db.Scalar<int>("select count(*) from tb_user_action_tokens where purpose = 'RESET_PASSWORD'"));
        }

        [DbFact]
        public async Task Forgot_does_not_wait_for_a_slow_sender_and_a_failing_send_changes_nothing_for_the_caller()
        {
            using var db = new TaxDb(); var e = new Env(db);
            await ActiveAccount(e);
            var queue = new TestQueue(); e.Queue = queue;
            var slow = new SlowSender();
            var ctx = db.Ctx(); var messaging = new MessagingRepository(ctx);
            var dispatcher = new ParentMessageDispatcher(messaging, new SchoolWhatsAppSenderRepository(ctx), slow, e.Email, e.Log.For<ParentMessageDispatcher>(), Options.Create(e.Opt));
            var svc = new ParentAccountService(new GuardianRepository(ctx), new ParentAccountRepository(ctx), messaging, new GuardianLoginIdService(messaging), e.Tokens(ctx), dispatcher, new AuthRepository(ctx), e.Email, new ContactThrottle(), Options.Create(e.Opt), e.Log.For<ParentAccountService>(), queue);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var answer = await svc.ForgotAsync(new ForgotLoginRequest { Contact = "9876543210" }, "ip", default);
            sw.Stop();
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(4), $"the answer waited for the sender ({sw.Elapsed})");
            Assert.Equal(0, slow.Calls);
            Assert.Equal(200, answer.StatusCode);

            // a send that fails in the background: the answer a caller got is the same as for a contact that does not exist, and running the queue does not throw
            e.Wa.Throw = true; e.Email.Succeeds = false;
            var failing = new TestQueue(); e.Queue = failing;
            var a = await e.Svc().ForgotAsync(new ForgotLoginRequest { Contact = "asha@example.com" }, "ip5", default);
            var b = await e.Svc().ForgotAsync(new ForgotLoginRequest { Contact = "nobody@example.com" }, "ip6", default);
            Assert.Equal(Json(a), Json(b));
            await failing.RunAllAsync(e.Svc());                                                                                          // no exception
            Assert.Equal("FAILED", db.Scalar<string>("select status from tb_message_deliveries order by delivery_id desc limit 1"));
        }

        [DbFact]
        public async Task The_set_password_endpoint_answers_every_unusable_link_the_same_way_and_never_sends_anything()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var (g, _, used) = await Account(e);
            await e.Svc().SetPasswordAsync(new SetParentPasswordRequest { Token = used, NewPassword = Strong }, default);
            var revoked = (await e.Tokens(db.Ctx()).IssueAsync(UserOf(db, g), A, ActionTokenPurposes.ResetPassword, null, default)).Token;
            await e.Tokens(db.Ctx()).IssueAsync(UserOf(db, g), A, ActionTokenPurposes.ResetPassword, null, default);   // the newer link revokes the one above
            var expired = (await e.Tokens(db.Ctx()).IssueAsync(UserOf(db, g), A, ActionTokenPurposes.SetPassword, null, default)).Token;
            db.Exec($"update tb_user_action_tokens set created_at = now() - interval '5 days', expires_at = now() - interval '1 day' where token_hash = '{UserActionTokens.Hash(expired)}'");
            var sentBefore = e.Wa.Sent.Count + e.Email.Sent.Count;
            var answers = new List<string>();
            foreach (var t in new[] { used, revoked, expired, UserActionTokens.NewToken(), "garbage", "" })
                answers.Add(Json(await e.Svc().SetPasswordAsync(new SetParentPasswordRequest { Token = t, NewPassword = "Another-Pass-3" }, default)));
            Assert.Single(answers.Distinct());
            Assert.Equal(sentBefore, e.Wa.Sent.Count + e.Email.Sent.Count);
            Assert.Single(new[] { Json(await e.Svc().CheckLinkAsync(used, default)), Json(await e.Svc().CheckLinkAsync("garbage", default)), Json(await e.Svc().CheckLinkAsync(expired, default)) }.Distinct());
        }

        // ------------------------------------------------------------------------------------------------ no token or link in any log
        [DbFact]
        public async Task Nothing_secret_reaches_a_log_or_a_log_row_through_a_whole_create_resend_forgot_and_set_password_round()
        {
            using var db = new TaxDb(); var e = new Env(db);
            var (g, loginId, token) = await Account(e);
            var tokens = new List<string> { token };
            await e.Svc().ResendSetPasswordLinkAsync(A, g, UserA, default); tokens.Add(e.Wa.Sent.Last().Content.ButtonParameter!);
            await e.Svc().SetPasswordAsync(new SetParentPasswordRequest { Token = tokens.Last(), NewPassword = Strong }, default);
            await e.Svc().SetPasswordAsync(new SetParentPasswordRequest { Token = tokens.First(), NewPassword = Strong }, default);   // a refused one too
            await e.Svc().ForgotAsync(new ForgotLoginRequest { Contact = "9876543210" }, "ip", default); tokens.Add(e.Wa.Sent.Last().Content.ButtonParameter!);
            e.Wa.Throw = true; e.Email.Succeeds = false;
            await e.Svc().AdminResetAsync(A, g, UserA, default);
            var logs = e.Log.All;
            var rows = db.Scalar<string>("select string_agg(row_to_json(d)::text, ' ') from tb_message_deliveries d");
            foreach (var secret in tokens.Concat(new[] { Strong, "9876543210", "asha@example.com", "set-password", "reset-password" }))
            {
                Assert.DoesNotContain(secret, logs);
                Assert.DoesNotContain(secret, rows);
            }
            Assert.DoesNotContain("?t=", logs);
            Assert.Contains(loginId, e.Wa.Sent[0].Content.BodyParameters);   // (the login id is not a secret, and is in the message, but not in the log rows)
            Assert.DoesNotContain(loginId, rows);
        }
    }
}
