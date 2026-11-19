using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// The operator-facing log of the daily fee-reminder job: ONE Information line per school run with counts only. These tests need no database
    /// (in-memory repository, fake Firebase, hand-moved clock), so they always run.
    /// </summary>
    public class FeeReminderLogLineTests
    {
        private const string School = "LOGSCHOOL001", Other = "LOGSCHOOL002";
        private const string SecretStudent = "SECRETSTUDENT", SecretToken = "tok-secret";

        // ------------------------------------------------------------------ fakes

        private sealed class LineLog<T> : ILogger<T>
        {
            public readonly List<(LogLevel Level, string Text)> Entries = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                Entries.Add((logLevel, formatter(state, exception)));
            public List<string> Info => Entries.Where(e => e.Level == LogLevel.Information).Select(e => e.Text).ToList();
        }

        private sealed class MemRepo : INotificationRepository
        {
            public readonly List<TbSchoolNotificationSettings> Settings = new();
            public readonly Dictionary<string, List<EligibleStudent>> Overdue = new();
            public readonly Dictionary<long, string> Devices = new(); // user id -> token (one device each)
            public readonly List<TbPushNotificationLog> Log = new();

            public Task<List<TbSchoolNotificationSettings>> GetEnabledSettingsAsync(CancellationToken ct) => Task.FromResult(Settings.Where(s => s.FeeReminderEnabled).ToList());
            public Task<TbSchoolNotificationSettings?> GetSettingsForUpdateAsync(string schoolId, CancellationToken ct) => Task.FromResult(Settings.FirstOrDefault(s => s.SchoolId == schoolId));
            public Task<TbSchoolNotificationSettings?> GetSettingsAsync(string schoolId, CancellationToken ct) => GetSettingsForUpdateAsync(schoolId, ct);
            public Task<bool> TryLockSchoolRunAsync(string schoolId, CancellationToken ct) => Task.FromResult(true);
            public Task<bool> UnlockSchoolRunAsync(string schoolId) => Task.FromResult(true);
            public Task<long?> GetCurrentAcademicYearIdAsync(string schoolId, CancellationToken ct) => Task.FromResult<long?>(1);
            public Task<List<EligibleStudent>> GetEligibleStudentsAsync(string schoolId, long academicYearId, string studentUserType, CancellationToken ct) =>
                Task.FromResult(Overdue.TryGetValue(schoolId, out var l) ? l.ToList() : new List<EligibleStudent>());
            public Task<int> CreateLogRowsAsync(string schoolId, DateOnly date, string type, string title, string body, IReadOnlyCollection<EligibleStudent> students, CancellationToken ct)
            {
                var added = 0;
                foreach (var s in students.Where(s => !Log.Any(r => r.StudentId == s.StudentId && r.NotifyDate == date)))
                {
                    Log.Add(new TbPushNotificationLog { SchoolId = schoolId, UserId = s.UserId, StudentId = s.StudentId, NotificationType = type, NotifyDate = date, Title = title, Body = body, Status = "PENDING" });
                    added++;
                }
                return Task.FromResult(added);
            }
            public Task<List<TbPushNotificationLog>> GetOpenLogRowsAsync(string schoolId, DateOnly date, string type, short maxAttempts, bool includeDryRun, CancellationToken ct) =>
                Task.FromResult(Log.Where(r => r.SchoolId == schoolId && r.NotifyDate == date &&
                    (r.Status == "PENDING" || (r.Status == "FAILED" && r.AttemptCount < maxAttempts) || (includeDryRun && r.Status == "DRY_RUN"))).ToList());
            public Task<List<ActiveDevice>> GetActiveDevicesAsync(string schoolId, IReadOnlyCollection<long> userIds, CancellationToken ct) =>
                Task.FromResult(userIds.Where(Devices.ContainsKey).Select(u => new ActiveDevice(u, u, Devices[u])).ToList());
            public Task<int> DeactivateTokensAsync(IReadOnlyCollection<string> tokens, CancellationToken ct) => Task.FromResult(0);
            public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(0);
            public void ClearTracking() { }

            // not used by the job
            public Task UpsertDeviceAsync(string schoolId, long userId, string token, string platform, string? label, string? appVersion, CancellationToken ct) => throw new NotSupportedException();
            public Task<int> DeactivateOwnDeviceAsync(long userId, string token, CancellationToken ct) => throw new NotSupportedException();
            public void AddSettings(TbSchoolNotificationSettings row) => throw new NotSupportedException();
            public Task<bool> StudentBelongsToSchoolAsync(string schoolId, string studentId, CancellationToken ct) => throw new NotSupportedException();
            public Task<List<TbPushNotificationLog>> GetLogAsync(string schoolId, string? studentId, DateOnly from, DateOnly to, int take, CancellationToken ct) => throw new NotSupportedException();
        }

        /// <summary>IStudentRepository has dozens of members and the job uses one: a proxy answers that one (everyone given counts as overdue) and refuses the rest.</summary>
        private class StudentStub : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? method, object?[]? args)
            {
                if (method?.Name != nameof(IStudentRepository.GetFeeAggregatesAsync)) throw new NotSupportedException(method?.Name);
                var ids = (IReadOnlyCollection<string>)args![2]!;
                return Task.FromResult(ids.ToDictionary(id => id, id => new StudentFeeAggregate(id, 1000m, 0m, 1)));
            }
        }

        private static (FeeReminderService Svc, MemRepo Repo, LineLog<FeeReminderService> Log, FakeFcm Fcm) Make(FixedClock clock, bool dryRun = false)
        {
            var repo = new MemRepo();
            var log = new LineLog<FeeReminderService>();
            var fcm = new FakeFcm();
            var students = DispatchProxy.Create<IStudentRepository, StudentStub>();
            return (new FeeReminderService(repo, students, fcm, clock, Options.Create(new FirebaseOptions { DryRun = dryRun }), log), repo, log, fcm);
        }

        private static void Enable(MemRepo repo, string school, DateOnly? lastRun = null) =>
            repo.Settings.Add(new TbSchoolNotificationSettings { SchoolId = school, FeeReminderEnabled = true, FeeReminderSendTime = new TimeOnly(9, 0), LastFeeReminderRunDate = lastRun });

        // ------------------------------------------------------------------ tests

        [Fact]
        public async Task OneInformationLinePerSchoolRun_WithTheSchoolAndAllFiveCounts()
        {
            var (svc, repo, log, fcm) = Make(new FixedClock(0, 9, 5));
            Enable(repo, School);
            repo.Overdue[School] = new() { new($"{SecretStudent}1", 1), new($"{SecretStudent}2", 2), new($"{SecretStudent}3", 3) };
            repo.Devices[1] = SecretToken + "-ok";   // delivered
            repo.Devices[2] = SecretToken + "-dead"; // refused for good
            // student 3 has no device
            fcm.Answer = m => m.Token.EndsWith("-dead") ? new FcmResult(FcmResultKind.InvalidToken, "UNREGISTERED") : new FcmResult(FcmResultKind.Success);

            await svc.RunTickAsync(default);

            var line = Assert.Single(log.Info);
            Assert.Equal($"Fee reminder run for school {School} (full): eligible=3 sent=1 failed=1 noDevice=1 dryRun=0", line);
        }

        [Fact]
        public async Task TheLineNeverCarriesNamesTokensOrMessageText()
        {
            var (svc, repo, log, _) = Make(new FixedClock(0, 9, 5));
            Enable(repo, School);
            repo.Overdue[School] = new() { new($"{SecretStudent}1", 1), new($"{SecretStudent}2", 2) };
            repo.Devices[1] = SecretToken;

            await svc.RunTickAsync(default);

            var title = repo.Log[0].Title; var body = repo.Log[0].Body;
            foreach (var entry in log.Entries)
            {
                Assert.DoesNotContain(SecretStudent, entry.Text);
                Assert.DoesNotContain(SecretToken, entry.Text);
                Assert.DoesNotContain(title, entry.Text);
                Assert.DoesNotContain(body, entry.Text);
            }
        }

        [Fact]
        public async Task DryRun_IsCountedSeparately_NeverAsSent()
        {
            var (svc, repo, log, _) = Make(new FixedClock(0, 9, 5), dryRun: true);
            Enable(repo, School);
            repo.Overdue[School] = new() { new("S1", 1), new("S2", 2), new("S3", 3) };
            repo.Devices[1] = "t1"; repo.Devices[2] = "t2";

            await svc.RunTickAsync(default);

            Assert.Equal($"Fee reminder run for school {School} (full): eligible=3 sent=0 failed=0 noDevice=1 dryRun=2", Assert.Single(log.Info));
        }

        [Fact]
        public async Task EachSchoolGetsItsOwnLine_WithItsOwnCounts()
        {
            var (svc, repo, log, _) = Make(new FixedClock(0, 9, 5));
            Enable(repo, School); Enable(repo, Other);
            repo.Overdue[School] = new() { new("A1", 1) };
            repo.Overdue[Other] = new() { new("B1", 2), new("B2", 3) };
            repo.Devices[1] = "ta"; repo.Devices[2] = "tb";

            await svc.RunTickAsync(default);

            Assert.Equal(2, log.Info.Count);
            Assert.Contains($"Fee reminder run for school {School} (full): eligible=1 sent=1 failed=0 noDevice=0 dryRun=0", log.Info);
            Assert.Contains($"Fee reminder run for school {Other} (full): eligible=2 sent=1 failed=0 noDevice=1 dryRun=0", log.Info);
        }

        [Fact]
        public async Task LaterTicksOfTheSameDay_WithNothingOpen_AddNoLine()
        {
            var clock = new FixedClock(0, 9, 5);
            var (svc, repo, log, _) = Make(clock);
            Enable(repo, School);
            repo.Overdue[School] = new() { new("S1", 1) };
            repo.Devices[1] = "t1";

            await svc.RunTickAsync(default);
            clock.At(0, 9, 10); await svc.RunTickAsync(default);
            clock.At(0, 15, 0); await svc.RunTickAsync(default);

            Assert.Single(log.Info); // not one line every five minutes all day
        }

        [Fact]
        public async Task ARetryOfOpenRows_GetsOneLineOfItsOwn_MarkedRetry()
        {
            var clock = new FixedClock(0, 9, 5);
            var (svc, repo, log, fcm) = Make(clock);
            Enable(repo, School);
            repo.Overdue[School] = new() { new("S1", 1) };
            repo.Devices[1] = "t1";
            fcm.Answer = _ => new FcmResult(FcmResultKind.Transient, "UNAVAILABLE"); // will retry

            await svc.RunTickAsync(default);
            clock.At(0, 9, 10);
            await svc.RunTickAsync(default);

            Assert.Equal(2, log.Info.Count);
            Assert.Equal($"Fee reminder run for school {School} (full): eligible=1 sent=0 failed=1 noDevice=0 dryRun=0", log.Info[0]);
            Assert.Equal($"Fee reminder run for school {School} (retry): eligible=1 sent=0 failed=1 noDevice=0 dryRun=0", log.Info[1]);
        }

        [Fact]
        public async Task ASchoolWithNobodyOverdue_StillLogsItsRun_WithZeros()
        {
            var (svc, repo, log, _) = Make(new FixedClock(0, 9, 5));
            Enable(repo, School);

            await svc.RunTickAsync(default);

            Assert.Equal($"Fee reminder run for school {School} (full): eligible=0 sent=0 failed=0 noDevice=0 dryRun=0", Assert.Single(log.Info));
        }
    }
}
