using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using smss_api.Controllers;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Stage 2c (Docs/smss-st-2c-RUN-ME.sql): admission in TWO steps. An application is ADDED (status code APPLICATION_SUBMITTED) and then CONFIRMED (ADMITTED, the student is created, who and when
    /// recorded); rejecting and withdrawing are exits from the added state. School Admin, Principal and Admission Staff may add, edit, confirm and reject; every other role is refused.
    /// [DbFact]: needs SMSS_TEST_PG and a template with the 2c script applied.
    /// </summary>
    public class TwoStepAdmission2cTests
    {
        private const string A = TaxDb.SchoolA;
        private const long Y = TaxDb.YearA;

        private static AdmissionService Admissions(TaxDb db) =>
            new(new AdmissionRepository(db.Ctx()), Fake.Of<INotificationService>(), Fake.Of<IStudentFeeBillingService>(), NullLogger<AdmissionService>.Instance);

        private static SubmitAdmissionRequestDto App(TaxDb db, string first) => new()
        {
            AcademicYearId = Y, AppliedClassId = db.Scalar<long>($"select min(class_id) from tb_classes where school_id='{A}'"),
            AdmissionTypeId = db.Scalar<long>("select min(admission_type_id) from lut_admission_type"),
            ApplicantFirstName = first, DateOfBirth = new DateTime(2018, 3, 3), GuardianName = "Mother", GuardianRelationship = "MOTHER", GuardianConsent = true, GuardianMobileNumber = "9876543210",
            GuardianEmail = "g@example.com", GuardianWhatsAppSameAsMobile = true,
        };

        private static long Added(TaxDb db, string first)
        {
            var r = Admissions(db).SubmitAsync(A, App(db, first), default).Result;
            Assert.True(r.Status, r.Message);
            Assert.Equal("APPLICATION_SUBMITTED", r.Data!.StatusName);
            return r.Data.AdmissionId;
        }

        private static long Section(TaxDb db) => db.Scalar<long>($"select min(section_id) from tb_academic_year_sections where academic_year_id={Y} and status_id=1");
        private static long SomeUser(TaxDb db) => db.Scalar<long>("select min(user_id) from tb_users");
        private static string StatusOf(TaxDb db, long id) => db.Scalar<string>($"select s.sname from tb_admissions a join lut_status s on s.sid=a.status_id where a.admission_id={id}");

        // ------------------------------------------------------------------------------------------------ the labels
        [Fact]
        public void The_words_people_see_are_Application_added_and_Confirmed_while_the_codes_stay()
        {
            Assert.Equal("Application added", AdmissionService.StatusLabel("APPLICATION_SUBMITTED"));
            Assert.Equal("Confirmed", AdmissionService.StatusLabel("ADMITTED"));
            Assert.Equal("Rejected", AdmissionService.StatusLabel("REJECTED"));
            Assert.Equal("Withdrawn", AdmissionService.StatusLabel("CANCELLED"));
        }

        // ------------------------------------------------------------------------------------------------ the two steps
        [DbFact]
        public async Task An_added_application_is_confirmed_directly_and_records_who_and_when_with_no_login_created()
        {
            using var db = new TaxDb();
            var id = Added(db, "Direct");
            var users = db.Scalar<long>("select count(*) from tb_users");
            var students = db.Scalar<long>($"select count(*) from tb_students where school_id='{A}'");
            var who = SomeUser(db);
            var r = await Admissions(db).ConfirmAdmissionAsync(A, id, who, new ConfirmAdmissionRequestDto { ClassSectionId = Section(db) }, default);
            Assert.True(r.Status, r.Message);
            Assert.Equal("ADMITTED", r.Data!.StatusName);
            Assert.NotNull(r.Data.ConfirmedAt);
            Assert.Equal(students + 1, db.Scalar<long>($"select count(*) from tb_students where school_id='{A}'"));
            Assert.Equal(who, db.Scalar<long>($"select confirmed_by from tb_admissions where admission_id={id}"));
            Assert.True(db.Scalar<bool>($"select confirmed_at is not null from tb_admissions where admission_id={id}"));
            Assert.Equal(users, db.Scalar<long>("select count(*) from tb_users"));   // Stage 2 sends no message and creates no parent login
        }

        [DbFact]
        public async Task Confirming_twice_is_refused_the_second_time_and_makes_one_student()
        {
            using var db = new TaxDb();
            var id = Added(db, "Twice");
            var section = Section(db);
            Assert.True((await Admissions(db).ConfirmAdmissionAsync(A, id, SomeUser(db), new ConfirmAdmissionRequestDto { ClassSectionId = section }, default)).Status);
            var students = db.Scalar<long>($"select count(*) from tb_students where school_id='{A}'");
            var again = await Admissions(db).ConfirmAdmissionAsync(A, id, SomeUser(db), new ConfirmAdmissionRequestDto { ClassSectionId = section }, default);
            Assert.Equal(409, again.StatusCode);
            Assert.Contains("already confirmed", again.Message);
            Assert.Equal(students, db.Scalar<long>($"select count(*) from tb_students where school_id='{A}'"));
        }

        [DbFact]
        public async Task Six_simultaneous_confirms_of_one_application_make_exactly_one_student()
        {
            using var db = new TaxDb();
            var id = Added(db, "Race");
            var section = Section(db);
            var who = SomeUser(db);
            var before = db.Scalar<long>($"select count(*) from tb_students where school_id='{A}'");
            var tasks = Enumerable.Range(0, 6).Select(_ => Task.Run(() =>
                Admissions(db).ConfirmAdmissionAsync(A, id, who, new ConfirmAdmissionRequestDto { ClassSectionId = section }, default))).ToArray();
            var results = await Task.WhenAll(tasks);
            Assert.Equal(1, results.Count(r => r.Status));
            Assert.All(results.Where(r => !r.Status), r => Assert.Equal(409, r.StatusCode));
            Assert.Equal(before + 1, db.Scalar<long>($"select count(*) from tb_students where school_id='{A}'"));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_admissions where admission_id={id} and student_id is not null"));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_student_enrollments e join tb_admissions a on a.student_id=e.student_id where a.admission_id={id}"));
            Assert.Equal("ADMITTED", StatusOf(db, id));
        }

        [DbFact]
        public async Task A_rejected_or_withdrawn_application_cannot_be_confirmed_and_an_added_one_can_be_rejected_with_a_reason_or_withdrawn()
        {
            using var db = new TaxDb();
            var svc = Admissions(db);
            var who = SomeUser(db);

            var noReason = await svc.RejectAsync(A, Added(db, "NoReason"), who, new AdmissionDecisionRequestDto(), default);
            Assert.Equal(400, noReason.StatusCode);

            var rejectedId = Added(db, "Rejectee");
            var rejected = await svc.RejectAsync(A, rejectedId, who, new AdmissionDecisionRequestDto { Remarks = "Seats full" }, default);
            Assert.True(rejected.Status, rejected.Message);   // straight from added: no review step first
            Assert.Equal("REJECTED", StatusOf(db, rejectedId));
            Assert.Equal(who, db.Scalar<long>($"select rejected_by from tb_admissions where admission_id={rejectedId}"));
            var confirmRejected = await Admissions(db).ConfirmAdmissionAsync(A, rejectedId, who, new ConfirmAdmissionRequestDto { ClassSectionId = Section(db) }, default);
            Assert.Equal(409, confirmRejected.StatusCode);
            Assert.Contains("Rejected", confirmRejected.Message);

            var withdrawnId = Added(db, "Withdrawn");
            Assert.True((await svc.CancelAsync(A, withdrawnId, new AdmissionDecisionRequestDto { Remarks = "Moved away" }, default)).Status);
            Assert.Equal("CANCELLED", StatusOf(db, withdrawnId));
            Assert.Equal(409, (await Admissions(db).ConfirmAdmissionAsync(A, withdrawnId, who, new ConfirmAdmissionRequestDto { ClassSectionId = Section(db) }, default)).StatusCode);
            Assert.Equal(409, (await svc.RejectAsync(A, withdrawnId, who, new AdmissionDecisionRequestDto { Remarks = "x" }, default)).StatusCode);
        }

        [DbFact]
        public async Task An_application_is_editable_only_while_it_is_added()
        {
            using var db = new TaxDb();
            var id = Added(db, "Editable");
            var edit = App(db, "Edited");
            Assert.True((await Admissions(db).UpdateAsync(A, id, edit, default)).Status);
            Assert.True((await Admissions(db).ConfirmAdmissionAsync(A, id, SomeUser(db), new ConfirmAdmissionRequestDto { ClassSectionId = Section(db) }, default)).Status);
            var after = await Admissions(db).UpdateAsync(A, id, edit, default);
            Assert.Equal(409, after.StatusCode);
            Assert.Contains("Confirmed", after.Message);
        }

        // ------------------------------------------------------------------------------------------------ who may do what (the controller's role gate)
        private static AdmissionController Controller(object svc, params string[] roles) =>
            Fake.WithUser(new AdmissionController((IAdmissionService)svc, Fake.Of<IAdmissionDocumentService>(), Fake.Of<IOnboardingService>()), Fake.User(A, 7, roles));

        /// <summary>The status code, or 0 when the setup gate (a stand-in here) threw: that happens only AFTER the role check passed.</summary>
        private static async Task<int> CodeOrZero(Func<Task<IActionResult>> run)
        {
            try { return Fake.Code(await run()); } catch (Exception) { return 0; }
        }

        public static IEnumerable<object[]> RoleCases() => Fake.AllRoles.Select(r => new object[] { r });
        private static readonly string[] Allowed = { "School Admin", "Principal", "Admission Staff" };

        [Theory, MemberData(nameof(RoleCases))]
        public async Task Only_School_Admin_Principal_and_Admission_Staff_may_add_edit_confirm_reject_withdraw_and_read(string role)
        {
            var ok = Allowed.Contains(role);
            var svc = Fake.Of<IAdmissionService>();
            var c = Controller(svc, role);
            var calls = new (string Name, Func<Task<IActionResult>> Run, bool NeedsSetup)[]
            {
                ("GetAllAsync", () => c.GetAll(A, null, null, null, default), false),
                ("GetByIdAsync", () => c.GetById(A, 1, default), false),
                ("UpdateAsync", () => c.Update(A, 1, new SubmitAdmissionRequestDto(), default), false),
                ("RejectAsync", () => c.Reject(A, 1, new AdmissionDecisionRequestDto { Remarks = "x" }, default), false),
                ("CancelAsync", () => c.Cancel(A, 1, new AdmissionDecisionRequestDto(), default), false),
            };
            foreach (var (name, run, _) in calls)
            {
                var result = await run();
                if (ok) Assert.NotEqual(403, Fake.Code(result));
                else Assert.Equal(403, Fake.Code(result));
                Assert.Equal(ok, Fake.ProxyOf(svc).Calls.Contains(name));
            }
            // add and confirm sit behind the setup gate as well; the role answer comes first, so a refused role is 403 and an allowed one is never 403
            var submit = await CodeOrZero(() => c.Submit(A, new SubmitAdmissionRequestDto(), default));
            var confirm = await CodeOrZero(() => c.Confirm(A, 1, new ConfirmAdmissionRequestDto(), default));
            if (ok) { Assert.NotEqual(403, submit); Assert.NotEqual(403, confirm); }
            else { Assert.Equal(403, submit); Assert.Equal(403, confirm); }
        }

        [Fact]
        public void The_approve_and_start_review_steps_are_gone_from_the_service_and_the_routes()
        {
            Assert.Null(typeof(IAdmissionService).GetMethod("ApproveAsync"));
            Assert.Null(typeof(IAdmissionService).GetMethod("StartReviewAsync"));
            Assert.Null(typeof(AdmissionController).GetMethod("Approve"));
            Assert.Null(typeof(AdmissionController).GetMethod("StartReview"));
        }

        // ------------------------------------------------------------------------------------------------ the migration (on the scratch copy)
        [DbFact]
        public void After_the_migration_nothing_is_under_review_or_approved_and_every_confirmed_application_has_its_confirmer()
        {
            using var db = new TaxDb();
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_admissions a join lut_status s on s.sid=a.status_id where s.sname in ('UNDER_REVIEW','APPROVED')"));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_admissions a join lut_status s on s.sid=a.status_id where s.sname='ADMITTED' and (confirmed_by is null or confirmed_at is null or confirmed_by is distinct from approved_by or confirmed_at is distinct from approved_at)"));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_admissions a join lut_status s on s.sid=a.status_id where s.sname<>'ADMITTED' and (confirmed_by is not null or confirmed_at is not null)"));
        }

        [DbFact]
        public void The_database_refuses_a_confirmed_application_without_who_and_when()
        {
            using var db = new TaxDb();
            var id = Added(db, "Check");
            var ex = Assert.ThrowsAny<Exception>(() => db.Exec($"update tb_admissions set status_id=(select sid from lut_status where sname='ADMITTED'), student_id='X' where admission_id={id}"));
            Assert.True(ex.ToString().Contains("chk_tb_admissions_confirmed") || ex.ToString().Contains("chk_tb_admissions_converted") || ex.ToString().Contains("fk_"), ex.Message);
            // and the confirmer check itself, with the student link out of the way
            var exact = Assert.ThrowsAny<Exception>(() => db.Exec($"alter table tb_admissions drop constraint chk_tb_admissions_converted; update tb_admissions set status_id=(select sid from lut_status where sname='ADMITTED') where admission_id={id}"));
            Assert.Contains("chk_tb_admissions_confirmed", exact.ToString());
        }

        private static string ScriptText()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Docs", "smss-st-2c-RUN-ME.sql"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine(dir!.FullName, "Docs", "smss-st-2c-RUN-ME.sql"));
        }

        [DbFact]
        public void The_script_moves_a_stray_under_review_application_and_a_second_run_changes_nothing()
        {
            using var db = new TaxDb();
            var id = Added(db, "Stray");
            db.Exec($"update tb_admissions set status_id=(select sid from lut_status where sname='UNDER_REVIEW'), reviewed_by={SomeUser(db)}, reviewed_at=now() where admission_id={id}");
            var total = db.Scalar<long>("select count(*) from tb_admissions");
            var script = ScriptText();
            db.Exec(script);   // moves it back to added; history kept
            Assert.Equal("APPLICATION_SUBMITTED", StatusOf(db, id));
            Assert.Equal(SomeUser(db), db.Scalar<long>($"select reviewed_by from tb_admissions where admission_id={id}"));
            Assert.Equal(total, db.Scalar<long>("select count(*) from tb_admissions"));
            var fingerprint = db.Scalar<string>("select md5(string_agg(t::text, '|' order by admission_id)) from tb_admissions t");
            db.Exec(script);   // second run: ALREADY MIGRATED
            Assert.Equal(fingerprint, db.Scalar<string>("select md5(string_agg(t::text, '|' order by admission_id)) from tb_admissions t"));
        }
    }
}
