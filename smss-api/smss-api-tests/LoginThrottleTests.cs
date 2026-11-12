using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>POST /Auth/login throttling: the limit triggers, the window expires, other ids and addresses are unaffected, unknown ids behave like known ones, and nothing reveals whether an id exists.</summary>
    public class LoginThrottleTests
    {
        private sealed class ManualClock : TimeProvider
        {
            private DateTimeOffset _now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
            public override DateTimeOffset GetUtcNow() => _now;
            public void Advance(TimeSpan by) => _now += by;
        }

        private static (LoginThrottle T, ManualClock C) Make(int perId = 5, int perIp = 30, int minutes = 15, int maxKeys = 50_000)
        {
            var c = new ManualClock();
            return (new LoginThrottle(new LoginThrottleOptions { MaxFailedPerLoginId = perId, MaxFailedPerIp = perIp, WindowMinutes = minutes, MaxTrackedKeys = maxKeys }, c), c);
        }

        private static void Fail(LoginThrottle t, string id, string ip, int times) { for (var i = 0; i < times; i++) t.RecordFailure(id, ip); }

        // ---- the counter itself ----------------------------------------------------------------------------------------------------------

        [Fact]
        public void Five_failures_are_allowed_the_sixth_attempt_is_blocked_with_a_wait()
        {
            var (t, _) = Make();
            Fail(t, "DPS001EM001", "1.1.1.1", 4);
            Assert.Null(t.RetryAfter("DPS001EM001", "1.1.1.1"));
            t.RecordFailure("DPS001EM001", "1.1.1.1");
            var wait = t.RetryAfter("DPS001EM001", "1.1.1.1");
            Assert.NotNull(wait);
            Assert.Equal(TimeSpan.FromMinutes(15), wait!.Value);
        }

        [Fact]
        public void The_window_expires_and_the_wait_counts_down()
        {
            var (t, c) = Make();
            Fail(t, "A", "1.1.1.1", 5);
            c.Advance(TimeSpan.FromMinutes(10));
            Assert.Equal(TimeSpan.FromMinutes(5), t.RetryAfter("A", "1.1.1.1"));
            c.Advance(TimeSpan.FromMinutes(5));
            Assert.Null(t.RetryAfter("A", "1.1.1.1"));   // nothing is locked for good
        }

        [Fact]
        public void A_sliding_window_frees_one_attempt_as_the_oldest_failure_ages_out()
        {
            var (t, c) = Make();
            t.RecordFailure("A", "ip"); c.Advance(TimeSpan.FromMinutes(5));
            Fail(t, "A", "ip", 4);                       // 5 failures, the first is 5 minutes old
            Assert.Equal(TimeSpan.FromMinutes(10), t.RetryAfter("A", "ip"));
            c.Advance(TimeSpan.FromMinutes(10));         // the first has now left the window
            Assert.Null(t.RetryAfter("A", "ip"));
        }

        [Fact]
        public void Other_ids_and_other_addresses_are_unaffected()
        {
            var (t, _) = Make();
            Fail(t, "VICTIM", "9.9.9.9", 5);
            Assert.NotNull(t.RetryAfter("VICTIM", "2.2.2.2"));   // the id is throttled from anywhere
            Assert.Null(t.RetryAfter("OTHER", "2.2.2.2"));       // another id from another address
            Assert.Null(t.RetryAfter("OTHER", "9.9.9.9"));       // another id from the SAME address (well under the address limit)
        }

        [Fact]
        public void The_per_address_limit_throttles_guessing_across_many_ids()
        {
            var (t, _) = Make(perIp: 30);
            for (var i = 0; i < 30; i++) t.RecordFailure("ID" + i, "7.7.7.7");   // one failure each: no id is near its own limit
            Assert.NotNull(t.RetryAfter("BRAND-NEW-ID", "7.7.7.7"));
            Assert.Null(t.RetryAfter("BRAND-NEW-ID", "8.8.8.8"));
        }

        [Fact]
        public void A_success_resets_the_id_but_not_the_address()
        {
            var (t, _) = Make(perIp: 6);
            Fail(t, "MINE", "5.5.5.5", 3);
            Fail(t, "OTHER", "5.5.5.5", 3);
            t.RecordSuccess("MINE");
            Assert.Null(t.RetryAfter("MINE", "6.6.6.6"));
            Fail(t, "MINE", "6.6.6.6", 4);
            Assert.Null(t.RetryAfter("MINE", "6.6.6.6"));        // the count really restarted (4 < 5)
            Assert.NotNull(t.RetryAfter("ANY", "5.5.5.5"));      // 6 failures from that address are still remembered
        }

        [Fact]
        public void Ids_are_matched_case_insensitively_and_trimmed_like_the_login_itself()
        {
            var (t, _) = Make();
            Fail(t, "dps001em001", "1.1.1.1", 5);
            Assert.NotNull(t.RetryAfter("  DPS001EM001 ", "2.2.2.2"));
        }

        [Fact]
        public void ClearLogin_forgets_the_id()
        {
            var (t, _) = Make();
            Fail(t, "A", "ip", 5);
            t.ClearLogin("a");
            Assert.Null(t.RetryAfter("A", "other-ip"));
        }

        [Fact]
        public void Memory_is_bounded_random_ids_past_the_cap_are_not_tracked()
        {
            var (t, _) = Make(maxKeys: 100);
            for (var i = 0; i < 1000; i++) t.RecordFailure("RANDOM" + i, "ip" + (i % 3));
            // still answers, and a tracked id is still throttled normally
            Fail(t, "RANDOM0", "ipX", 5);
            Assert.NotNull(t.RetryAfter("RANDOM0", "ipX"));
        }

        [Fact]
        public void Expired_entries_are_swept()
        {
            var (t, c) = Make();
            for (var i = 0; i < 50; i++) t.RecordFailure("ID" + i, "ip");
            c.Advance(TimeSpan.FromMinutes(20));
            t.RecordFailure("NEW", "ip");   // triggers the sweep
            Assert.Null(t.RetryAfter("ID1", "ip"));
        }

        [Fact]
        public void The_limits_come_from_configuration_with_sane_floors()
        {
            var cfg = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["LoginThrottle:MaxFailedPerLoginId"] = "3", ["LoginThrottle:MaxFailedPerIp"] = "0", ["LoginThrottle:WindowMinutes"] = "2" }).Build();
            var o = LoginThrottleOptions.From(cfg);
            Assert.Equal((3, 1, 2), (o.MaxFailedPerLoginId, o.MaxFailedPerIp, o.WindowMinutes));
            var d = LoginThrottleOptions.From(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
            Assert.Equal((5, 30, 15), (d.MaxFailedPerLoginId, d.MaxFailedPerIp, d.WindowMinutes));
        }

        // ---- through AuthService.LoginAsync ----------------------------------------------------------------------------------------------

        private class RepoProxy : DispatchProxy
        {
            public TbUsers? User;
            public int LookupCount;
            protected override object? Invoke(MethodInfo? m, object?[]? args)
            {
                var ret = m!.ReturnType;
                if (m.Name == nameof(IAuthRepository.GetUserByLoginIdAsync))
                {
                    LookupCount++;
                    var id = (string)args![0]!;
                    return Task.FromResult(User != null && string.Equals(User.OrgUserId, id, StringComparison.OrdinalIgnoreCase) ? User : null);
                }
                if (m.Name == nameof(IAuthRepository.GetStatusIdByNameAsync)) return Task.FromResult<int?>(1);
                if (m.Name == nameof(IAuthRepository.GetRolesByUserIdAsync)) return Task.FromResult(new List<LutRole>());
                if (ret == typeof(Task)) return Task.CompletedTask;
                if (ret.IsGenericType && ret.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    var inner = ret.GetGenericArguments()[0];
                    return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, new[] { inner.IsValueType ? Activator.CreateInstance(inner) : null });
                }
                return null;
            }
        }

        private const string Password = "Right#Pass1";

        private static (AuthService Svc, RepoProxy Repo, LoginThrottle T, ManualClock C) Auth()
        {
            var repo = DispatchProxy.Create<IAuthRepository, RepoProxy>();
            var proxy = (RepoProxy)(object)repo;
            proxy.User = new TbUsers { UserId = 7, OrgUserId = "DPS001EM001", UserType = "Employee", PasswordHash = PasswordHelper.Hash(Password), StatusId = 1, DisplayName = "Asha" };
            var (t, c) = Make();
            var jwt = new JwtTokenGenerator(Options.Create(new JwtSettings { Key = new string('k', 40) }));
            return (new AuthService(repo, jwt, NullLogger<AuthService>.Instance, t), proxy, t, c);
        }

        private static LoginRequest Req(string id, string pw) => new() { LoginId = id, Password = pw };

        [Fact]
        public async Task Five_wrong_passwords_then_429_with_a_wait_even_for_the_right_password()
        {
            var (svc, _, _, _) = Auth();
            for (var i = 0; i < 5; i++) Assert.Equal(401, (await svc.LoginAsync(Req("DPS001EM001", "wrong" + i), "1.1.1.1")).StatusCode);
            var blocked = await svc.LoginAsync(Req("DPS001EM001", Password), "1.1.1.1");
            Assert.Equal(429, blocked.StatusCode);
            var data = Assert.IsType<LoginThrottledDto>(blocked.Data);
            Assert.InRange(data.RetryAfterSeconds, 1, 15 * 60);
            Assert.Contains("Too many failed sign-in attempts", blocked.Message);
        }

        [Fact]
        public async Task Unknown_ids_are_throttled_the_same_way_and_the_answers_are_identical_to_a_known_id()
        {
            var (svc, repo, _, _) = Auth();
            for (var i = 0; i < 5; i++) { Assert.Equal(401, (await svc.LoginAsync(Req("NO-SUCH-ID", "x"), "1.1.1.1")).StatusCode); Assert.Equal(401, (await svc.LoginAsync(Req("DPS001EM001", "x"), "2.2.2.2")).StatusCode); }
            var unknown = await svc.LoginAsync(Req("NO-SUCH-ID", "x"), "3.3.3.3");
            var known = await svc.LoginAsync(Req("DPS001EM001", "x"), "4.4.4.4");
            Assert.Equal(429, unknown.StatusCode); Assert.Equal(429, known.StatusCode);
            Assert.Equal(known.Message, unknown.Message);                                              // nothing in the text tells them apart
            Assert.Equal(((LoginThrottledDto)known.Data!).RetryAfterSeconds, ((LoginThrottledDto)unknown.Data!).RetryAfterSeconds);
        }

        [Fact]
        public async Task A_throttled_attempt_never_reaches_the_database_or_the_password_check()
        {
            var (svc, repo, _, _) = Auth();
            for (var i = 0; i < 5; i++) await svc.LoginAsync(Req("DPS001EM001", "bad"), "1.1.1.1");
            var before = repo.LookupCount;
            await svc.LoginAsync(Req("DPS001EM001", Password), "1.1.1.1");
            Assert.Equal(before, repo.LookupCount);
        }

        [Fact]
        public async Task The_wrong_password_and_the_unknown_id_give_the_same_401()
        {
            var (svc, _, _, _) = Auth();
            var wrong = await svc.LoginAsync(Req("DPS001EM001", "nope"), "1.1.1.1");
            var unknown = await svc.LoginAsync(Req("NO-SUCH-ID", "nope"), "1.1.1.1");
            Assert.Equal((wrong.StatusCode, wrong.Message), (unknown.StatusCode, unknown.Message));
        }

        [Fact]
        public async Task The_window_expires_and_a_right_password_signs_in_again()
        {
            var (svc, _, _, c) = Auth();
            for (var i = 0; i < 5; i++) await svc.LoginAsync(Req("DPS001EM001", "bad"), "1.1.1.1");
            Assert.Equal(429, (await svc.LoginAsync(Req("DPS001EM001", Password), "1.1.1.1")).StatusCode);
            c.Advance(TimeSpan.FromMinutes(15));
            Assert.Equal(200, (await svc.LoginAsync(Req("DPS001EM001", Password), "1.1.1.1")).StatusCode);
        }

        [Fact]
        public async Task A_successful_login_resets_the_count_for_that_id()
        {
            var (svc, _, _, _) = Auth();
            for (var i = 0; i < 4; i++) await svc.LoginAsync(Req("DPS001EM001", "bad"), "1.1.1.1");
            Assert.Equal(200, (await svc.LoginAsync(Req("DPS001EM001", Password), "1.1.1.1")).StatusCode);
            for (var i = 0; i < 4; i++) Assert.Equal(401, (await svc.LoginAsync(Req("DPS001EM001", "bad"), "1.1.1.1")).StatusCode);   // 4 again, still not blocked
            Assert.Equal(200, (await svc.LoginAsync(Req("DPS001EM001", Password), "1.1.1.1")).StatusCode);
        }

        [Fact]
        public async Task Another_id_from_the_same_address_and_the_same_id_from_another_address_are_judged_on_their_own()
        {
            var (svc, repo, _, _) = Auth();
            for (var i = 0; i < 5; i++) await svc.LoginAsync(Req("DPS001EM001", "bad"), "1.1.1.1");
            repo.User = new TbUsers { UserId = 8, OrgUserId = "DPS001EM002", UserType = "Employee", PasswordHash = PasswordHelper.Hash(Password), StatusId = 1, DisplayName = "Ravi" };
            Assert.Equal(200, (await svc.LoginAsync(Req("DPS001EM002", Password), "1.1.1.1")).StatusCode);
        }

        // ---- a School Admin / Super Admin password reset clears the counter ----------------------------------------------------------

        private sealed class OneUserResetRepo : IPasswordResetRepository
        {
            public readonly TbUsers User = new() { UserId = 12, OrgUserId = "DPS001EM001", SchoolId = "SCH1", UserType = "Employee", PasswordHash = PasswordHelper.Hash("old"), StatusId = 1 };
            public Task<PasswordResetTarget?> FindTargetAsync(string loginId, string? onlySchoolId, CancellationToken ct) =>
                Task.FromResult<PasswordResetTarget?>(string.Equals(loginId, User.OrgUserId, StringComparison.OrdinalIgnoreCase) ? new PasswordResetTarget(User, new List<string> { RoleNames.Teacher }, true) : null);
            public Task SaveResetAsync(TbPasswordResetAudit audit, long userId, DateTime now, CancellationToken ct) => Task.CompletedTask;
        }

        [Fact]
        public async Task A_password_reset_clears_the_login_ids_counter_so_the_person_can_sign_in_at_once()
        {
            var (t, _) = Make();
            Fail(t, "DPS001EM001", "1.1.1.1", 5);
            Assert.NotNull(t.RetryAfter("DPS001EM001", "1.1.1.1"));
            var svc = new PasswordResetService(new OneUserResetRepo(), NullLogger<PasswordResetService>.Instance, t);
            var r = await svc.ResetAsync(new PasswordResetCaller(10, "SCH1", new[] { RoleNames.SchoolAdmin }), new ResetPasswordRequest { LoginId = "DPS001EM001", Reason = "locked out" }, default);
            Assert.Equal(200, r.StatusCode);
            Assert.Null(t.RetryAfter("DPS001EM001", "9.9.9.9"));
        }

        [Fact]
        public async Task A_refused_reset_does_not_clear_anything()
        {
            var (t, _) = Make();
            Fail(t, "DPS001EM001", "1.1.1.1", 5);
            var svc = new PasswordResetService(new OneUserResetRepo(), NullLogger<PasswordResetService>.Instance, t);
            var r = await svc.ResetAsync(new PasswordResetCaller(10, "SCH1", new[] { RoleNames.Teacher }), new ResetPasswordRequest { LoginId = "DPS001EM001", Reason = "x" }, default);
            Assert.Equal(403, r.StatusCode);
            Assert.NotNull(t.RetryAfter("DPS001EM001", "9.9.9.9"));
        }
    }
}
