using Microsoft.Extensions.Logging.Abstractions;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Stage 1c: the Principal, Accountant, Teacher and Student dashboards and the one rule for the year they show (DashboardYears). [DbFact]: needs SMSS_TEST_PG and the smss_tpl_1b template.
    /// School GHS261103002 has 176 students enrolled in 2026-27 (year 41, current) with fees; these tests add a 2027-28 year nobody is enrolled in.
    /// </summary>
    public class YearDashboards1cTests
    {
        private const string A = TaxDb.SchoolA, B = TaxDb.SchoolB;
        private const long Y1 = TaxDb.YearA;
        private const int Students = 176;

        private static long NextYear(TaxDb db, bool current)
        {
            db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('{A}', '2027-28', '2027-04-01', '2028-03-31', false, 1)");
            var y2 = db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id='{A}' and year_name='2027-28'");
            if (current)
            {
                db.Exec($"update tb_academic_years set is_current=false where academic_year_id={Y1}");
                db.Exec($"update tb_academic_years set is_current=true where academic_year_id={y2}");
            }
            return y2;
        }

        private static PrincipalDashboardService Principal(TaxDb db)
        {
            var c = db.Ctx();
            return new PrincipalDashboardService(db.Reports(), Fake.Of<IEmployeeService>(), Fake.Of<IEmployeeLeaveService>(), Fake.Of<IAdmissionService>(), Fake.Of<IReportResultService>(),
                Fake.Of<ISchoolEventService>(), Fake.Of<INoticeService>(), new SchoolDashboardRepository(c), NullLogger<PrincipalDashboardService>.Instance);
        }

        private static AccountantDashboardService Accountant(TaxDb db)
        {
            var c = db.Ctx();
            return new AccountantDashboardService(db.Reports(), new AccountantDashboardRepository(c), new SchoolDashboardRepository(c), NullLogger<AccountantDashboardService>.Instance);
        }

        // ------------------------------------------------------------------------------------------------ the year rule
        [DbFact]
        public async Task The_viewed_year_is_the_one_asked_for_else_the_current_flagged_year_else_the_year_containing_today()
        {
            using var db = new TaxDb();
            var y2 = NextYear(db, current: true);                 // 2027-28 is flagged current, but today (2026) is inside 2026-27
            var repo = new SchoolDashboardRepository(db.Ctx());
            var today = SchoolClock.Today();

            var byDefault = await DashboardYears.ResolveAsync(repo, A, null, today, default);
            Assert.Equal(y2, byDefault.Viewed!.AcademicYearId);   // the flag wins over "contains today"
            Assert.False(byDefault.ViewingAnotherYear);

            var asked = await DashboardYears.ResolveAsync(repo, A, Y1, today, default);
            Assert.Equal(Y1, asked.Viewed!.AcademicYearId);
            Assert.True(asked.ViewingAnotherYear);
            Assert.Contains("You are viewing 2026-27", DashboardYears.ViewingNote(asked));
            Assert.Contains("current year is 2027-28", DashboardYears.ViewingNote(asked));

            Assert.True((await DashboardYears.ResolveAsync(repo, A, db.Scalar<long>($"select min(academic_year_id) from tb_academic_years where school_id='{B}'"), today, default)).NotFound);
            Assert.True((await DashboardYears.ResolveAsync(repo, A, 99999999, today, default)).NotFound);

            db.Exec($"update tb_academic_years set is_current=false where school_id='{A}'");   // nothing flagged
            var fallback = await DashboardYears.ResolveAsync(repo, A, null, today, default);
            Assert.Equal(Y1, fallback.Viewed!.AcademicYearId);    // the year that contains today
            Assert.True(fallback.FellBackToToday);
            Assert.Contains("No academic year is set as current", DashboardYears.ViewingNote(fallback));
        }

        // ------------------------------------------------------------------------------------------------ principal
        [DbFact]
        public async Task Principal_defaults_to_the_current_year_and_shows_no_fee_structure_not_zeros_when_nobody_is_in_it()
        {
            using var db = new TaxDb();
            var y2 = NextYear(db, current: true);
            var d = (await Principal(db).GetSummaryAsync(A, 1, null, default)).Data!;
            Assert.Equal("2027-28", d.AcademicYearName);
            Assert.True(d.AcademicYearIsCurrent);
            Assert.Equal(y2, d.AcademicYearId);
            Assert.Equal(0, d.Stats.TotalStudents);                 // enrolled in the viewed year, not the roster of today's sections
            Assert.False(d.HasFeeSetup);
            Assert.Null(d.Stats.FeeTotalDue); Assert.Null(d.Stats.FeeCollected); Assert.Null(d.Stats.FeeOutstanding);
            Assert.Null(d.ViewingNote);
        }

        [DbFact]
        public async Task Principal_can_look_at_another_year_and_says_so_and_never_changes_the_current_year()
        {
            using var db = new TaxDb();
            NextYear(db, current: true);
            var d = (await Principal(db).GetSummaryAsync(A, 1, Y1, default)).Data!;
            Assert.Equal("2026-27", d.AcademicYearName);
            Assert.False(d.AcademicYearIsCurrent);
            Assert.Equal(Students, d.Stats.TotalStudents);
            Assert.True(d.HasFeeSetup);
            Assert.True(d.Stats.FeeTotalDue > 0);
            Assert.True(d.Stats.FeeOutstanding > 0);
            Assert.Equal("2027-28", d.CurrentYearName);
            Assert.Contains("You are viewing 2026-27", d.ViewingNote);
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_academic_years where school_id='{A}' and is_current"));
            Assert.Equal("2027-28", db.Scalar<string>($"select year_name from tb_academic_years where school_id='{A}' and is_current"));
        }

        [DbFact]
        public async Task Principal_gets_404_for_another_schools_year()
        {
            using var db = new TaxDb();
            var r = await Principal(db).GetSummaryAsync(A, 1, db.Scalar<long>($"select min(academic_year_id) from tb_academic_years where school_id='{B}'"), default);
            Assert.Equal(404, r.StatusCode);
            Assert.Null(r.Data);
            Assert.Equal(404, (await Principal(db).GetSummaryAsync(A, 1, 99999999, default)).StatusCode);
        }

        [DbFact]
        public async Task Principal_with_no_year_flagged_current_falls_back_to_the_year_containing_today_and_says_so()
        {
            using var db = new TaxDb();
            db.Exec($"update tb_academic_years set is_current=false where school_id='{A}'");
            var d = (await Principal(db).GetSummaryAsync(A, 1, null, default)).Data!;
            Assert.Equal("2026-27", d.AcademicYearName);
            Assert.Equal(Students, d.Stats.TotalStudents);
            Assert.Contains("No academic year is set as current", d.ViewingNote);
        }

        // ------------------------------------------------------------------------------------------------ accountant
        [DbFact]
        public async Task Accountant_dues_follow_the_viewed_year_while_the_cash_figures_stay_all_years_cash()
        {
            using var db = new TaxDb();
            NextYear(db, current: true);
            db.Exec($@"insert into tb_fee_payments (student_id, school_id, amount, payment_mode, receipt_number, fee_amount, payment_date, status)
                       values ('GHS001ST001', '{A}', 321, 'Cash', 'ZZ-1C', 321, now(), 'Active')");
            var current = (await Accountant(db).GetSummaryAsync(A, null, default)).Data!;          // 2027-28: no fee structure yet
            Assert.Equal("2027-28", current.AcademicYearName);
            Assert.False(current.HasFeeSetup);
            Assert.Null(current.Stats.Outstanding); Assert.Null(current.Stats.OverdueCount); Assert.Null(current.Stats.UpcomingCount);
            Assert.True(current.Stats.CollectedToday >= 321m);    // cash received today, whatever year it paid for
            Assert.NotEmpty(current.RecentPayments);

            var past = (await Accountant(db).GetSummaryAsync(A, Y1, default)).Data!;
            Assert.Equal("2026-27", past.AcademicYearName);
            Assert.True(past.HasFeeSetup);
            Assert.True(past.Stats.Outstanding > 0);
            Assert.Contains("You are viewing 2026-27", past.ViewingNote);
            Assert.Equal(current.Stats.CollectedToday, past.Stats.CollectedToday);                  // the cash tiles do not change with the viewed year
            Assert.Equal(current.Stats.CollectedThisMonth, past.Stats.CollectedThisMonth);
            Assert.Equal(404, (await Accountant(db).GetSummaryAsync(A, db.Scalar<long>($"select min(academic_year_id) from tb_academic_years where school_id='{B}'"), default)).StatusCode);
        }

        // ------------------------------------------------------------------------------------------------ teacher
        [DbFact]
        public async Task The_teacher_dashboard_names_the_year_it_shows()
        {
            using var db = new TaxDb();
            var c = db.Ctx();
            var grid = new TimetableGridDto { AcademicYearId = Y1, AcademicYearName = "2026-27", Scope = "teacher" };
            var svc = new TeacherDashboardService(new TimetableRepository(c), new EmployeeRepository(c), Fake.Of<ITimetableService>(new() { ["GetMyGridAsync"] = Fake.Ok(grid) }),
                Fake.Of<IClassTeacherService>(), Fake.Of<IAttendanceService>(), Fake.Of<ISchoolEventService>(), Fake.Of<INoticeService>(), new TeacherDashboardRepository(c),
                NullLogger<TeacherDashboardService>.Instance);
            var r = await svc.GetAsync(A, 116, default);       // user 116 = GHS001EM001, a teacher of this school
            Assert.Equal(200, r.StatusCode);
            Assert.Equal(Y1, r.Data!.AcademicYearId);
            Assert.Equal("2026-27", r.Data.AcademicYearName);
        }

        // ------------------------------------------------------------------------------------------------ student
        private static StudentDashboardService StudentSvc(TaxDb db)
        {
            var c = db.Ctx();
            return new StudentDashboardService(new StudentRepository(c), Fake.Of<IAttendanceService>(), Fake.Of<ISchoolEventService>(), Fake.Of<INoticeService>(), Fake.Of<ITimetableService>(),
                Fake.Of<IAssessmentService>(), Fake.Of<IStudentLeaveService>(), new SchoolDashboardRepository(c), NullLogger<StudentDashboardService>.Instance);
        }

        [DbFact]
        public async Task A_student_enrolled_in_todays_year_gets_that_year_and_no_note()
        {
            using var db = new TaxDb();
            var r = await StudentSvc(db).GetAsync(A, 179, default);       // user 179 = GHS001ST001
            Assert.Equal(200, r.StatusCode);
            Assert.False(r.Data!.IsReadOnlyYear);
            Assert.Equal("2026-27", r.Data.AcademicYearName);
            Assert.Null(r.Data.ViewingNote);
            Assert.Equal("2026-27", r.Data.Profile.AcademicYearName);
        }

        [DbFact]
        public async Task A_student_promoted_ahead_of_the_calendar_sees_the_latest_enrolment_year_read_only_not_an_error()
        {
            using var db = new TaxDb();
            var y2 = NextYear(db, current: false);
            // move his only enrolment to 2027-28: in another class, in a section that year offers
            var s2 = db.Scalar<long>($"select min(section_id) from tb_sections where school_id='{A}' and class_id <> (select s.class_id from tb_student_enrollments e join tb_sections s on s.section_id=e.class_section_id where e.student_id='GHS001ST001') and status_id=1");
            var c2 = db.Scalar<string>($"select c.class_name from tb_sections s join tb_classes c on c.class_id=s.class_id where s.section_id={s2}");
            db.Exec($"insert into tb_academic_year_classes (school_id, academic_year_id, class_id, status_id) select '{A}', {y2}, class_id, 1 from tb_sections where section_id={s2}");
            db.Exec($"insert into tb_academic_year_sections (school_id, academic_year_id, class_id, section_id, status_id) select '{A}', {y2}, class_id, section_id, 1 from tb_sections where section_id={s2}");
            db.Exec($"update tb_student_enrollments set academic_year_id={y2}, class_section_id={s2}, roll_number=null where student_id='GHS001ST001'");

            var r = await StudentSvc(db).GetAsync(A, 179, default);
            Assert.Equal(200, r.StatusCode);                              // not a 404 / error
            var d = r.Data!;
            Assert.True(d.IsReadOnlyYear);
            Assert.Equal("2027-28", d.AcademicYearName);
            Assert.Contains("not enrolled in 2026-27", d.ViewingNote);
            Assert.Contains("Showing 2027-28", d.ViewingNote);
            Assert.Equal(c2, d.Profile.ClassName);                        // the class of the year shown
            Assert.Empty(d.TodayClasses);
            Assert.Null(d.Attendance);
            Assert.Empty(d.RecentResults);
        }
    }
}
