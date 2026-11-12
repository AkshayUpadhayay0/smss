using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Admin-initiated password reset: the authorization matrix (every caller role x every kind of target), the cross-school 404, self-reset, the reason rules, an inactive login (409),
    /// and what a successful reset does to the user row, the audit row and the logs. (Old password / old refresh token / first-login enforcement are exercised end to end against a real
    /// database in scripts/password-reset-smoke.py.)
    /// </summary>
    public class PasswordResetServiceTests
    {
        private const string S1 = "SCH1", S2 = "SCH2";

        private sealed class FakeRepo : IPasswordResetRepository
        {
            public readonly List<PasswordResetTarget> Targets = new();
            public readonly List<TbPasswordResetAudit> Audits = new();
            public readonly List<long> RevokedFor = new();
            public string? LastSearchSchool = "<none>";

            public Task<PasswordResetTarget?> FindTargetAsync(string loginId, string? onlySchoolId, CancellationToken ct)
            {
                LastSearchSchool = onlySchoolId;
                return Task.FromResult(Targets.FirstOrDefault(t =>
                    string.Equals(t.User.OrgUserId, loginId, StringComparison.OrdinalIgnoreCase) && (onlySchoolId == null || t.User.SchoolId == onlySchoolId)));
            }

            public Task SaveResetAsync(TbPasswordResetAudit audit, long userId, DateTime now, CancellationToken ct)
            {
                Audits.Add(audit); RevokedFor.Add(userId);
                return Task.CompletedTask;
            }
        }

        private sealed class CapturingLogger<T> : ILogger<T>
        {
            public readonly List<string> Lines = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                Lines.Add(formatter(state, exception) + (exception == null ? "" : " " + exception));
        }

        private const string OldPassword = "Old#Password1";

        private static PasswordResetTarget Target(long id, string login, string? school, bool active = true, params string[] roles) =>
            new(new TbUsers { UserId = id, OrgUserId = login, DisplayName = login + " name", SchoolId = school, UserType = "X", PasswordHash = PasswordHelper.Hash(OldPassword), IsFirstLogin = false }, roles.ToList(), active);

        // ids: 1 = Super Admin (own school-less login), 10 = S1's School Admin login, 11 = a second School Admin in S1, 12 teacher, 13 student, 14 guardian, 15 principal,
        //      16 accountant, 17 admission staff, 20 = S2's School Admin, 21 = S2's teacher
        private static FakeRepo Repo()
        {
            var r = new FakeRepo();
            r.Targets.AddRange(new[]
            {
                Target(1, "SUAD001", null, true, RoleNames.SuperAdmin),
                Target(10, "SCH1", S1, true, RoleNames.SchoolAdmin),
                Target(11, "SCH1ADM2", S1, true, RoleNames.SchoolAdmin),
                Target(12, "SCH1EM001", S1, true, RoleNames.Teacher),
                Target(13, "SCH1ST001", S1, true, RoleNames.Student),
                Target(14, "SCH1GD001", S1, true, RoleNames.Guardian),
                Target(15, "SCH1EM002", S1, true, RoleNames.Principal),
                Target(16, "SCH1EM003", S1, true, RoleNames.Accountant),
                Target(17, "SCH1EM004", S1, true, RoleNames.AdmissionStaff),
                Target(20, "SCH2", S2, true, RoleNames.SchoolAdmin),
                Target(21, "SCH2EM001", S2, true, RoleNames.Teacher),
            });
            return r;
        }

        private static (PasswordResetService Svc, FakeRepo Repo, CapturingLogger<PasswordResetService> Log) Make(FakeRepo? repo = null)
        {
            repo ??= Repo(); var log = new CapturingLogger<PasswordResetService>();
            return (new PasswordResetService(repo, log, new LoginThrottle(new LoginThrottleOptions())), repo, log);
        }

        private static PasswordResetCaller Caller(long id, string? school, params string[] roles) => new(id, school, roles);
        private static ResetPasswordRequest Req(string login, string? reason = "Forgot it") => new() { LoginId = login, Reason = reason };

        // ---- the authorization matrix: every caller role x every kind of target -------------------------------------------------------------

        public static IEnumerable<object[]> Matrix()
        {
            var targets = new (string Login, string Kind)[]
            {
                ("SCH1", "own school's School Admin login"), ("SCH1ADM2", "second School Admin of the school"), ("SCH1EM001", "teacher"), ("SCH1ST001", "student"), ("SCH1GD001", "guardian"),
                ("SCH1EM002", "principal"), ("SCH1EM003", "accountant"), ("SCH1EM004", "admission staff"),
                ("SCH2", "another school's School Admin"), ("SCH2EM001", "another school's teacher"), ("SUAD001", "Super Admin"),
            };
            // role -> (caller id, school); null school for the Super Admin
            var callers = new (string Role, long Id, string? School)[]
            {
                (RoleNames.SuperAdmin, 1, null), (RoleNames.SchoolAdmin, 10, S1), (RoleNames.Principal, 15, S1), (RoleNames.Teacher, 12, S1), (RoleNames.Accountant, 16, S1),
                (RoleNames.AdmissionStaff, 17, S1), (RoleNames.Student, 13, S1), (RoleNames.Guardian, 14, S1),
            };
            foreach (var c in callers)
                foreach (var t in targets)
                    yield return new object[] { c.Role, c.Id, c.School ?? "", t.Login, t.Kind, Expected(c.Role, c.Id, t.Login) };
        }

        /// <summary>The rule as the specification states it, written independently of the service.</summary>
        private static int Expected(string callerRole, long callerId, string targetLogin)
        {
            var self = (callerId == 1 && targetLogin == "SUAD001") || (callerId == 10 && targetLogin == "SCH1") || (callerId == 12 && targetLogin == "SCH1EM001") || (callerId == 13 && targetLogin == "SCH1ST001")
                       || (callerId == 14 && targetLogin == "SCH1GD001") || (callerId == 15 && targetLogin == "SCH1EM002") || (callerId == 16 && targetLogin == "SCH1EM003") || (callerId == 17 && targetLogin == "SCH1EM004");
            var targetInS1 = targetLogin.StartsWith("SCH1");
            var targetIsSchoolAdmin = targetLogin is "SCH1" or "SCH1ADM2" or "SCH2";
            return callerRole switch
            {
                RoleNames.SuperAdmin => self ? 403 : targetIsSchoolAdmin ? 200 : 403,
                RoleNames.SchoolAdmin => !targetInS1 && targetLogin != "SUAD001" ? 404 : self ? 403 : targetLogin == "SUAD001" ? 404 : 200,
                _ => 403,   // Principal, Teacher, Accountant, Admission Staff, Student, Guardian: nobody
            };
        }

        [Theory]
        [MemberData(nameof(Matrix))]
        public async Task Who_may_reset_whom(string callerRole, long callerId, string school, string targetLogin, string kind, int expected)
        {
            var (svc, repo, _) = Make();
            var r = await svc.ResetAsync(Caller(callerId, school == "" ? null : school, callerRole), Req(targetLogin), default);
            Assert.True(expected == r.StatusCode, $"{callerRole} -> {kind} ({targetLogin}): expected {expected}, got {r.StatusCode} ({r.Message})");
            if (expected == 200) { Assert.Single(repo.Audits); Assert.NotNull(r.Data); }
            else { Assert.Empty(repo.Audits); Assert.Null(r.Data); }
        }

        // ---- the specific promises ---------------------------------------------------------------------------------------------------------

        [Fact]
        public async Task A_School_Admin_only_ever_searches_their_OWN_school_taken_from_the_token()
        {
            var (svc, repo, _) = Make();
            await svc.ResetAsync(Caller(10, S1, RoleNames.SchoolAdmin), Req("SCH2EM001"), default);
            Assert.Equal(S1, repo.LastSearchSchool);   // the search is limited to the token's school, so SCH2's teacher is simply not found
            Assert.Equal(404, (await svc.ResetAsync(Caller(10, S1, RoleNames.SchoolAdmin), Req("SCH2EM001"), default)).StatusCode);
            Assert.Equal(404, (await svc.ResetAsync(Caller(10, S1, RoleNames.SchoolAdmin), Req("NOBODY"), default)).StatusCode);   // another school's user and a missing one look the same
        }

        [Fact]
        public async Task A_School_Admin_without_a_school_on_the_token_is_refused()
        {
            var (svc, _, _) = Make();
            Assert.Equal(403, (await svc.ResetAsync(Caller(10, null, RoleNames.SchoolAdmin), Req("SCH1EM001"), default)).StatusCode);
        }

        [Fact]
        public async Task Nobody_resets_their_own_password_this_way()
        {
            var (svc, repo, _) = Make();
            var r = await svc.ResetAsync(Caller(10, S1, RoleNames.SchoolAdmin), Req("sch1" /* case-insensitive */), default);
            Assert.Equal(403, r.StatusCode); Assert.Contains("Change Password", r.Message); Assert.Empty(repo.Audits);
            Assert.Equal(403, (await svc.ResetAsync(Caller(1, null, RoleNames.SuperAdmin), Req("SUAD001"), default)).StatusCode);
        }

        [Theory]
        [InlineData(null)] [InlineData("")] [InlineData("   ")]
        public async Task A_reason_is_required(string? reason)
        {
            var (svc, repo, _) = Make();
            var r = await svc.ResetAsync(Caller(10, S1, RoleNames.SchoolAdmin), Req("SCH1EM001", reason), default);
            Assert.Equal(400, r.StatusCode); Assert.Empty(repo.Audits);
        }

        [Fact]
        public async Task The_reason_is_limited_to_500_characters_and_a_blank_login_id_is_refused()
        {
            var (svc, repo, _) = Make();
            var admin = Caller(10, S1, RoleNames.SchoolAdmin);
            Assert.Equal(400, (await svc.ResetAsync(admin, Req("SCH1EM001", new string('x', 501)), default)).StatusCode);
            Assert.Equal(200, (await svc.ResetAsync(admin, Req("SCH1EM001", new string('x', 500)), default)).StatusCode);
            Assert.Equal(400, (await svc.ResetAsync(admin, Req(" "), default)).StatusCode);
            Assert.Single(repo.Audits);
        }

        [Fact]
        public async Task An_inactive_or_blocked_login_is_a_409_and_nothing_changes()
        {
            var repo = Repo(); repo.Targets.Add(Target(30, "SCH1EM030", S1, active: false, RoleNames.Teacher));
            var (svc, _, _) = Make(repo);
            var before = repo.Targets.Last().User.PasswordHash;
            var r = await svc.ResetAsync(Caller(10, S1, RoleNames.SchoolAdmin), Req("SCH1EM030"), default);
            Assert.Equal(409, r.StatusCode); Assert.Contains("inactive or blocked", r.Message);
            Assert.Equal(before, repo.Targets.Last().User.PasswordHash); Assert.Empty(repo.Audits); Assert.Empty(repo.RevokedFor);
        }

        // ---- what a successful reset does ---------------------------------------------------------------------------------------------------

        [Fact]
        public async Task A_reset_replaces_the_password_forces_a_change_revokes_sessions_and_writes_one_audit_row()
        {
            var (svc, repo, _) = Make();
            var user = repo.Targets.First(t => t.User.UserId == 12).User;
            Assert.False(user.IsFirstLogin); Assert.True(PasswordHelper.Verify(OldPassword, user.PasswordHash!));

            var r = await svc.ResetAsync(Caller(10, S1, RoleNames.SchoolAdmin), Req("SCH1EM001", "  Forgot it after the holidays  "), default);

            Assert.Equal(200, r.StatusCode);
            var temp = r.Data!.TemporaryPassword;
            Assert.Equal(12, temp.Length);
            Assert.True(PasswordHelper.Verify(temp, user.PasswordHash!));            // the new password works ...
            Assert.False(PasswordHelper.Verify(OldPassword, user.PasswordHash!));     // ... and the old one does not
            Assert.NotEqual(temp, user.PasswordHash);                                  // only the hash is stored
            Assert.True(user.IsFirstLogin);                                            // first-login enforcement applies until they change it
            Assert.Equal(new[] { 12L }, repo.RevokedFor);                              // every refresh token of that user is revoked

            var audit = Assert.Single(repo.Audits);
            Assert.Equal((12L, "SCH1EM001", 10L, S1, "Forgot it after the holidays"), (audit.TargetUserId, audit.TargetLoginId, audit.ResetByUserId, audit.SchoolId, audit.Reason));
            Assert.True(audit.ResetAt > DateTime.UtcNow.AddMinutes(-1));
            Assert.DoesNotContain(temp, string.Join("|", audit.TargetLoginId, audit.Reason, audit.SchoolId));   // the audit row holds no password
            Assert.All(typeof(TbPasswordResetAudit).GetProperties(), p => Assert.DoesNotContain("password", p.Name, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Two_resets_give_two_different_passwords()
        {
            var (svc, _, _) = Make();
            var a = (await svc.ResetAsync(Caller(10, S1, RoleNames.SchoolAdmin), Req("SCH1EM001"), default)).Data!.TemporaryPassword;
            var b = (await svc.ResetAsync(Caller(10, S1, RoleNames.SchoolAdmin), Req("SCH1EM001"), default)).Data!.TemporaryPassword;
            Assert.NotEqual(a, b);
        }

        [Fact]
        public async Task The_password_and_the_reason_never_reach_the_logs_not_even_when_the_save_fails()
        {
            var (svc, _, log) = Make();
            var ok = await svc.ResetAsync(Caller(10, S1, RoleNames.SchoolAdmin), Req("SCH1EM001", "secret-reason-text"), default);
            var all = string.Join("\n", log.Lines);
            Assert.NotEmpty(log.Lines);
            Assert.DoesNotContain(ok.Data!.TemporaryPassword, all);
            Assert.DoesNotContain("secret-reason-text", all);

            var failing = new ThrowingRepo(Repo().Targets);
            var log2 = new CapturingLogger<PasswordResetService>();
            var r = await new PasswordResetService(failing, log2, new LoginThrottle(new LoginThrottleOptions())).ResetAsync(Caller(10, S1, RoleNames.SchoolAdmin), Req("SCH1EM001", "secret-reason-text"), default);
            Assert.Equal(500, r.StatusCode); Assert.Null(r.Data);
            var user = failing.Targets.First(t => t.User.UserId == 12).User;
            var failedLog = string.Join("\n", log2.Lines);
            Assert.DoesNotContain("secret-reason-text", failedLog);
            Assert.DoesNotContain(user.PasswordHash!, failedLog);   // not even the new hash
        }

        private sealed class ThrowingRepo : IPasswordResetRepository
        {
            public readonly List<PasswordResetTarget> Targets;
            public ThrowingRepo(List<PasswordResetTarget> targets) => Targets = targets;
            public Task<PasswordResetTarget?> FindTargetAsync(string loginId, string? onlySchoolId, CancellationToken ct) =>
                Task.FromResult(Targets.FirstOrDefault(t => string.Equals(t.User.OrgUserId, loginId, StringComparison.OrdinalIgnoreCase) && (onlySchoolId == null || t.User.SchoolId == onlySchoolId)));
            public Task SaveResetAsync(TbPasswordResetAudit audit, long userId, DateTime now, CancellationToken ct) => throw new InvalidOperationException("database down");
        }

        [Fact]
        public async Task The_response_is_the_one_place_the_password_appears()
        {
            var (svc, _, _) = Make();
            var r = await svc.ResetAsync(Caller(1, null, RoleNames.SuperAdmin), Req("SCH2"), default);
            Assert.Equal(200, r.StatusCode);
            Assert.Equal("SCH2", r.Data!.LoginId);
            Assert.Equal(12, r.Data.TemporaryPassword.Length);
        }
    }
}
