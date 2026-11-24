using Microsoft.Extensions.Logging.Abstractions;
using smss_api_db_layer.repository;
using smss_api_service_layer.@interface;
using smss_api_service_layer.helper;
using smss_api_service_layer.helper.idcard;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Follow-up sweep for stage 2a (optional last name): the remaining readers of a student's name that the first pass did not exercise. Every one must print the first (and middle) name
    /// alone, with no "null", no "undefined" and no stray space, when the last name is missing. [DbFact]: needs SMSS_TEST_PG and the smss_tpl_2a template.
    /// </summary>
    public class NameSweep2aFollowUpTests
    {
        private const string A = TaxDb.SchoolA;
        private const long Y = TaxDb.YearA;
        private const string S = "GHS001ST001";

        private static string NoLastName(TaxDb db, string? middle = "Kumar")
        {
            db.Exec($"update tb_students set last_name = null, middle_name = {(middle == null ? "null" : $"'{middle}'")} where student_id='{S}'");
            return string.Join(' ', new[] { db.Scalar<string>($"select first_name from tb_students where student_id='{S}'"), middle }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private static void Clean(string name, string expected)
        {
            Assert.Equal(expected, name);
            Assert.DoesNotContain("null", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("undefined", name, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(name.Trim(), name);
            Assert.DoesNotContain("  ", name);
        }

        [DbFact]
        public async Task The_attendance_roster_and_a_students_own_year_history_print_a_clean_name()
        {
            using var db = new TaxDb();
            var want = NoLastName(db);
            var section = db.Scalar<long>($"select class_section_id from tb_student_enrollments where student_id='{S}' and academic_year_id={Y}");
            var attendance = new AttendanceService(new AttendanceRepository(db.Ctx()), Fake.Of<IClassTeacherService>(), NullLogger<AttendanceService>.Instance);
            var roster = await attendance.GetSectionAsync(A, 120, true, section, null, default);
            Assert.True(roster.Status, roster.Message);
            Clean(roster.Data!.Students.Single(s => s.StudentId == S).FullName, want);
        }

        [DbFact]
        public async Task The_accountants_recent_payments_and_the_students_dashboard_print_a_clean_name()
        {
            using var db = new TaxDb();
            var want = NoLastName(db);
            Assert.True((await db.Pay(S, 10m)).Status);
            var recent = await new AccountantDashboardRepository(db.Ctx()).GetRecentPaymentsAsync(A, 50, default);
            Clean(recent.First(p => p.StudentId == S).StudentName, want);

            var c = db.Ctx();
            var svc = new StudentDashboardService(new StudentRepository(c), Fake.Of<IAttendanceService>(), Fake.Of<ISchoolEventService>(), Fake.Of<INoticeService>(), Fake.Of<ITimetableService>(),
                Fake.Of<IAssessmentService>(), Fake.Of<IStudentLeaveService>(), new SchoolDashboardRepository(c), NullLogger<StudentDashboardService>.Instance);
            var userId = db.Scalar<long>($"select user_id from tb_users where org_user_id='{S}' and school_id='{A}'");
            Clean((await svc.GetAsync(A, userId, default)).Data!.Profile.FullName, want);
        }

        [DbFact]
        public async Task The_admission_dashboard_and_the_applications_list_print_an_applicant_without_a_last_name_cleanly()
        {
            using var db = new TaxDb();
            db.Exec($@"insert into tb_admissions (school_id, application_number, academic_year_id, applied_class_id, admission_type_id, applicant_first_name, applicant_middle_name, applicant_last_name, date_of_birth,
                       guardian_name, guardian_relationship, guardian_mobile_number, guardian_email, guardian_whatsapp_number, application_date, status_id)
                       values ('{A}', 'ZZ2A-SWEEP', {Y}, (select min(class_id) from tb_classes where school_id='{A}'), (select min(admission_type_id) from lut_admission_type), 'Dev', 'Raj', null, '2018-01-01',
                       'G', 'FATHER', '9876543210', 'guardian@example.com', '+919876543210', current_date, (select sid from lut_status where sname='APPLICATION_SUBMITTED'))");
            var svc = new AdmissionService(new AdmissionRepository(db.Ctx()), Fake.Of<INotificationService>(), Fake.Of<IStudentFeeBillingService>(), NullLogger<AdmissionService>.Instance);
            var list = (await svc.GetAllAsync(A, null, null, null, default)).Data!;
            Clean(list.Single(a => a.ApplicationNumber == "ZZ2A-SWEEP").ApplicantFullName, "Dev Raj");

            // the dashboard's "missing a required document" list builds the applicant's name in SQL-side code of its own
            db.Exec($"insert into tb_document_types (school_id, document_name, document_code, applies_to, is_required, status_id) values ('{A}', 'Sweep doc', 'ZZ2A', 'STUDENT', true, 1)");
            var missing = await new AdmissionDashboardRepository(db.Ctx()).GetMissingDocumentsAsync(A, new[] { "APPLICATION_SUBMITTED" }, default);
            Clean(missing.Single(m => m.ApplicationNumber == "ZZ2A-SWEEP").ApplicantName, "Dev Raj");
        }

        [Fact]
        public void The_id_card_initials_work_for_a_name_without_a_last_name()
        {
            Assert.Equal("A", IdCardText.InitialsOf("Asha"));
            Assert.Equal("AD", IdCardText.InitialsOf("Asha Devi"));
            Assert.Equal("AR", IdCardText.InitialsOf("Asha Devi Rawat"));
            Assert.Equal("?", IdCardText.InitialsOf("   "));
        }
    }
}
