using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using smss_api.Controllers;
using smss_api.Helpers;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>A clock the tests move by hand (India times are given as hour / minute).</summary>
    internal sealed class FixedClock : TimeProvider
    {
        // 2026-11-19 00:00 India time = 2026-11-18 18:30 UTC
        public static readonly DateOnly Day1 = new(2026, 11, 19);
        private DateTime _utc;
        public FixedClock(int day, int hour, int minute) => At(day, hour, minute);
        public FixedClock At(int day, int hour, int minute) { _utc = new DateTime(2026, 11, 18, 18, 30, 0, DateTimeKind.Utc).AddDays(day).AddHours(hour).AddMinutes(minute); return this; }
        public override DateTimeOffset GetUtcNow() => new(_utc, TimeSpan.Zero);
    }

    /// <summary>The fake Firebase door: records every message, and answers per token. No Firebase credentials are ever needed.</summary>
    internal sealed class FakeFcm : IFcmSender
    {
        public bool Ready = true;
        public int DelayMs;
        public Func<FcmMessage, FcmResult> Answer = _ => new FcmResult(FcmResultKind.Success);
        private readonly List<FcmMessage> _sent = new();
        private int _calls;
        public IReadOnlyList<FcmMessage> Sent { get { lock (_sent) return _sent.ToList(); } }
        public int Calls => _calls;

        public bool TryEnsureReady(out string reason) { reason = Ready ? "" : "Firebase:CredentialsPath is not configured."; return Ready; }

        public async Task<IReadOnlyList<FcmResult>> SendAsync(IReadOnlyList<FcmMessage> messages, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            if (DelayMs > 0) await Task.Delay(DelayMs, ct);
            lock (_sent) _sent.AddRange(messages);
            return messages.Select(m => Answer(m)).ToList();
        }
    }

    /// <summary>
    /// Fee-overdue push reminders (Docs/push-notification-design.md section 10). Everything here runs on the scratch database with a fake IFcmSender and a hand-moved clock.
    /// The [DbFact] half needs SMSS_TEST_PG / SMSS_TEST_TEMPLATE (see TaxTestKit); the first half needs no database.
    /// </summary>
    public class PushNotificationTests
    {
        private const string SchoolA = TaxDb.SchoolA, SchoolB = TaxDb.SchoolB;

        // ------------------------------------------------------------------ plumbing

        private static FeeReminderService Svc(TaxDb db, IFcmSender fcm, TimeProvider clock, bool dryRun = false)
        {
            var c = db.Ctx();
            return new FeeReminderService(new NotificationRepository(c), new StudentRepository(c), fcm, clock,
                Options.Create(new FirebaseOptions { DryRun = dryRun }), NullLogger<FeeReminderService>.Instance);
        }

        private static DeviceService Devices(TaxDb db) => new(new NotificationRepository(db.Ctx()), NullLogger<DeviceService>.Instance);
        private static NotificationSettingsService Settings(TaxDb db) => new(new NotificationRepository(db.Ctx()), NullLogger<NotificationSettingsService>.Instance);
        private static NotificationLogService Log(TaxDb db) => new(new NotificationRepository(db.Ctx()), NullLogger<NotificationLogService>.Instance);

        private static List<object[]> Rows(TaxDb db, string sql)
        {
            var list = new List<object[]>();
            using var c = new NpgsqlConnection(db.Conn); c.Open();
            using var cmd = new NpgsqlCommand(sql, c);
            using var r = cmd.ExecuteReader();
            while (r.Read()) { var row = new object[r.FieldCount]; r.GetValues(row); list.Add(row); }
            return list;
        }

        /// <summary>School A students that have an Active Student login, in id order: (student id, user id).</summary>
        private static List<(string Student, long User)> LoginStudents(TaxDb db) =>
            Rows(db, $"select s.student_id, u.user_id from tb_students s join tb_users u on u.org_user_id = s.student_id and u.school_id = s.school_id and u.user_type = 'Student' " +
                     $"where s.school_id = '{SchoolA}' order by s.student_id").Select(r => ((string)r[0], (long)r[1])).ToList();

        /// <summary>Everybody in school A fully paid: a clean slate where each test then makes exactly the students it wants overdue.</summary>
        private static void PayAll(TaxDb db) => db.Exec($"update tb_student_fee_dues set amount_paid = amount_due where school_id = '{SchoolA}'");

        /// <summary>Leaves ONE installment of the student unpaid-ish: due on <paramref name="due"/>, with <paramref name="paid"/> paid of it (every other installment of the student stays paid).</summary>
        private static void SetDue(TaxDb db, string student, DateOnly due, decimal paid)
        {
            db.Exec($"update tb_student_fee_dues set due_date = '{due:yyyy-MM-dd}', amount_paid = {paid} " +
                    $"where fee_due_id = (select min(fee_due_id) from tb_student_fee_dues where student_id = '{student}')");
            db.Exec($"update tb_student_fee_dues set amount_due = 1000 where student_id = '{student}' and amount_paid = {paid} and due_date = '{due:yyyy-MM-dd}' and amount_due < 1000");
        }

        private static void Enable(TaxDb db, int grace = 0, string time = "09:00", string? lastRun = null, string school = SchoolA) =>
            db.Exec($"insert into tb_school_notification_settings (school_id, fee_reminder_enabled, fee_reminder_send_time, fee_reminder_grace_days, last_fee_reminder_run_date) " +
                    $"values ('{school}', true, '{time}', {grace}, {(lastRun == null ? "null" : $"'{lastRun}'")}) " +
                    $"on conflict (school_id) do update set fee_reminder_enabled = true, fee_reminder_send_time = '{time}', fee_reminder_grace_days = {grace}, last_fee_reminder_run_date = {(lastRun == null ? "null" : $"'{lastRun}'")}");

        private static Task Register(TaxDb db, long user, string token, string school = SchoolA) =>
            Devices(db).RegisterAsync(school, user, new RegisterDeviceRequest { FcmToken = token, Platform = "android" }, default);

        private static string? LogStatus(TaxDb db, string student, string? date = null) =>
            db.Scalar<string?>($"select status from tb_push_notification_log where student_id = '{student}'{(date == null ? "" : $" and notify_date = '{date}'")} order by notification_id desc limit 1");

        private static int LogCount(TaxDb db) => db.Scalar<int>("select count(*) from tb_push_notification_log");

        private static readonly DateOnly Yesterday = FixedClock.Day1.AddDays(-1);

        // ------------------------------------------------------------------ eligibility

        [DbFact]
        public async Task Eligibility_DueYesterdayIsOverdue_DueTodayIsNotYet_GraceZero()
        {
            using var db = new TaxDb(); PayAll(db);
            var s = LoginStudents(db);
            SetDue(db, s[0].Student, Yesterday, 0);
            SetDue(db, s[1].Student, FixedClock.Day1, 0);          // due today, grace 0: not yet overdue
            Enable(db, grace: 0);
            await Register(db, s[0].User, "tok-a"); await Register(db, s[1].User, "tok-b");

            var fcm = new FakeFcm();
            await Svc(db, fcm, new FixedClock(0, 9, 5)).RunTickAsync(default);

            Assert.Equal("SENT", LogStatus(db, s[0].Student));
            Assert.Null(LogStatus(db, s[1].Student));
            Assert.Equal(new[] { "tok-a" }, fcm.Sent.Select(m => m.Token).ToArray());
            Assert.Equal(1, LogCount(db));
        }

        [DbFact]
        public async Task Eligibility_GraceDaysBoundary()
        {
            using var db = new TaxDb(); PayAll(db);
            var s = LoginStudents(db);
            SetDue(db, s[0].Student, FixedClock.Day1.AddDays(-3), 0);   // due + 3 grace = today: not yet
            SetDue(db, s[1].Student, FixedClock.Day1.AddDays(-4), 0);   // due + 3 grace = yesterday: overdue
            Enable(db, grace: 3);
            await Svc(db, new FakeFcm(), new FixedClock(0, 9, 5)).RunTickAsync(default);
            Assert.Null(LogStatus(db, s[0].Student));
            Assert.NotNull(LogStatus(db, s[1].Student));
        }

        [DbFact]
        public async Task Preview_CountsWhoWouldBeRemindedAndWhoHasADevice_SendsAndWritesNothing()
        {
            using var db = new TaxDb(); PayAll(db);
            var s = LoginStudents(db);
            var today = smss_api_service_layer.helper.SchoolClock.Today();     // the preview always answers for the real today
            SetDue(db, s[0].Student, today.AddDays(-4), 0);                        // grace 3: overdue
            SetDue(db, s[1].Student, today.AddDays(-4), 0);
            SetDue(db, s[2].Student, today.AddDays(-3), 0);                        // grace 3: due + 3 = today, not yet
            Enable(db, grace: 3);
            await Register(db, s[0].User, "tok-a");
            var fcm = new FakeFcm();

            var p = (await Svc(db, fcm, new FixedClock(0, 9, 5)).PreviewAsync(SchoolA, default)).Data!;

            Assert.Equal((today, 3, 2, 1, 1), (p.Date, p.GraceDays, p.StudentsToRemind, p.WithDevice, p.WithoutDevice));
            Assert.Empty(fcm.Sent);
            Assert.Equal(0, LogCount(db));
        }

        [DbFact]
        public async Task Eligibility_PartialPaymentStillOverdue_FullPaymentStops_VoidMakesItEligibleAgain()
        {
            using var db = new TaxDb(); PayAll(db);
            var s = LoginStudents(db);
            var student = s[0];
            SetDue(db, student.Student, Yesterday, 400);              // 400 of 1000 paid: still overdue
            SetDue(db, s[1].Student, Yesterday, 1000);               // fully paid: not overdue
            Enable(db);
            await Register(db, student.User, "tok-a");
            var fcm = new FakeFcm();

            await Svc(db, fcm, new FixedClock(0, 9, 5)).RunTickAsync(default);
            Assert.Equal("SENT", LogStatus(db, student.Student, "2026-11-19"));
            Assert.Null(LogStatus(db, s[1].Student));

            // the family pays the remaining 600 (a real payment through the Fee module), so day 2 sends nothing for the student
            var paid = await db.Pay(student.Student, 600);
            Assert.True(paid.Status, paid.Message);
            await Svc(db, fcm, new FixedClock(1, 9, 5)).RunTickAsync(default);
            Assert.Null(LogStatus(db, student.Student, "2026-11-20"));

            // the payment is voided: the installment is overdue again, so day 3 reminds again (no extra code: overdue is derived)
            var v = await db.Payments().VoidPaymentAsync(SchoolA, paid.Data!.PaymentId, TaxDb.UserA, new VoidPaymentRequest { VoidReason = "entered by mistake" }, default);
            Assert.Equal(200, v.StatusCode);
            await Svc(db, fcm, new FixedClock(2, 9, 5)).RunTickAsync(default);
            Assert.Equal("SENT", LogStatus(db, student.Student, "2026-11-21"));
        }

        [DbFact]
        public async Task Eligibility_InactiveStudentInactiveEnrollmentInactiveLoginNoLoginNotEnrolled_AreAllExcluded()
        {
            using var db = new TaxDb(); PayAll(db);
            var s = LoginStudents(db);
            foreach (var x in s.Take(6)) SetDue(db, x.Student, Yesterday, 0);
            db.Exec($"update tb_students set status_id = 2 where student_id = '{s[1].Student}'");                       // inactive student
            db.Exec($"update tb_student_enrollments set status_id = 2 where student_id = '{s[2].Student}'");            // inactive enrollment
            db.Exec($"update tb_users set status_id = 2 where org_user_id = '{s[3].Student}' and user_type = 'Student'"); // login blocked
            db.Exec($"delete from tb_users where org_user_id = '{s[4].Student}' and user_type = 'Student'");             // no login at all
            db.Exec($"update tb_academic_years set is_current = false where school_id = '{SchoolA}'");                    // temporarily: nothing current...
            db.Exec($"update tb_academic_years set is_current = true where academic_year_id = {TaxDb.YearA}");
            db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) select school_id, 'Other', '2025-04-01', '2026-03-31', false, status_id from tb_academic_years where academic_year_id = {TaxDb.YearA}");
            var other = db.Scalar<long>($"select max(academic_year_id) from tb_academic_years where school_id = '{SchoolA}'");
            db.Exec($"update tb_student_enrollments set academic_year_id = {other} where student_id = '{s[5].Student}'"); // enrolled in another year only
            Enable(db);
            foreach (var x in s.Take(6)) await Register(db, x.User, "tok-" + x.Student);

            await Svc(db, new FakeFcm(), new FixedClock(0, 9, 5)).RunTickAsync(default);

            Assert.NotNull(LogStatus(db, s[0].Student));
            foreach (var i in new[] { 1, 2, 3, 4, 5 }) Assert.Null(LogStatus(db, s[i].Student));
        }

        // ------------------------------------------------------------------ the tick: send time, catch-up, disabled schools

        [DbFact]
        public async Task Catch_up_TickBeforeSendTimeDoesNothing_TickAfterRuns_NoBackfillOfMissedDays()
        {
            using var db = new TaxDb(); PayAll(db);
            var s = LoginStudents(db);
            SetDue(db, s[0].Student, Yesterday, 0);
            Enable(db, time: "09:00", lastRun: "2026-11-17");        // ran on the 17th; the API was down on the 18th
            await Register(db, s[0].User, "tok-a");
            var fcm = new FakeFcm();

            var early = await Svc(db, fcm, new FixedClock(0, 8, 55)).RunTickAsync(default);
            Assert.Equal(0, early.SchoolsRun);
            Assert.Equal(0, LogCount(db));

            var late = await Svc(db, fcm, new FixedClock(0, 9, 5)).RunTickAsync(default);   // the first tick after 09:00 is the catch-up
            Assert.Equal(1, late.SchoolsRun);
            Assert.Equal(1, LogCount(db));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_push_notification_log where notify_date = '2026-11-18'"));   // the missed 18th is not back-filled
            Assert.Equal("2026-11-19", db.Scalar<string>($"select last_fee_reminder_run_date::text from tb_school_notification_settings where school_id = '{SchoolA}'"));
        }

        [DbFact]
        public async Task DisabledSchoolOrNoSettingsRow_IsNeverTouched()
        {
            using var db = new TaxDb(); PayAll(db);
            var s = LoginStudents(db);
            SetDue(db, s[0].Student, Yesterday, 0);
            await Register(db, s[0].User, "tok-a");
            var fcm = new FakeFcm();
            var noRow = await Svc(db, fcm, new FixedClock(0, 10, 0)).RunTickAsync(default);       // default = OFF
            Enable(db); db.Exec($"update tb_school_notification_settings set fee_reminder_enabled = false where school_id = '{SchoolA}'");
            var off = await Svc(db, fcm, new FixedClock(0, 10, 0)).RunTickAsync(default);
            Assert.Equal((0, 0), (noRow.SchoolsChecked, off.SchoolsChecked));
            Assert.Empty(fcm.Sent);
            Assert.Equal(0, LogCount(db));
        }

        // ------------------------------------------------------------------ idempotency and concurrency

        [DbFact]
        public async Task Idempotency_TwoRunsTheSameDay_OneLogRowAndOnePushPerStudent()
        {
            using var db = new TaxDb(); PayAll(db);
            var s = LoginStudents(db);
            foreach (var x in s.Take(3)) { SetDue(db, x.Student, Yesterday, 0); await Register(db, x.User, "tok-" + x.Student); }
            Enable(db);
            var fcm = new FakeFcm();

            await Svc(db, fcm, new FixedClock(0, 9, 5)).RunTickAsync(default);
            await Svc(db, fcm, new FixedClock(0, 9, 10)).RunTickAsync(default);          // the next tick: already run today
            Assert.Equal(3, fcm.Sent.Count);
            Assert.Equal(3, LogCount(db));

            // even a forced second FULL run (settings reset, as after a crash before the date was saved) sends nothing twice: the unique index + status keep it once
            db.Exec($"update tb_school_notification_settings set last_fee_reminder_run_date = null where school_id = '{SchoolA}'");
            await Svc(db, fcm, new FixedClock(0, 9, 15)).RunTickAsync(default);
            Assert.Equal(3, fcm.Sent.Count);
            Assert.Equal(3, LogCount(db));
        }

        [DbFact]
        public async Task Idempotency_UniqueIndexIsTheBackstop_ADuplicateRowCannotBeWritten()
        {
            using var db = new TaxDb();
            var s = LoginStudents(db);
            var repo = new NotificationRepository(db.Ctx());
            var one = new[] { new smss_api_db_layer.@interface.EligibleStudent(s[0].Student, s[0].User) };
            var date = FixedClock.Day1;
            Assert.Equal(1, await repo.CreateLogRowsAsync(SchoolA, date, "FEE_OVERDUE", "t", "b", one, default));
            Assert.Equal(0, await repo.CreateLogRowsAsync(SchoolA, date, "FEE_OVERDUE", "t", "b", one, default));   // ON CONFLICT DO NOTHING
            var ex = Assert.Throws<PostgresException>(() => db.Exec(
                $"insert into tb_push_notification_log (school_id, student_id, notification_type, notify_date, title, body) values ('{SchoolA}', '{s[0].Student}', 'FEE_OVERDUE', '2026-11-19', 't', 'b')"));
            Assert.Equal("23505", ex.SqlState);
        }

        [DbFact]
        public async Task Idempotency_TwoConcurrentRuns_SendOnce_TheAdvisoryLockKeepsTheOtherOut()
        {
            using var db = new TaxDb(); PayAll(db);
            var s = LoginStudents(db);
            foreach (var x in s.Take(4)) { SetDue(db, x.Student, Yesterday, 0); await Register(db, x.User, "tok-" + x.Student); }
            Enable(db);
            var fcm = new FakeFcm { DelayMs = 800 };

            var a = Svc(db, fcm, new FixedClock(0, 9, 5)).RunTickAsync(default);
            var b = Svc(db, fcm, new FixedClock(0, 9, 5)).RunTickAsync(default);
            var results = await Task.WhenAll(a, b);

            Assert.Equal(1, results.Sum(r => r.SchoolsRun));
            Assert.Equal(1, results.Sum(r => r.SchoolsSkippedLocked));
            Assert.Equal(4, fcm.Sent.Count);
            Assert.Equal(4, LogCount(db));
        }

        // ------------------------------------------------------------------ devices and send results

        [DbFact]
        public async Task Devices_ATokenReassignedToANewUserStopsGoingToTheOldUser()
        {
            using var db = new TaxDb(); PayAll(db);
            var s = LoginStudents(db);
            SetDue(db, s[0].Student, Yesterday, 0); SetDue(db, s[1].Student, Yesterday, 0);
            Enable(db);
            await Register(db, s[0].User, "shared-phone");
            await Register(db, s[1].User, "shared-phone");     // the other student signs in on the same phone
            Assert.Equal(1, db.Scalar<int>("select count(*) from tb_user_devices where fcm_token = 'shared-phone'"));
            Assert.Equal(s[1].User, db.Scalar<long>("select user_id from tb_user_devices where fcm_token = 'shared-phone'"));

            var fcm = new FakeFcm();
            await Svc(db, fcm, new FixedClock(0, 9, 5)).RunTickAsync(default);

            Assert.Equal("NO_DEVICE", LogStatus(db, s[0].Student));    // the old user no longer has the phone
            Assert.Equal("SENT", LogStatus(db, s[1].Student));
            Assert.Single(fcm.Sent);
        }

        [DbFact]
        public async Task Devices_UnregisterByANonOwnerIsANoOp_ByTheOwnerDeactivates()
        {
            using var db = new TaxDb();
            var s = LoginStudents(db);
            await Register(db, s[0].User, "tok-own");
            var stranger = await Devices(db).UnregisterAsync(SchoolA, s[1].User, new UnregisterDeviceRequest { FcmToken = "tok-own" }, default);
            Assert.Equal(200, stranger.StatusCode);                                                       // same answer: no hint whether the token exists
            Assert.True(db.Scalar<bool>("select is_active from tb_user_devices where fcm_token = 'tok-own'"));
            var owner = await Devices(db).UnregisterAsync(SchoolA, s[0].User, new UnregisterDeviceRequest { FcmToken = "tok-own" }, default);
            Assert.Equal(200, owner.StatusCode);
            Assert.False(db.Scalar<bool>("select is_active from tb_user_devices where fcm_token = 'tok-own'"));
            await Register(db, s[0].User, "tok-own");                                                       // signing in again re-activates it
            Assert.True(db.Scalar<bool>("select is_active from tb_user_devices where fcm_token = 'tok-own'"));
        }

        [DbFact]
        public async Task Devices_ADeadTokenIsDeactivated_AndTheRowFails()
        {
            using var db = new TaxDb(); PayAll(db);
            var s = LoginStudents(db);
            SetDue(db, s[0].Student, Yesterday, 0);
            Enable(db);
            await Register(db, s[0].User, "dead-token");
            var fcm = new FakeFcm { Answer = _ => new FcmResult(FcmResultKind.InvalidToken, "Unregistered") };

            await Svc(db, fcm, new FixedClock(0, 9, 5)).RunTickAsync(default);

            Assert.Equal("FAILED", LogStatus(db, s[0].Student));
            Assert.False(db.Scalar<bool>("select is_active from tb_user_devices where fcm_token = 'dead-token'"));
            Assert.Equal(3, db.Scalar<int>($"select attempt_count from tb_push_notification_log where student_id = '{s[0].Student}'"));   // permanent: not retried
            await Svc(db, fcm, new FixedClock(0, 9, 10)).RunTickAsync(default);
            Assert.Single(fcm.Sent);                                                                          // nothing more today
        }

        [DbFact]
        public async Task SendResults_TransientFailureIsRetriedOnLaterTicks_ThenStopsAtThreeAttempts()
        {
            using var db = new TaxDb(); PayAll(db);
            var s = LoginStudents(db);
            SetDue(db, s[0].Student, Yesterday, 0); SetDue(db, s[1].Student, Yesterday, 0);
            Enable(db);
            await Register(db, s[0].User, "tok-flaky"); await Register(db, s[1].User, "tok-ok");
            var fcm = new FakeFcm { Answer = m => m.Token == "tok-flaky" ? new FcmResult(FcmResultKind.Transient, "Unavailable") : new FcmResult(FcmResultKind.Success) };

            await Svc(db, fcm, new FixedClock(0, 9, 5)).RunTickAsync(default);
            Assert.Equal(("FAILED", "SENT"), (LogStatus(db, s[0].Student), LogStatus(db, s[1].Student)));
            Assert.True(db.Scalar<bool>("select is_active from tb_user_devices where fcm_token = 'tok-flaky'"));   // a transient error never kills a token

            var retry = await Svc(db, fcm, new FixedClock(0, 9, 10)).RunTickAsync(default);                          // 2nd attempt, same day
            Assert.Equal(1, retry.SchoolsRetried);
            await Svc(db, fcm, new FixedClock(0, 9, 15)).RunTickAsync(default);                                      // 3rd attempt
            await Svc(db, fcm, new FixedClock(0, 9, 20)).RunTickAsync(default);                                      // capped: no 4th
            Assert.Equal(3, fcm.Sent.Count(m => m.Token == "tok-flaky"));
            Assert.Equal(1, fcm.Sent.Count(m => m.Token == "tok-ok"));                                              // the delivered one is never sent again

            fcm.Answer = _ => new FcmResult(FcmResultKind.Success);                                                    // and recovery inside the cap works
            db.Exec($"update tb_push_notification_log set attempt_count = 1 where student_id = '{s[0].Student}'");
            await Svc(db, fcm, new FixedClock(0, 9, 25)).RunTickAsync(default);
            Assert.Equal("SENT", LogStatus(db, s[0].Student));
        }

        [DbFact]
        public async Task SendResults_NoDeviceIsLogged_AndOneMessagePerDevice_WithNeutralTextAndNoStudentData()
        {
            using var db = new TaxDb(); PayAll(db);
            var s = LoginStudents(db);
            SetDue(db, s[0].Student, Yesterday, 0); SetDue(db, s[1].Student, Yesterday, 0);
            Enable(db);
            await Register(db, s[0].User, "phone-1"); await Register(db, s[0].User, "phone-2");   // one student, two phones: one log row, two messages
            var fcm = new FakeFcm();

            await Svc(db, fcm, new FixedClock(0, 9, 5)).RunTickAsync(default);

            Assert.Equal("NO_DEVICE", LogStatus(db, s[1].Student));
            Assert.Equal(2, fcm.Sent.Count);
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_push_notification_log where student_id = '{s[0].Student}'"));
            Assert.Equal(2, db.Scalar<int>($"select devices_succeeded from tb_push_notification_log where student_id = '{s[0].Student}'"));
            Assert.All(fcm.Sent, m => { Assert.Equal("FEE_OVERDUE", m.DataType); Assert.Equal(FeeReminderService.Title, m.Title); Assert.Equal(FeeReminderService.Body, m.Body); });
            Assert.DoesNotContain(fcm.Sent, m => m.Body.Any(char.IsDigit) || m.Title.Any(char.IsDigit));        // no amount, no id on a lock screen
        }

        [DbFact]
        public async Task DryRun_WritesDRY_RUN_NeverSENT_AndIsNotRepeatedByAnotherDryTick()
        {
            using var db = new TaxDb(); PayAll(db);
            var s = LoginStudents(db);
            SetDue(db, s[0].Student, Yesterday, 0); Enable(db);
            await Register(db, s[0].User, "tok-a");
            var fcm = new FakeFcm();
            var r = await Svc(db, fcm, new FixedClock(0, 9, 5), dryRun: true).RunTickAsync(default);
            Assert.Equal(0, r.Sent);
            Assert.Equal("DRY_RUN", LogStatus(db, s[0].Student));
            Assert.Null(db.Scalar<string?>($"select sent_at::text from tb_push_notification_log where student_id = '{s[0].Student}'"));
            Assert.Contains("Dry run", db.Scalar<string>($"select error_summary from tb_push_notification_log where student_id = '{s[0].Student}'"));
            await Svc(db, fcm, new FixedClock(0, 9, 10), dryRun: true).RunTickAsync(default);
            Assert.Single(fcm.Sent);                                    // a second dry tick does not validate the same row again
        }

        [DbFact]
        public async Task DryRunThenRealRun_TheSameDay_SendsOnce_UpdatingTheRowInPlace()
        {
            using var db = new TaxDb(); PayAll(db);
            var s = LoginStudents(db);
            SetDue(db, s[0].Student, Yesterday, 0); Enable(db);
            await Register(db, s[0].User, "tok-a");
            var dry = new FakeFcm(); var real = new FakeFcm();

            await Svc(db, dry, new FixedClock(0, 9, 5), dryRun: true).RunTickAsync(default);
            var id = db.Scalar<long>($"select notification_id from tb_push_notification_log where student_id = '{s[0].Student}'");
            Assert.Equal("DRY_RUN", LogStatus(db, s[0].Student));

            var r = await Svc(db, real, new FixedClock(0, 9, 10), dryRun: false).RunTickAsync(default);
            Assert.Equal(1, r.SchoolsRetried);
            Assert.Equal("SENT", LogStatus(db, s[0].Student));
            Assert.Single(real.Sent);
            Assert.Equal(1, LogCount(db));                                                   // same row, unique index intact
            Assert.Equal(id, db.Scalar<long>($"select notification_id from tb_push_notification_log where student_id = '{s[0].Student}'"));
            Assert.NotNull(db.Scalar<string?>($"select sent_at::text from tb_push_notification_log where student_id = '{s[0].Student}'"));
            Assert.Null(db.Scalar<string?>($"select error_summary from tb_push_notification_log where student_id = '{s[0].Student}'"));

            await Svc(db, real, new FixedClock(0, 9, 15), dryRun: false).RunTickAsync(default);
            Assert.Single(real.Sent);                                                          // and never a second real push
        }

        [DbFact]
        public async Task RealRunThenDryRun_NeverOverwritesASentRow()
        {
            using var db = new TaxDb(); PayAll(db);
            var s = LoginStudents(db);
            SetDue(db, s[0].Student, Yesterday, 0); Enable(db);
            await Register(db, s[0].User, "tok-a");
            var real = new FakeFcm(); var dry = new FakeFcm();

            await Svc(db, real, new FixedClock(0, 9, 5), dryRun: false).RunTickAsync(default);
            var q = $"select status, sent_at::text, attempt_count::text, devices_succeeded::text from tb_push_notification_log where student_id = '{s[0].Student}'";
            var before = Rows(db, q)[0];
            Assert.Equal("SENT", before[0]);

            await Svc(db, dry, new FixedClock(0, 9, 10), dryRun: true).RunTickAsync(default);                   // retry pass in dry mode
            db.Exec($"update tb_school_notification_settings set last_fee_reminder_run_date = null where school_id = '{SchoolA}'");
            await Svc(db, dry, new FixedClock(0, 9, 15), dryRun: true).RunTickAsync(default);                   // even a forced full run in dry mode

            Assert.Equal(before, Rows(db, q)[0]);
            Assert.Empty(dry.Sent);
            Assert.Equal(1, LogCount(db));
        }

        // ------------------------------------------------------------------ failure containment

        [DbFact]
        public async Task NotReady_NoCredentials_OneErrorPerRun_NothingSent_NothingWritten_NoThrow()
        {
            using var db = new TaxDb(); PayAll(db);
            var s = LoginStudents(db);
            SetDue(db, s[0].Student, Yesterday, 0); Enable(db);
            await Register(db, s[0].User, "tok-a");
            var fcm = new FakeFcm { Ready = false };
            var r = await Svc(db, fcm, new FixedClock(0, 9, 5)).RunTickAsync(default);
            Assert.True(r.NotReady);
            Assert.Equal(0, fcm.Calls);
            Assert.Equal(0, LogCount(db));
            Assert.Null(db.Scalar<string?>($"select last_fee_reminder_run_date::text from tb_school_notification_settings where school_id = '{SchoolA}'"));   // not marked as run: it will run once credentials exist
        }

        [DbFact]
        public async Task OneSchoolFailing_NeverStopsTheOthers()
        {
            using var db = new TaxDb(); PayAll(db);
            var a = LoginStudents(db);
            SetDue(db, a[0].Student, Yesterday, 0);
            const string studentB = "APS001ST001";
            db.Exec($"update tb_student_fee_dues set due_date = '2026-11-18', amount_paid = 0 where fee_due_id = (select min(fee_due_id) from tb_student_fee_dues where student_id = '{studentB}')");
            var userB = db.Scalar<long>($"select user_id from tb_users where org_user_id = '{studentB}' and user_type = 'Student'");
            Enable(db, school: SchoolB); Enable(db);
            await Register(db, a[0].User, "tok-a"); await Register(db, userB, "tok-b", SchoolB);
            // school B is processed first (APS... sorts before GHS...) and its send blows up
            var fcm = new FakeFcm { Answer = m => m.Token == "tok-b" ? throw new InvalidOperationException("boom") : new FcmResult(FcmResultKind.Success) };

            var r = await Svc(db, fcm, new FixedClock(0, 9, 5)).RunTickAsync(default);

            Assert.Equal((1, 1), (r.SchoolsFailed, r.SchoolsRun));
            Assert.Equal("SENT", LogStatus(db, a[0].Student));                         // school A still got its reminders
            Assert.Equal("PENDING", LogStatus(db, studentB));                          // school B's row waits for the next tick
            Assert.Null(db.Scalar<string?>($"select last_fee_reminder_run_date::text from tb_school_notification_settings where school_id = '{SchoolB}'"));
        }

        // ------------------------------------------------------------------ the session-level advisory lock

        private static int AdvisoryLocks(TaxDb db) =>
            db.Scalar<int>("select count(*) from pg_locks where locktype = 'advisory' and database = (select oid from pg_database where datname = current_database())");

        private static async Task<bool> CanLockAgain(TaxDb db)
        {
            var repo = new NotificationRepository(db.Ctx());
            var got = await repo.TryLockSchoolRunAsync(SchoolA, default);
            if (got) Assert.True(await repo.UnlockSchoolRunAsync(SchoolA));
            return got;
        }

        private static async Task<int> BackendPid(smss_api_db_layer.context.dbContext ctx) =>
            (await ctx.Database.SqlQuery<int>($"select pg_backend_pid() as \"Value\"").ToListAsync())[0];

        [DbFact]
        public async Task Lock_IsTakenAndReleasedOnTheSameDedicatedConnection()
        {
            using var db = new TaxDb();
            var ctx = db.Ctx();
            var repo = new NotificationRepository(ctx);
            Assert.True(await repo.TryLockSchoolRunAsync(SchoolA, default));
            // while held, the context's own connection is the one holding it: same backend pid as the server reports for the lock
            Assert.Equal(await BackendPid(ctx), db.Scalar<int>("select pid from pg_locks where locktype = 'advisory' and granted and database = (select oid from pg_database where datname = current_database()) limit 1"));   // THIS test's database only: pg_locks is cluster-wide and parallel tests hold advisory locks in theirs
            Assert.Equal(1, AdvisoryLocks(db));
            Assert.False(await new NotificationRepository(db.Ctx()).TryLockSchoolRunAsync(SchoolA, default));       // another session is refused
            Assert.True(await repo.UnlockSchoolRunAsync(SchoolA));                                                   // unlock found the lock on that same connection
            Assert.Equal(0, AdvisoryLocks(db));
            Assert.False(await repo.UnlockSchoolRunAsync(SchoolA));                                                  // nothing left to release
            Assert.True(await CanLockAgain(db));
        }

        [DbFact]
        public async Task Lock_AfterARunThatThrows_TheSchoolCanBeLockedAgain()
        {
            using var db = new TaxDb(); PayAll(db);
            var s = LoginStudents(db);
            SetDue(db, s[0].Student, Yesterday, 0); Enable(db);
            await Register(db, s[0].User, "tok-a");
            var fcm = new FakeFcm { Answer = _ => throw new InvalidOperationException("boom mid-run") };
            var r = await Svc(db, fcm, new FixedClock(0, 9, 5)).RunTickAsync(default);
            Assert.Equal(1, r.SchoolsFailed);
            Assert.Equal(0, AdvisoryLocks(db));                       // the pooled connection went back clean
            Assert.True(await CanLockAgain(db));
        }

        [DbFact]
        public async Task Lock_AfterACancelledRun_TheSchoolCanBeLockedAgain()
        {
            using var db = new TaxDb(); PayAll(db);
            var s = LoginStudents(db);
            SetDue(db, s[0].Student, Yesterday, 0); Enable(db);
            await Register(db, s[0].User, "tok-a");
            var fcm = new FakeFcm { DelayMs = 10_000 };
            using var cts = new CancellationTokenSource(); cts.CancelAfter(400);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Svc(db, fcm, new FixedClock(0, 9, 5)).RunTickAsync(cts.Token));
            Assert.Equal(0, AdvisoryLocks(db));
            Assert.True(await CanLockAgain(db));
        }

        [DbFact]
        public async Task Lock_IfTheConnectionDiesMidRun_UnlockDoesNotThrow_AndTheLockIsFree()
        {
            using var db = new TaxDb();
            var ctx = db.Ctx();
            var repo = new NotificationRepository(ctx);
            Assert.True(await repo.TryLockSchoolRunAsync(SchoolA, default));
            var pid = await BackendPid(ctx);
            db.Exec($"select pg_terminate_backend({pid})");
            await Task.Delay(300);
            await repo.UnlockSchoolRunAsync(SchoolA);                 // must not throw, and must not leave a broken connection in the pool
            Assert.Equal(0, AdvisoryLocks(db));
            Assert.True(await CanLockAgain(db));
        }

        // ------------------------------------------------------------------ cross-tenant

        [DbFact]
        public async Task CrossTenant_SchoolAJobNeverReadsSchoolBStudentsOrTokens()
        {
            using var db = new TaxDb(); PayAll(db);
            var a = LoginStudents(db);
            SetDue(db, a[0].Student, Yesterday, 0);
            Enable(db);                                                             // only school A has reminders on
            await Register(db, a[0].User, "tok-a");
            // school B: a student with a login, a device and an overdue installment, but reminders OFF for B
            var b = Rows(db, $"select s.student_id, u.user_id from tb_students s join tb_users u on u.org_user_id = s.student_id and u.user_type = 'Student' where s.school_id = '{SchoolB}' limit 1");
            var fcm = new FakeFcm();
            await Svc(db, fcm, new FixedClock(0, 9, 5)).RunTickAsync(default);
            Assert.Equal(new[] { "tok-a" }, fcm.Sent.Select(m => m.Token).ToArray());
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_push_notification_log where school_id <> '{SchoolA}'"));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_push_notification_log l join tb_students s on s.student_id = l.student_id where s.school_id <> l.school_id"));

            // a token registered for a school-B user is not reachable from school A's device query
            if (b.Count == 1)
            {
                await Register(db, (long)b[0][1], "tok-b", SchoolB);
                var seen = await new NotificationRepository(db.Ctx()).GetActiveDevicesAsync(SchoolA, new[] { (long)b[0][1] }, default);
                Assert.Empty(seen);
            }
        }

        // ------------------------------------------------------------------ authorization (controllers, real services on the scratch DB)

        [DbFact]
        public async Task Authorization_DeviceEndpoints_WorkForAnySchoolUser_TheSchoolComesFromTheToken()
        {
            using var db = new TaxDb();
            var s = LoginStudents(db);
            foreach (var role in new[] { "Student", "Teacher", "School Admin", "Accountant" })
            {
                var c = Fake.WithUser(new DeviceController(Devices(db)), Fake.User(SchoolA, s[0].User, role));
                Assert.Equal(200, Fake.Code(await c.Register(new RegisterDeviceRequest { FcmToken = "t-" + role, Platform = "IOS" }, default)));
            }
            Assert.Equal(4, db.Scalar<int>($"select count(*) from tb_user_devices where school_id = '{SchoolA}' and user_id = {s[0].User}"));
            // an account with no school claim is refused; a bad platform / empty token is a 400
            var noSchool = Fake.WithUser(new DeviceController(Devices(db)), Fake.User(null, s[0].User, "Student"));
            Assert.Equal(400, Fake.Code(await noSchool.Register(new RegisterDeviceRequest { FcmToken = "x", Platform = "IOS" }, default)));
            var ok = Fake.WithUser(new DeviceController(Devices(db)), Fake.User(SchoolA, s[0].User, "Student"));
            Assert.Equal(400, Fake.Code(await ok.Register(new RegisterDeviceRequest { FcmToken = "x", Platform = "WINDOWS" }, default)));
            Assert.Equal(400, Fake.Code(await ok.Register(new RegisterDeviceRequest { FcmToken = "  ", Platform = "IOS" }, default)));
            // the request type has no school or user field at all: nothing a client sends can change where the token lands
            Assert.DoesNotContain(typeof(RegisterDeviceRequest).GetProperties(), p => p.Name.Contains("School", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("User", StringComparison.OrdinalIgnoreCase));
        }

        [DbFact]
        public async Task Authorization_SettingsPreviewAndLog_AreSchoolAdminOnly()
        {
            using var db = new TaxDb();
            var s = LoginStudents(db);
            foreach (var role in new[] { "Student", "Teacher", "Accountant", "Principal", "Guardian", "Admission Staff", "Super Admin", "Driver" })
            {
                var user = Fake.User(SchoolA, s[0].User, role);
                var settings = Fake.WithUser(new NotificationSettingsController(Settings(db), Svc(db, new FakeFcm(), new FixedClock(0, 9, 5))), user);
                Assert.Equal(403, Fake.Code(await settings.Get(default)));
                Assert.Equal(403, Fake.Code(await settings.Save(new NotificationSettingsRequest { FeeReminderEnabled = true, FeeReminderSendTime = "09:00" }, default)));
                Assert.Equal(403, Fake.Code(await settings.Preview(default)));
                var log = Fake.WithUser(new NotificationLogController(Log(db)), user);
                Assert.Equal(403, Fake.Code(await log.List(null, null, null, default)));
            }
            var admin = Fake.User(SchoolA, TaxDb.UserA, "School Admin");
            var sc = Fake.WithUser(new NotificationSettingsController(Settings(db), Svc(db, new FakeFcm(), new FixedClock(0, 9, 5))), admin);
            Assert.Equal(200, Fake.Code(await sc.Get(default)));
            Assert.Equal(200, Fake.Code(await sc.Save(new NotificationSettingsRequest { FeeReminderEnabled = true, FeeReminderSendTime = "10:30", FeeReminderGraceDays = 2 }, default)));
            Assert.Equal(200, Fake.Code(await sc.Preview(default)));
            Assert.Equal(200, Fake.Code(await Fake.WithUser(new NotificationLogController(Log(db)), admin).List(null, null, null, default)));
            Assert.Equal(("t", "10:30:00", (short)2, (long)TaxDb.UserA), (db.Scalar<bool>($"select fee_reminder_enabled from tb_school_notification_settings where school_id = '{SchoolA}'") ? "t" : "f",
                db.Scalar<string>($"select fee_reminder_send_time::text from tb_school_notification_settings where school_id = '{SchoolA}'"),
                db.Scalar<short>($"select fee_reminder_grace_days from tb_school_notification_settings where school_id = '{SchoolA}'"),
                db.Scalar<long>($"select updated_by from tb_school_notification_settings where school_id = '{SchoolA}'")));
        }

        [DbFact]
        public async Task Authorization_SettingsValidation_AndEachSchoolSeesOnlyItsOwnRow()
        {
            using var db = new TaxDb();
            var admin = Fake.User(SchoolA, TaxDb.UserA, "School Admin");
            var sc = Fake.WithUser(new NotificationSettingsController(Settings(db), Svc(db, new FakeFcm(), new FixedClock(0, 9, 5))), admin);
            Assert.Equal(400, Fake.Code(await sc.Save(new NotificationSettingsRequest { FeeReminderSendTime = "25:00" }, default)));
            Assert.Equal(400, Fake.Code(await sc.Save(new NotificationSettingsRequest { FeeReminderSendTime = "9am" }, default)));
            Assert.Equal(400, Fake.Code(await sc.Save(new NotificationSettingsRequest { FeeReminderSendTime = "09:00", FeeReminderGraceDays = 31 }, default)));
            Assert.Equal(400, Fake.Code(await sc.Save(new NotificationSettingsRequest { FeeReminderSendTime = "09:00", FeeReminderGraceDays = -1 }, default)));
            Assert.Equal(200, Fake.Code(await sc.Save(new NotificationSettingsRequest { FeeReminderEnabled = true, FeeReminderSendTime = "08:15", FeeReminderGraceDays = 5 }, default)));

            var other = Fake.WithUser(new NotificationSettingsController(Settings(db), Svc(db, new FakeFcm(), new FixedClock(0, 9, 5))), Fake.User(SchoolB, TaxDb.UserB, "School Admin"));
            var seen = (ApiResponse<NotificationSettingsDto>)((ObjectResult)await other.Get(default)).Value!;
            Assert.False(seen.Data!.FeeReminderEnabled);                       // school B has its own (default) settings, not A's
            Assert.Equal("09:00", seen.Data.FeeReminderSendTime);
        }

        [DbFact]
        public async Task Authorization_Log_StudentOfAnotherSchoolIs404_AndRowsAreScopedToTheSchool()
        {
            using var db = new TaxDb(); PayAll(db);
            var s = LoginStudents(db);
            SetDue(db, s[0].Student, Yesterday, 0); Enable(db);
            await Register(db, s[0].User, "tok-a");
            await Svc(db, new FakeFcm(), new FixedClock(0, 9, 5)).RunTickAsync(default);

            var adminA = Fake.WithUser(new NotificationLogController(Log(db)), Fake.User(SchoolA, TaxDb.UserA, "School Admin"));
            var from = new DateOnly(2026, 11, 1); var to = new DateOnly(2026, 11, 30);
            var own = (ApiResponse<List<PushLogItemDto>>)((ObjectResult)await adminA.List(s[0].Student, from, to, default)).Value!;
            Assert.Single(own.Data!);
            Assert.Equal(("SENT", "FEE_OVERDUE"), (own.Data![0].Status, own.Data[0].NotificationType));

            var adminB = Fake.WithUser(new NotificationLogController(Log(db)), Fake.User(SchoolB, TaxDb.UserB, "School Admin"));
            Assert.Equal(404, Fake.Code(await adminB.List(s[0].Student, from, to, default)));                 // another school's student
            var all = (ApiResponse<List<PushLogItemDto>>)((ObjectResult)await adminB.List(null, from, to, default)).Value!;
            Assert.Empty(all.Data!);                                                                          // and school B's own log holds none of A's rows
            Assert.Equal(400, Fake.Code(await adminA.List(null, new DateOnly(2026, 1, 1), new DateOnly(2026, 11, 30), default)));   // more than 92 days
            Assert.Equal(400, Fake.Code(await adminA.List(null, to, from, default)));
        }

        // ------------------------------------------------------------------ no database needed

        [Fact]
        public void Firebase_DryRunDefaultsToTrue_InCodeAndInEveryAppsettingsFile_AndNoCredentialsPathIsCommitted()
        {
            Assert.True(new FirebaseOptions().DryRun);
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "smss-api.slnx"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var files = Directory.GetFiles(Path.Combine(dir!.FullName, "smss-api"), "appsettings*.json");
            Assert.True(files.Length >= 2);
            foreach (var f in files)
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                Assert.True(doc.RootElement.TryGetProperty("Firebase", out var fb), Path.GetFileName(f) + " has no Firebase section");
                Assert.True(fb.GetProperty("DryRun").GetBoolean(), Path.GetFileName(f) + ": Firebase:DryRun must be true");
                Assert.False(fb.TryGetProperty("CredentialsPath", out var p) && !string.IsNullOrWhiteSpace(p.GetString()), Path.GetFileName(f) + " must not carry a credentials path");
            }
            var ignore = File.ReadAllText(Path.Combine(dir.Parent!.FullName, ".gitignore"));
            Assert.Contains("*firebase*adminsdk*.json", ignore);
            Assert.Contains("firebase-service-account*.json", ignore);
        }

        [Fact]
        public void FcmSender_MissingOrInvalidCredentials_NeverThrow_AndNeverEchoThePath()
        {
            Assert.False(new FcmSender(Options.Create(new FirebaseOptions { CredentialsPath = null }), NullLogger<FcmSender>.Instance).TryEnsureReady(out var none));
            Assert.Equal("Firebase:CredentialsPath is not configured.", none);

            var missing = Path.Combine(Path.GetTempPath(), "smss-no-such-" + Guid.NewGuid().ToString("N") + ".json");
            Assert.False(new FcmSender(Options.Create(new FirebaseOptions { CredentialsPath = missing }), NullLogger<FcmSender>.Instance).TryEnsureReady(out var r1));
            Assert.DoesNotContain(missing, r1);
            Assert.DoesNotContain(Path.GetFileName(missing), r1);

            var junk = Path.Combine(Path.GetTempPath(), "smss-junk-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(junk, "{ \"not\": \"a service account\" }");
            try
            {
                Assert.False(new FcmSender(Options.Create(new FirebaseOptions { CredentialsPath = junk }), NullLogger<FcmSender>.Instance).TryEnsureReady(out var r2));
                Assert.DoesNotContain(junk, r2);
                Assert.Contains("could not be loaded", r2);
            }
            finally { File.Delete(junk); }
        }

        [Fact]
        public async Task HostedService_ATickThatThrows_IsContained()
        {
            var services = new ServiceCollection().AddScoped<IFeeReminderService, ThrowingReminders>().BuildServiceProvider();
            var job = new FeeReminderHostedService(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<FeeReminderHostedService>.Instance);
            await job.TickAsync(default);                                  // would fail the test if it threw
            Assert.Equal(TimeSpan.FromMinutes(5), FeeReminderHostedService.Interval);
        }

        private sealed class ThrowingReminders : IFeeReminderService
        {
            public Task<FeeReminderTickResult> RunTickAsync(CancellationToken ct) => throw new InvalidOperationException("db is not migrated yet");
            public Task<ApiResponse<FeeReminderPreviewDto>> PreviewAsync(string? schoolId, CancellationToken ct) => throw new NotSupportedException();
        }

        [Theory]
        [InlineData("android", "ANDROID")] [InlineData(" IOS ", "IOS")] [InlineData("windows", null)] [InlineData("", null)] [InlineData(null, null)]
        public void Rules_Platform(string? input, string? expected) => Assert.Equal(expected, NotificationRules.NormalizePlatform(input));

        [Theory]
        [InlineData("09:00", true)] [InlineData("23:59", true)] [InlineData("00:00", true)] [InlineData("24:00", false)] [InlineData("9:00", false)] [InlineData("09:60", false)] [InlineData("nine", false)] [InlineData(null, false)]
        public void Rules_SendTime(string? input, bool ok) => Assert.Equal(ok, NotificationRules.TryParseSendTime(input, out _));
    }
}
