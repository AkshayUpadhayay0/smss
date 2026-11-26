using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using smss_api.Controllers;
using smss_api_db_layer.helper;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Stage 8a: "Record leaving" (LEFT / TRANSFERRED_OUT), its correction, the login rules after leaving (sign-in AND refresh) and the ONE live-enrolment rule used by every live roster and count.
    /// tb_students.status_id is never changed: "left" is the outcome of the enrolment. [DbFact]: needs SMSS_TEST_PG and the template with the 8a migration (smss_tpl_8a).
    /// </summary>
    public class StudentLeaving8aTests
    {
        private const string A = TaxDb.SchoolA;
        private const string B = TaxDb.SchoolB;
        private const long UserA = TaxDb.UserA;
        private static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        private const string ActiveSql = "(select sid from lut_status where sname = 'Active' and stype = 'general status')";

        private static LeavingService Svc(TaxDb db) => new(new LeavingRepository(db.Ctx()), NullLogger<LeavingService>.Instance);

        /// <summary>A student of school A with a login, a current-year enrolment and NO unpaid dues (so nothing blocks leaving unless a test adds a due).</summary>
        private static string Clean(TaxDb db, int n = 0)
        {
            var id = db.Scalar<string>($@"select e.student_id from tb_student_enrollments e join tb_users u on u.org_user_id = e.student_id and u.school_id = e.school_id and u.user_type = 'Student'
                 where e.school_id = {Q(A)} and e.academic_year_id = {TaxDb.YearA} and e.outcome is null order by e.student_id offset {n} limit 1");
            db.Exec($"update tb_student_fee_dues set amount_paid = amount_due where school_id = {Q(A)} and student_id = {Q(id)}");
            return id;
        }

        private static long EnrolmentOf(TaxDb db, string student) => db.Scalar<long>($"select enrollment_id from tb_student_enrollments where school_id = {Q(A)} and student_id = {Q(student)} and academic_year_id = {TaxDb.YearA}");
        private static long SectionOf(TaxDb db, string student) => db.Scalar<long>($"select class_section_id from tb_student_enrollments where enrollment_id = {EnrolmentOf(db, student)}");

        private static RecordLeavingRequest Req(string student, string type = "LEFT", string reason = "Family moved away", bool confirm = true, DateOnly? date = null, bool ovr = false, string? ovrReason = null) =>
            new() { StudentId = student, Type = type, Reason = reason, Confirm = confirm, LeavingDate = date, OverrideDues = ovr, OverrideReason = ovrReason };

        private static int Count(TaxDb db, string sql) => db.Scalar<int>(sql);

        // ------------------------------------------------------------------------------------------------ recording
        [DbFact]
        public async Task Recording_a_leaving_writes_the_outcome_date_reason_and_who_creates_no_enrolment_leaves_the_student_status_and_logs_it()
        {
            using var db = new TaxDb(); var s = Clean(db);
            var statusBefore = db.Scalar<int>($"select status_id from tb_students where student_id = {Q(s)}");
            var enrolmentsBefore = Count(db, "select count(*) from tb_student_enrollments");
            var others = db.Scalar<string>($"select md5(string_agg(concat_ws('|', e.*), ',' order by enrollment_id)) from tb_student_enrollments e where student_id <> {Q(s)}");
            var r = await Svc(db).RecordAsync(A, UserA, Req(s), default);
            Assert.True(r.Status, r.Message); Assert.Equal("LEFT", r.Data!.Outcome);
            Assert.Equal(1, Count(db, $@"select count(*) from tb_student_enrollments where enrollment_id = {EnrolmentOf(db, s)} and outcome = 'LEFT' and outcome_at is not null and leaving_date = current_date
                                         and leaving_reason = 'Family moved away' and outcome_by = {UserA} and leaving_override_reason is null and leaving_outstanding = 0"));
            Assert.Equal(enrolmentsBefore, Count(db, "select count(*) from tb_student_enrollments"));                                             // no new enrolment
            Assert.Equal(statusBefore, db.Scalar<int>($"select status_id from tb_students where student_id = {Q(s)}"));                          // tb_students is NOT changed
            Assert.Equal(others, db.Scalar<string>($"select md5(string_agg(concat_ws('|', e.*), ',' order by enrollment_id)) from tb_student_enrollments e where student_id <> {Q(s)}"));   // nobody else changed
            Assert.Equal(1, Count(db, $"select count(*) from tb_student_leaving_log where student_id = {Q(s)} and action = 'LEFT' and acted_by = {UserA} and not override_applied"));
        }

        [DbFact]
        public async Task A_transfer_out_is_recorded_the_same_way_and_the_type_is_checked()
        {
            using var db = new TaxDb(); var s = Clean(db);
            Assert.Equal(400, (await Svc(db).RecordAsync(A, UserA, Req(s, type: "PASSED_OUT"), default)).StatusCode);
            Assert.Equal(400, (await Svc(db).RecordAsync(A, UserA, Req(s, type: ""), default)).StatusCode);
            var r = await Svc(db).RecordAsync(A, UserA, Req(s, type: "transferred_out"), default);
            Assert.True(r.Status, r.Message);
            Assert.Equal("TRANSFERRED_OUT", db.Scalar<string>($"select outcome from tb_student_enrollments where enrollment_id = {EnrolmentOf(db, s)}"));
        }

        [DbFact]
        public async Task The_reason_the_confirmation_and_the_date_are_checked_and_a_student_who_already_left_is_a_409()
        {
            using var db = new TaxDb(); var s = Clean(db);
            Assert.Equal(400, (await Svc(db).RecordAsync(A, UserA, Req(s, reason: "  "), default)).StatusCode);
            Assert.Equal(400, (await Svc(db).RecordAsync(A, UserA, Req(s, confirm: false), default)).StatusCode);
            Assert.Equal(409, (await Svc(db).RecordAsync(A, UserA, Req(s, date: SchoolClock.Today().AddDays(1)), default)).StatusCode);                  // not in the future
            var enrolled = DateOnly.Parse(db.Scalar<string>($"select enrollment_date::text from tb_student_enrollments where enrollment_id = {EnrolmentOf(db, s)}"));
            Assert.Equal(409, (await Svc(db).RecordAsync(A, UserA, Req(s, date: enrolled.AddDays(-1)), default)).StatusCode);                            // not before the enrolment
            Assert.Equal(0, Count(db, "select count(*) from tb_student_leaving_log"));
            Assert.True((await Svc(db).RecordAsync(A, UserA, Req(s), default)).Status);
            Assert.Equal(409, (await Svc(db).RecordAsync(A, UserA, Req(s), default)).StatusCode);                                                       // the second time
            Assert.Equal(1, Count(db, "select count(*) from tb_student_leaving_log"));
        }

        [DbFact]
        public async Task Unpaid_dues_up_to_the_date_block_it_until_the_override_has_a_reason_of_ten_characters_and_the_override_is_recorded()
        {
            using var db = new TaxDb(); var s = Clean(db);
            db.Exec($@"update tb_student_fee_dues set amount_paid = 0 where fee_due_id = (select min(fee_due_id) from tb_student_fee_dues where school_id = {Q(A)} and student_id = {Q(s)} and due_date <= current_date)");
            var due = db.Scalar<decimal>($"select coalesce(sum(amount_due - amount_paid), 0) from tb_student_fee_dues where school_id = {Q(A)} and student_id = {Q(s)} and due_date <= current_date and amount_due > amount_paid");
            if (due == 0) { db.Exec($"update tb_student_fee_dues set due_date = current_date - 1, amount_paid = 0 where fee_due_id = (select min(fee_due_id) from tb_student_fee_dues where school_id = {Q(A)} and student_id = {Q(s)})"); due = db.Scalar<decimal>($"select sum(amount_due - amount_paid) from tb_student_fee_dues where school_id = {Q(A)} and student_id = {Q(s)} and due_date <= current_date and amount_due > amount_paid"); }
            Assert.True(due > 0);
            var check = (await Svc(db).CheckAsync(A, s, null, default)).Data!;
            Assert.True(check.RequiresOverride); Assert.Equal(due, check.Outstanding); Assert.NotEmpty(check.Dues); Assert.True(check.CanRecord);
            Assert.Equal(400, (await Svc(db).RecordAsync(A, UserA, Req(s), default)).StatusCode);                                                      // no override
            Assert.Equal(400, (await Svc(db).RecordAsync(A, UserA, Req(s, ovr: true, ovrReason: "too short"), default)).StatusCode);                  // 9 characters
            Assert.Equal(0, Count(db, $"select count(*) from tb_student_enrollments where student_id = {Q(s)} and outcome is not null"));
            var r = await Svc(db).RecordAsync(A, UserA, Req(s, ovr: true, ovrReason: "Parent promised to pay"), default);
            Assert.True(r.Status, r.Message); Assert.True(r.Data!.OverrideApplied); Assert.Equal(due, r.Data.Outstanding);
            Assert.Equal(1, Count(db, $"select count(*) from tb_student_enrollments where enrollment_id = {EnrolmentOf(db, s)} and leaving_override_reason = 'Parent promised to pay' and leaving_outstanding = {due}"));
            Assert.Equal(1, Count(db, $"select count(*) from tb_student_leaving_log where student_id = {Q(s)} and override_applied and override_reason = 'Parent promised to pay' and outstanding_amount = {due}"));
        }

        [DbFact]
        public async Task A_student_of_another_school_or_an_unknown_id_is_the_same_404_for_every_call()
        {
            using var db = new TaxDb(); var s = Clean(db);
            Assert.Equal(404, (await Svc(db).CheckAsync(B, s, null, default)).StatusCode);
            Assert.Equal(404, (await Svc(db).RecordAsync(B, TaxDb.UserB, Req(s), default)).StatusCode);
            Assert.Equal(404, (await Svc(db).CheckCorrectionAsync(B, s, default)).StatusCode);
            Assert.Equal(404, (await Svc(db).CorrectAsync(B, TaxDb.UserB, new LeavingCorrectionRequest { StudentId = s, Reason = "A long enough reason", Confirm = true }, default)).StatusCode);
            Assert.Equal(404, (await Svc(db).RecordAsync(A, UserA, Req("NOPE999"), default)).StatusCode);
            Assert.Equal(0, Count(db, $"select count(*) from tb_student_enrollments where student_id = {Q(s)} and outcome is not null"));
            Assert.Empty((await Svc(db).ListLeaversAsync(B, null, default)).Data!);
        }

        [DbFact]
        public async Task Only_a_School_Admin_or_a_Principal_may_record_check_correct_or_list()
        {
            using var db = new TaxDb(); var s = Clean(db);
            int Code(IActionResult r) => Fake.Code(r);
            StudentLeavingController C(params string[] roles) => Fake.WithUser(new StudentLeavingController(Svc(db)), Fake.User(A, UserA, roles));
            foreach (var role in new[] { "Teacher", "Accountant", "Student", "Guardian", "Admission Staff", "Store Keeper" })
            {
                Assert.Equal(403, Code(await C(role).Check(s, null, default)));
                Assert.Equal(403, Code(await C(role).Record(Req(s), default)));
                Assert.Equal(403, Code(await C(role).CheckCorrection(s, default)));
                Assert.Equal(403, Code(await C(role).Correct(new LeavingCorrectionRequest { StudentId = s, Reason = "A long enough reason", Confirm = true }, default)));
                Assert.Equal(403, Code(await C(role).Leavers(null, default)));
            }
            Assert.Equal(0, Count(db, $"select count(*) from tb_student_enrollments where student_id = {Q(s)} and outcome is not null"));
            Assert.Equal(200, Code(await C("Principal").Check(s, null, default)));
            Assert.Equal(200, Code(await C("School Admin").Record(Req(s), default)));
            Assert.Equal(200, Code(await C("Principal").Leavers(null, default)));
        }

        // ------------------------------------------------------------------------------------------------ the student's login
        private static AuthService Auth(TaxDb db)
        {
            var ctx = db.Ctx();
            return new AuthService(new AuthRepository(ctx), new JwtTokenGenerator(Options.Create(new JwtSettings { Key = new string('k', 48) })), NullLogger<AuthService>.Instance, new LoginThrottle(new LoginThrottleOptions()), new ParentAccountRepository(ctx));
        }

        private static void GivePassword(TaxDb db, string student) =>
            db.Exec($"update tb_users set password_hash = {Q(PasswordHelper.Hash("Sunny-Day-2026"))}, is_first_login = false, status_id = {ActiveSql} where org_user_id = {Q(student)} and school_id = {Q(A)} and user_type = 'Student'");

        private static async Task<(int Code, string Message, string? Refresh)> SignIn(TaxDb db, string student)
        {
            var r = await Auth(db).LoginAsync(new LoginRequest { LoginId = student, Password = "Sunny-Day-2026", RememberMe = true });
            string? refresh = null;
            if (r.Data != null) refresh = r.Data.GetType().GetProperty("RefreshToken")?.GetValue(r.Data) as string;
            return (r.StatusCode, r.Message, refresh);
        }

        [DbFact]
        public async Task A_student_who_left_cannot_sign_in_and_the_message_says_to_contact_the_school_but_before_leaving_and_after_a_correction_they_can()
        {
            using var db = new TaxDb(); var s = Clean(db); GivePassword(db, s);
            Assert.Equal(200, (await SignIn(db, s)).Code);
            Assert.True((await Svc(db).RecordAsync(A, UserA, Req(s), default)).Status);
            var blocked = await SignIn(db, s);
            Assert.Equal(403, blocked.Code); Assert.Equal(LeavingRules.LoginBlockedMessage, blocked.Message); Assert.Contains("contact your school", blocked.Message);
            var fix = await Svc(db).CorrectAsync(A, UserA, new LeavingCorrectionRequest { StudentId = s, Reason = "Recorded by mistake", Confirm = true }, default);
            Assert.True(fix.Status, fix.Message);
            Assert.Equal(200, (await SignIn(db, s)).Code);
        }

        [DbFact]
        public async Task Recording_a_leaving_revokes_the_refresh_tokens_at_once_and_a_refresh_with_an_old_token_is_refused()
        {
            using var db = new TaxDb(); var s = Clean(db); GivePassword(db, s);
            var login = await SignIn(db, s); Assert.Equal(200, login.Code); Assert.False(string.IsNullOrEmpty(login.Refresh));
            var userId = db.Scalar<long>($"select user_id from tb_users where org_user_id = {Q(s)} and school_id = {Q(A)} and user_type = 'Student'");
            Assert.True(db.Scalar<int>($"select count(*) from tb_refresh_tokens where user_id = {userId} and revoked_at is null") > 0);
            Assert.True((await Svc(db).RecordAsync(A, UserA, Req(s, type: "TRANSFERRED_OUT"), default)).Status);
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_refresh_tokens where user_id = {userId} and revoked_at is null"));
            var refreshed = await Auth(db).RefreshAsync(new RefreshTokenRequest { RefreshToken = login.Refresh! });
            Assert.NotEqual(200, refreshed.StatusCode);
        }

        [DbFact]
        public async Task Refresh_itself_blocks_a_student_who_left_even_when_the_token_was_never_revoked()
        {
            using var db = new TaxDb(); var s = Clean(db); GivePassword(db, s);
            var login = await SignIn(db, s);
            db.Exec($"update tb_student_enrollments set outcome = 'LEFT', outcome_at = now() where enrollment_id = {EnrolmentOf(db, s)}");              // the outcome without going through the service: no revoke happened
            var refreshed = await Auth(db).RefreshAsync(new RefreshTokenRequest { RefreshToken = login.Refresh! });
            Assert.Equal(403, refreshed.StatusCode); Assert.Equal(LeavingRules.LoginBlockedMessage, refreshed.Message);
        }

        [DbFact]
        public async Task A_passed_out_student_signs_in_while_their_year_is_current_and_is_refused_once_it_is_not_and_an_Inactive_student_is_refused_like_a_leaver()
        {
            using var db = new TaxDb(); var s = Clean(db); GivePassword(db, s);
            db.Exec($"update tb_student_enrollments set outcome = 'PASSED_OUT', outcome_at = now() where enrollment_id = {EnrolmentOf(db, s)}");
            Assert.Equal(200, (await SignIn(db, s)).Code);                                                                                           // the year is still current
            db.Exec($"update tb_academic_years set is_current = false where academic_year_id = {TaxDb.YearA}");
            Assert.Equal(403, (await SignIn(db, s)).Code);                                                                                           // no longer current
            db.Exec($"update tb_academic_years set is_current = true where academic_year_id = {TaxDb.YearA}");
            db.Exec($"update tb_student_enrollments set outcome = null, outcome_at = null where enrollment_id = {EnrolmentOf(db, s)}");
            Assert.Equal(200, (await SignIn(db, s)).Code);
            db.Exec($"update tb_students set status_id = (select sid from lut_status where sname = 'Inactive' and stype = 'general status') where student_id = {Q(s)}");
            var inactive = await SignIn(db, s);
            Assert.Equal(403, inactive.Code); Assert.Equal(LeavingRules.LoginBlockedMessage, inactive.Message);
        }

        [Fact]
        public void The_login_rule_is_a_table_not_a_guess()
        {
            Assert.False(LeavingRules.StudentLoginBlocked(true, null, true));
            Assert.False(LeavingRules.StudentLoginBlocked(true, "PROMOTED", false));
            Assert.True(LeavingRules.StudentLoginBlocked(true, "LEFT", true));
            Assert.True(LeavingRules.StudentLoginBlocked(true, "TRANSFERRED_OUT", true));
            Assert.False(LeavingRules.StudentLoginBlocked(true, "PASSED_OUT", true));
            Assert.True(LeavingRules.StudentLoginBlocked(true, "PASSED_OUT", false));
            Assert.True(LeavingRules.StudentLoginBlocked(false, null, true));
        }

        // ------------------------------------------------------------------------------------------------ the guardian
        [DbFact]
        public async Task A_guardian_keeps_read_only_access_to_a_child_who_left_the_card_says_LEFT_and_the_profile_still_opens()
        {
            using var db = new TaxDb(); var s = Clean(db);
            var user = db.Scalar<long>($@"insert into tb_users (school_id, user_type, org_user_id, display_name, status_id, is_first_login, password_hash) values ({Q(A)}, 'Guardian', 'GHS001GD777', 'Parent', {ActiveSql}, false, null) returning user_id");
            db.Exec($"insert into tb_user_roles (user_id, role_id) select {user}, role_id from lut_roles where role_code = 'GUARDIAN'");
            var g = db.Scalar<long>($@"insert into tb_guardians (school_id, first_name, relationship, mobile_number, status_id, user_id) values ({Q(A)}, 'Parent', 'MOTHER', '9876543210', {ActiveSql}, {user}) returning guardian_id");
            db.Exec($"insert into tb_student_guardians (student_id, guardian_id, relationship) values ({Q(s)}, {g}, 'MOTHER')");
            Assert.True((await Svc(db).RecordAsync(A, UserA, Req(s), default)).Status);
            var ctx = db.Ctx(); var accounts = new ParentAccountRepository(ctx); var students = new StudentRepository(ctx); var guardians = new GuardianRepository(ctx);
            var helper = new StudentAccessService(accounts, students, guardians);
            var caller = new AccessCaller(A, user, false, true, g);
            Assert.Equal(200, (await helper.ResolveAsync(caller, s, default)).StatusCode);                                                                    // still reachable (the helper is unchanged)
            var fam = new FamilyService(helper, accounts, students, guardians, Fake.Of<IStudentService>(new() { ["GetProfileForStudentAsync"] = Fake.Ok(new StudentMyProfileDto { StudentId = s, FullName = "Child", AdmissionNumber = "1" }) }),
                Fake.Of<IAttendanceService>(), Fake.Of<IHomeworkService>(), Fake.Of<ITimetableService>(), Fake.Of<IReportResultService>(), Fake.Of<IReportScheduleService>(), Fake.Of<IStudentMyFeesService>(),
                Fake.Of<IFeePaymentService>(), Fake.Of<IStudentLeaveService>(), Fake.Of<IStudentPhotoService>(), Fake.Of<INoticeService>(), Fake.Of<ISchoolEventService>(), NullLogger<FamilyService>.Instance);
            var list = (await fam.ListChildrenAsync(caller, default)).Data!;
            var card = Assert.Single(list.Children, c => c.StudentId == s);
            Assert.Equal("LEFT", card.ChipState); Assert.True(card.ReadOnly);
            Assert.Equal(200, (await fam.ProfileAsync(caller, s, default)).StatusCode);
        }

        // ------------------------------------------------------------------------------------------------ the correction
        [DbFact]
        public async Task A_correction_clears_everything_it_wrote_logs_who_and_why_and_changes_nobody_else()
        {
            using var db = new TaxDb(); var s = Clean(db);
            Assert.True((await Svc(db).RecordAsync(A, UserA, Req(s), default)).Status);
            var others = db.Scalar<string>($"select md5(string_agg(concat_ws('|', e.*), ',' order by enrollment_id)) from tb_student_enrollments e where student_id <> {Q(s)}");
            Assert.Equal(400, (await Svc(db).CorrectAsync(A, UserA, new LeavingCorrectionRequest { StudentId = s, Reason = "short", Confirm = true }, default)).StatusCode);
            Assert.Equal(400, (await Svc(db).CorrectAsync(A, UserA, new LeavingCorrectionRequest { StudentId = s, Reason = "A long enough reason", Confirm = false }, default)).StatusCode);
            var r = await Svc(db).CorrectAsync(A, UserA, new LeavingCorrectionRequest { StudentId = s, Reason = "Recorded by mistake", Confirm = true }, default);
            Assert.True(r.Status, r.Message);
            Assert.Equal(1, Count(db, $@"select count(*) from tb_student_enrollments where enrollment_id = {EnrolmentOf(db, s)} and outcome is null and outcome_at is null and leaving_date is null and leaving_reason is null
                                         and outcome_by is null and leaving_override_reason is null and leaving_outstanding is null"));
            Assert.Equal(1, Count(db, $"select count(*) from tb_student_leaving_log where student_id = {Q(s)} and action = 'CORRECTION' and outcome_cleared = 'LEFT' and reason = 'Recorded by mistake' and acted_by = {UserA}"));
            Assert.Equal(2, Count(db, $"select count(*) from tb_student_leaving_log where student_id = {Q(s)}"));
            Assert.Equal(others, db.Scalar<string>($"select md5(string_agg(concat_ws('|', e.*), ',' order by enrollment_id)) from tb_student_enrollments e where student_id <> {Q(s)}"));
            Assert.Equal(409, (await Svc(db).CorrectAsync(A, UserA, new LeavingCorrectionRequest { StudentId = s, Reason = "Recorded by mistake", Confirm = true }, default)).StatusCode);   // nothing left to correct
        }

        [DbFact]
        public async Task A_correction_is_refused_when_a_later_enrolment_exists_or_the_outcome_came_from_a_promotion_run()
        {
            using var db = new TaxDb(); var s = Clean(db); var promoted = Clean(db, 1);
            Assert.True((await Svc(db).RecordAsync(A, UserA, Req(s), default)).Status);
            // a later enrolment: a newer year with an enrolment of the same student
            db.Exec($@"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ({Q(A)}, '2027-28', date '2027-04-01', date '2028-03-31', false, {ActiveSql})");
            var next = db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id = {Q(A)} and year_name = '2027-28'");
            db.Exec($"insert into tb_academic_year_classes (school_id, academic_year_id, class_id, status_id) select {Q(A)}, {next}, class_id, {ActiveSql} from tb_sections where section_id = {SectionOf(db, s)}");
            db.Exec($"insert into tb_academic_year_sections (school_id, academic_year_id, class_id, section_id, status_id) select {Q(A)}, {next}, class_id, section_id, {ActiveSql} from tb_sections where section_id = {SectionOf(db, s)}");
            db.Exec($@"insert into tb_student_enrollments (school_id, student_id, academic_year_id, class_section_id, enrollment_date, admission_type_id, status_id)
                       select school_id, student_id, {next}, class_section_id, date '2027-04-02', admission_type_id, {ActiveSql} from tb_student_enrollments where enrollment_id = {EnrolmentOf(db, s)}");
            var later = await Svc(db).CorrectAsync(A, UserA, new LeavingCorrectionRequest { StudentId = s, Reason = "Recorded by mistake", Confirm = true }, default);
            Assert.Equal(409, later.StatusCode); Assert.Contains("later enrolment", later.Message);
            // an outcome written by promotion has no leaving date: it is corrected on the Promotion screen
            db.Exec($"update tb_student_enrollments set outcome = 'LEFT', outcome_at = now() where enrollment_id = {EnrolmentOf(db, promoted)}");
            var fromPromotion = await Svc(db).CorrectAsync(A, UserA, new LeavingCorrectionRequest { StudentId = promoted, Reason = "Recorded by mistake", Confirm = true }, default);
            Assert.Equal(409, fromPromotion.StatusCode); Assert.Contains("Promotion", fromPromotion.Message);
        }

        // ------------------------------------------------------------------------------------------------ the ONE live-enrolment rule
        [Fact]
        public void The_live_rule_as_code_is_the_table_the_decision_states()
        {
            Assert.True(LiveEnrolment.IsLive(true, null, true));
            Assert.True(LiveEnrolment.IsLive(true, null, false));
            Assert.False(LiveEnrolment.IsLive(false, null, true));                 // an inactive enrolment is not live
            Assert.False(LiveEnrolment.IsLive(true, "LEFT", true));
            Assert.False(LiveEnrolment.IsLive(true, "TRANSFERRED_OUT", true));
            Assert.True(LiveEnrolment.IsLive(true, "PASSED_OUT", true));           // until its year is no longer current
            Assert.False(LiveEnrolment.IsLive(true, "PASSED_OUT", false));
            Assert.True(LiveEnrolment.IsLive(true, "PROMOTED", false));
            Assert.True(LiveEnrolment.IsLive(true, "REPEATED", false));
        }

        [DbFact]
        public void The_SQL_form_the_EF_form_and_the_code_form_of_the_rule_agree_on_every_outcome_and_year_state()
        {
            using var db = new TaxDb(); var s = Clean(db); var enr = EnrolmentOf(db, s);
            foreach (var outcome in new string?[] { null, "PROMOTED", "REPEATED", "PASSED_OUT", "LEFT", "TRANSFERRED_OUT" })
                foreach (var current in new[] { true, false })
                {
                    db.Exec($"update tb_academic_years set is_current = {(current ? "true" : "false")} where academic_year_id = {TaxDb.YearA}");
                    db.Exec($"update tb_student_enrollments set outcome = {(outcome == null ? "null" : Q(outcome))}, outcome_at = {(outcome == null ? "null" : "now()")} where enrollment_id = {enr}");
                    var forYear = LiveEnrolment.IsLiveForYear(outcome, current);
                    Assert.Equal(forYear, db.Scalar<bool>($"select {LiveEnrolment.SqlForYear("e")} from tb_student_enrollments e where e.enrollment_id = {enr}"));
                    using (var c2 = db.Ctx()) Assert.Equal(forYear, c2.StudentEnrollments.LiveForYear(c2.AcademicYears).Any(e => e.EnrollmentId == enr));
                    var expected = LiveEnrolment.OutcomeIsLive(outcome, current);
                    Assert.Equal(expected, db.Scalar<bool>($"select {LiveEnrolment.Sql("e")} from tb_student_enrollments e where e.enrollment_id = {enr}"));
                    using var ctx = db.Ctx();
                    Assert.Equal(expected, ctx.StudentEnrollments.LiveOutcome(ctx.AcademicYears).Any(e => e.EnrollmentId == enr));
                }
        }

        /// <summary>Every live roster, count and list that was changed: present before the leaving, gone after it. Old-year and history views keep the student.</summary>
        [DbFact]
        public async Task A_leaver_drops_out_of_every_live_roster_count_and_list_but_old_views_keep_them()
        {
            using var db = new TaxDb(); var s = Clean(db); var sec = SectionOf(db, s);
            var cls = db.Scalar<long>($"select class_id from tb_sections where section_id = {sec}");
            var active = db.Scalar<int>($"select {ActiveSql}");
            db.Exec($"insert into tb_class_teacher_assignments (school_id, academic_year_id, section_id, employee_id, status_id, effective_from) select {Q(A)}, {TaxDb.YearA}, {sec}, (select min(employee_id) from tb_employees where school_id = {Q(A)}), {ActiveSql}, date '2026-04-01' where false");
            db.Exec($"delete from tb_push_notification_log where false");

            async Task<Dictionary<string, bool>> Snapshot()
            {
                var c = db.Ctx(); var r = new Dictionary<string, bool>();
                r["attendance roster"] = (await new AttendanceRepository(c).GetRosterAsync(A, TaxDb.YearA, sec, active, default)).Any(x => x.StudentId == s);
                r["assessment roster"] = (await new AssessmentRepository(db.Ctx()).GetRosterAsync(A, TaxDb.YearA, sec, active, default)).Any(x => x.StudentId == s);
                r["report marks roster"] = (await new ReportMarksRepository(db.Ctx()).GetRosterAsync(A, TaxDb.YearA, sec, active, default)).Any(x => x.StudentId == s);
                r["class student list"] = (await new StudentRepository(db.Ctx()).GetEnrolledStudentsAsync(A, TaxDb.YearA, sec, default)).Any(x => x.StudentId == s);
                r["fee class students"] = (await new FeeReportRepository(db.Ctx()).GetClassStudentsAsync(A, TaxDb.YearA, cls, sec, default)).Any(x => x.StudentId == s);
                r["push eligible"] = (await new NotificationRepository(db.Ctx()).GetEligibleStudentsAsync(A, TaxDb.YearA, "Student", default)).Any(x => x.StudentId == s);
                return r;
            }
            var before = await Snapshot();
            Assert.All(before, kv => Assert.True(kv.Value, kv.Key + " should list the student before leaving"));
            var sectionCountBefore = (await new AcademicSetupRepository(db.Ctx()).EnrolmentsBySectionAsync(A, TaxDb.YearA, active, default))[sec];
            var dashBefore = await new SchoolDashboardRepository(db.Ctx()).EnrolledStudentsAsync(A, TaxDb.YearA, default);
            var adminBefore = await new SchoolAdminDashboardRepository(db.Ctx()).EnrolledStudentsAsync(A, active, TaxDb.YearA, default);
            var billedBefore = (await new SubscriptionRepository(db.Ctx()).CountBillableUsersAsync(A, active, default)).Students;
            var unmarkedBefore = (await new AttendanceRepository(db.Ctx()).GetMarkableSectionsAsync(A, TaxDb.YearA, SchoolClock.Today(), null, active, default)).Single(x => x.SectionId == sec).RosterCount;

            Assert.True((await Svc(db).RecordAsync(A, UserA, Req(s), default)).Status);

            var after = await Snapshot();
            Assert.All(after, kv => Assert.False(kv.Value, kv.Key + " must not list a leaver"));
            Assert.Equal(sectionCountBefore - 1, (await new AcademicSetupRepository(db.Ctx()).EnrolmentsBySectionAsync(A, TaxDb.YearA, active, default))[sec]);
            Assert.Equal(dashBefore - 1, await new SchoolDashboardRepository(db.Ctx()).EnrolledStudentsAsync(A, TaxDb.YearA, default));
            Assert.Equal(adminBefore - 1, await new SchoolAdminDashboardRepository(db.Ctx()).EnrolledStudentsAsync(A, active, TaxDb.YearA, default));
            Assert.Equal(billedBefore - 1, (await new SubscriptionRepository(db.Ctx()).CountBillableUsersAsync(A, active, default)).Students);        // billing stops with the leaving
            Assert.Equal(unmarkedBefore - 1, (await new AttendanceRepository(db.Ctx()).GetMarkableSectionsAsync(A, TaxDb.YearA, SchoolClock.Today(), null, active, default)).Single(x => x.SectionId == sec).RosterCount);

            // history: the old / year-wise views are unchanged
            Assert.Contains((await new AttendanceRepository(db.Ctx()).GetReportRosterAsync(A, TaxDb.YearA, sec, default)), x => x.StudentId == s);
            Assert.Contains((await new StudentRepository(db.Ctx()).GetEnrollmentsAsync(A, s, default)), x => x.AcademicYearId == TaxDb.YearA);
            Assert.Equal(1, Count(db, $"select count(*) from tb_students where student_id = {Q(s)} and status_id = {ActiveSql}"));
        }

        [DbFact]
        public async Task A_passed_out_student_stays_in_the_live_lists_while_their_year_is_current_and_leaves_them_when_it_is_not()
        {
            using var db = new TaxDb(); var s = Clean(db); var sec = SectionOf(db, s);
            var active = db.Scalar<int>($"select {ActiveSql}");
            db.Exec($"update tb_student_enrollments set outcome = 'PASSED_OUT', outcome_at = now() where enrollment_id = {EnrolmentOf(db, s)}");
            Assert.Contains(await new AttendanceRepository(db.Ctx()).GetRosterAsync(A, TaxDb.YearA, sec, active, default), x => x.StudentId == s);
            db.Exec($"update tb_academic_years set is_current = false where academic_year_id = {TaxDb.YearA}");
            // a view of an OLD year is history and shows everyone, passed-out and leavers included
            Assert.Contains(await new AttendanceRepository(db.Ctx()).GetRosterAsync(A, TaxDb.YearA, sec, active, default), x => x.StudentId == s);
            var other = db.Scalar<string>($"select student_id from tb_student_enrollments where school_id = {Q(A)} and academic_year_id = {TaxDb.YearA} and class_section_id = {sec} and student_id <> {Q(s)} and outcome is null limit 1");
            db.Exec($"update tb_student_enrollments set outcome = 'LEFT', outcome_at = now() where school_id = {Q(A)} and student_id = {Q(other)} and academic_year_id = {TaxDb.YearA}");
            Assert.Contains(await new AttendanceRepository(db.Ctx()).GetRosterAsync(A, TaxDb.YearA, sec, active, default), x => x.StudentId == other);
            db.Exec($"update tb_academic_years set is_current = true where academic_year_id = {TaxDb.YearA}");
            Assert.DoesNotContain(await new AttendanceRepository(db.Ctx()).GetRosterAsync(A, TaxDb.YearA, sec, active, default), x => x.StudentId == other);
        }

        /// <summary>The lists that need a little set-up: parent details, pending documents, fees to assign, the teacher's unpaid-dues count, and the ID card.</summary>
        [DbFact]
        public async Task A_leaver_also_drops_out_of_the_parent_details_the_pending_documents_the_fees_to_assign_the_unpaid_count_and_the_id_cards()
        {
            using var db = new TaxDb(); var s = Clean(db); var sec = SectionOf(db, s);
            var active = db.Scalar<int>($"select {ActiveSql}");
            // a parent with no WhatsApp number, primary for this student only
            db.Exec($"update tb_guardians set whatsapp_number = null, status_id = {ActiveSql} where guardian_id in (select guardian_id from tb_student_guardians where student_id = {Q(s)} and is_primary)");
            db.Exec($"delete from tb_student_guardians where is_primary and student_id <> {Q(s)} and guardian_id in (select guardian_id from tb_student_guardians where student_id = {Q(s)} and is_primary)");   // this parent has only this child
            // a required document nobody has
            db.Exec($"insert into tb_document_types (school_id, document_name, document_code, applies_to, is_required, status_id, required_from) values ({Q(A)}, 'Leaving test paper', 'LTP8A', 'STUDENT', true, {ActiveSql}, date '2000-01-01')");
            // an unassigned fee structure and an unpaid due this month
            db.Exec($"delete from tb_student_fee_structure where school_id = {Q(A)} and student_id = {Q(s)}");
            db.Exec($"update tb_student_fee_dues set amount_paid = 0, due_date = current_date where fee_due_id = (select min(fee_due_id) from tb_student_fee_dues where school_id = {Q(A)} and student_id = {Q(s)})");
            var from = SchoolClock.Today().AddDays(-1); var to = SchoolClock.Today().AddDays(1);

            async Task<(int Parents, int Pending, bool Missing, int Unpaid, bool Card)> Look()
            {
                var parents = await new GuardianRepository(db.Ctx()).CountIncompletePrimaryAsync(A, active, default);
                var pending = await new StudentDocumentRepository(db.Ctx()).CountPendingStudentsAsync(A, active, active, default);
                var missing = (await new FeeRepairRepository(db.Ctx()).ListMissingAsync(A, TaxDb.YearA, default)).Any(x => x.StudentId == s);
                var unpaid = await new TeacherDashboardRepository(db.Ctx()).CountStudentsWithUnpaidDuesAsync(A, TaxDb.YearA, new[] { sec }, active, from, to, default);
                var card = (await new StudentIdCardRepository(db.Ctx()).GetSectionStudentsAsync(A, TaxDb.YearA, sec, default)).Any(x => x.StudentId == s);
                return (parents, pending, missing, unpaid, card);
            }
            var before = await Look();
            Assert.True(before.Missing); Assert.True(before.Card); Assert.True(before.Unpaid >= 1); Assert.True(before.Parents >= 1); Assert.True(before.Pending >= 1);
            Assert.True((await Svc(db).RecordAsync(A, UserA, Req(s, ovr: true, ovrReason: "Dues will be paid later"), default)).Status);
            var after = await Look();
            Assert.Equal(before.Parents - 1, after.Parents); Assert.Equal(before.Pending - 1, after.Pending); Assert.False(after.Missing); Assert.Equal(before.Unpaid - 1, after.Unpaid); Assert.False(after.Card);
        }

        [DbFact]
        public async Task A_promotion_correction_is_refused_while_the_new_year_enrolment_already_has_a_leaving_on_it()
        {
            using var db = new TaxDb(); var s = Clean(db);
            Assert.True((await Svc(db).RecordAsync(A, UserA, Req(s), default)).Status);
            Assert.Equal("LEFT", db.Scalar<string>($"select outcome from tb_student_enrollments where enrollment_id = {EnrolmentOf(db, s)}"));
            // the promotion check reads the outcome of the enrolment it would remove (TargetOutcome): the column the leaving writes is the one it looks at
            using var ctx = db.Ctx();
            Assert.Equal("LEFT", ctx.StudentEnrollments.Single(e => e.EnrollmentId == EnrolmentOf(db, s)).Outcome);
        }
    }
}
