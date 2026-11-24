using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using smss_api.Controllers;
using smss_api_db_layer.@interface;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>Role restriction, the no-school answer and the failure answer (no database).</summary>
    public class SchoolAdminDashboardControllerTests
    {
        private static DashboardController Make(ClaimsPrincipal user, out object svc)
        {
            var fake = Fake.Of<ISchoolAdminDashboardService>(new() { ["GetAsync"] = Fake.Ok(new SchoolAdminDashboardDto { TotalStudents = 7 }) });
            svc = fake;
            return Fake.WithUser(new DashboardController(fake), user);
        }

        [Fact]
        public async Task School_Admin_gets_the_dashboard_and_the_service_is_called_with_the_token_school()
        {
            string? seen = null;
            var fake = Fake.Of<ISchoolAdminDashboardService>(new() { ["GetAsync"] = (Func<object?[], object?>)(a => { seen = (string?)a[0]; return Fake.Ok(new SchoolAdminDashboardDto()); }) });
            var c = Fake.WithUser(new DashboardController(fake), Fake.User("SCH-A", 1, "School Admin"));
            Assert.Equal(200, Fake.Code(await c.SchoolAdmin(null, default)));
            Assert.Equal("SCH-A", seen);
        }

        [Theory]
        [InlineData("Principal")] [InlineData("Accountant")] [InlineData("Teacher")] [InlineData("Admission Staff")] [InlineData("Student")]
        [InlineData("Guardian")] [InlineData("Driver")] [InlineData("Super Admin")]
        public async Task Every_other_role_gets_403_and_the_service_is_never_reached(string role)
        {
            var c = Make(Fake.User("SCH-A", 1, role), out var svc);
            Assert.Equal(403, Fake.Code(await c.SchoolAdmin(null, default)));
            Assert.DoesNotContain("GetAsync", Fake.ProxyOf(svc).Calls);
        }

        [Fact]
        public async Task No_school_on_the_token_is_a_400_and_a_database_failure_a_safe_500()
        {
            var svc = new SchoolAdminDashboardService(new ThrowingRepo(), new ThrowingAttendance(), NullLogger<SchoolAdminDashboardService>.Instance);
            Assert.Equal(400, (await svc.GetAsync(null, default)).StatusCode);
            var failed = await svc.GetAsync("SCH-A", default);
            Assert.Equal(500, failed.StatusCode);
            Assert.DoesNotContain("boom", failed.Message);
            Assert.Null(failed.Data);
        }

        private sealed class ThrowingRepo : ISchoolAdminDashboardRepository
        {
            public Task<int?> ActiveStatusIdAsync(CancellationToken ct) => throw new InvalidOperationException("boom");
            public Task<DashboardYearRow?> CurrentYearAsync(string s, int a, CancellationToken ct) => throw new InvalidOperationException("boom");
            public Task<DashboardYearRow?> YearAsync(string s, long y, CancellationToken ct) => throw new InvalidOperationException("boom");
            public Task<int> EnrolledStudentsAsync(string s, int a, long y, CancellationToken ct) => throw new InvalidOperationException("boom");
            public Task<bool> HasFeeItemsAsync(string s, long y, CancellationToken ct) => throw new InvalidOperationException("boom");
            public Task<DashboardFeeMonthRow> FeeMonthAsync(string s, int a, long y, DateOnly f, DateOnly t, CancellationToken ct) => throw new InvalidOperationException("boom");
            public Task<decimal> CollectedAsync(string s, long y, DateTime? f, DateTime? t, CancellationToken ct) => throw new InvalidOperationException("boom");
            public Task<int> NewAdmissionsAsync(string s, int a, DateOnly f, DateOnly t, CancellationToken ct) => throw new InvalidOperationException("boom");
            public Task<DashboardEmployeeRow> EmployeesAsync(string s, int a, CancellationToken ct) => throw new InvalidOperationException("boom");
            public Task<int> ActiveVehiclesAsync(string s, int a, CancellationToken ct) => throw new InvalidOperationException("boom");
        }

        private sealed class ThrowingAttendance : ISchoolDashboardRepository
        {
            public Task<List<SchoolAttendanceDayRow>> GetAttendanceByDayAsync(string s, DateOnly f, DateOnly t, string p, CancellationToken ct) => throw new InvalidOperationException("boom");
            public Task<List<SchoolYearSpanRow>> GetYearsOverlappingAsync(string s, DateOnly f, DateOnly t, CancellationToken ct) => throw new InvalidOperationException("boom");
        }
    }

    /// <summary>
    /// Every figure with known seeded data, on a real PostgreSQL (the scratch server, see TaxTestKit). Skipped without SMSS_TEST_PG.
    /// School ZZDA is seeded by hand below; ZZDB is a second school whose rows must never show up in ZZDA's numbers.
    /// Month under test: November 2026 (30 days); session 2026-04-01..2027-03-31.
    /// </summary>
    public class SchoolAdminDashboardDbTests
    {
        private const string A = "ZZDA00000001", B = "ZZDB00000001";
        private static readonly DateOnly LastDay = new(2026, 11, 30);
        private const string Active = "(select sid from lut_status where sname='Active' and stype='general status')";
        private const string Inactive = "(select sid from lut_status where sname='Inactive' and stype='general status')";

        private static SchoolAdminDashboardService Svc(TaxDb db) { var c = db.Ctx(); return new(new SchoolAdminDashboardRepository(c), new SchoolDashboardRepository(c), NullLogger<SchoolAdminDashboardService>.Instance); }
        private static async Task<SchoolAdminDashboardDto> Get(TaxDb db, string school, DateOnly today)
        {
            var r = await Svc(db).GetAsync(school, default, today);
            Assert.True(r.Status, r.Message);
            return r.Data!;
        }

        private static void School(TaxDb db, string id) =>
            db.Exec($"insert into tb_schools (school_id, school_code, school_name, school_status_id) values ('{id}', '{id}-C', 'Dash test {id}', {Active})");

        private static void Year(TaxDb db, string school) =>
            db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('{school}', '2026-27', '2026-04-01', '2027-03-31', true, {Active})");

        private static void Student(TaxDb db, string school, string id, string type, string admitted, bool active = true) =>
            db.Exec($@"insert into tb_students (student_id, school_id, admission_number, first_name, last_name, date_of_birth, admission_type_id, admission_date, status_id)
                       values ('{id}', '{school}', '{id}-N', 'Stu', '{id}', '2015-01-01', (select admission_type_id from lut_admission_type where type_code='{type}'), '{admitted}', {(active ? Active : Inactive)})");

        private static void FeeItem(TaxDb db, string school, string student)
        {
            db.Exec($@"insert into tb_fee_types (school_id, fee_type_name, fee_type_code, is_optional, isactive) select '{school}', 'Tuition', 'T{student}', false, true where not exists (select 1 from tb_fee_types where school_id='{school}' and fee_type_code='T{student}')");
            db.Exec($@"insert into tb_student_fee_items (student_id, school_id, academic_year_id, fee_type_id, frequency_id, base_amount, net_amount)
                       values ('{student}', '{school}', (select academic_year_id from tb_academic_years where school_id='{school}'),
                               (select fee_type_id from tb_fee_types where school_id='{school}' and fee_type_code='T{student}'), (select min(frequency_id) from lut_fee_frequency), 1000, 1000)");
        }

        private static void Due(TaxDb db, string school, string student, string due, decimal amount, decimal paid)
        {
            if (db.Scalar<int>($"select count(*) from tb_student_fee_items where student_id='{student}'") == 0) FeeItem(db, school, student);
            db.Exec($@"insert into tb_student_fee_dues (student_fee_item_id, student_id, school_id, due_date, amount_due, amount_paid)
                       values ((select min(student_fee_item_id) from tb_student_fee_items where student_id='{student}'), '{student}', '{school}', '{due}', {amount}, {paid})");
        }

        private static int _receipt;
        /// <summary>A payment made at the given UTC instant, allocated to the student's due on dueDate (cash = allocated - discounted).</summary>
        private static void Pay(TaxDb db, string school, string student, string dueDate, string utc, decimal allocated, decimal discount = 0, string status = "Active")
        {
            var n = Interlocked.Increment(ref _receipt);
            var voided = status == "Voided" ? ", voided_by, voided_at, void_reason" : "";
            var voidVals = status == "Voided" ? ", 4, now(), 'test'" : "";
            db.Exec($@"insert into tb_fee_payments (student_id, school_id, amount, payment_mode, receipt_number, fee_amount, payment_date, status{voided})
                       values ('{student}', '{school}', {allocated}, 'Cash', 'ZZ-{n}', {allocated}, '{utc}+00', '{status}'{voidVals})");
            db.Exec($@"insert into tb_fee_payment_allocations (payment_id, fee_due_id, allocated_amount, concession_amount, fee_type_name_snapshot)
                       values ((select payment_id from tb_fee_payments where school_id='{school}' and receipt_number='ZZ-{n}'),
                               (select fee_due_id from tb_student_fee_dues where student_id='{student}' and due_date='{dueDate}'), {allocated}, {discount}, 'Tuition')");
        }

        private static void Seed(TaxDb db)
        {
            School(db, A); School(db, B); Year(db, A); Year(db, B);

            // students: admission type and date drive "new admissions this session" (2026-04-01..2027-03-31, both ends inclusive)
            Student(db, A, "ZDA1", "NEW_ADMISSION", "2026-06-01");
            Student(db, A, "ZDA2", "NEW_ADMISSION", "2026-03-31");      // the day before the session
            Student(db, A, "ZDA3", "NEW_ADMISSION", "2027-03-31");      // the last day of the session
            Student(db, A, "ZDA4", "EXISTING_STUDENT", "2026-07-01");   // entered as existing: not a new admission
            Student(db, A, "ZDA5", "NEW_ADMISSION", "2026-08-01");
            Student(db, A, "ZDA6", "NEW_ADMISSION", "2026-09-01", active: false);   // inactive: in no count
            Student(db, A, "ZDA7", "NEW_ADMISSION", "2027-04-01");      // the day after the session
            Student(db, B, "ZDB1", "NEW_ADMISSION", "2026-06-01");

            // dues of November 2026
            Due(db, A, "ZDA1", "2026-11-01", 1000, 1000);   // first day of the month, fully paid
            Due(db, A, "ZDA1", "2026-11-30", 500, 500);     // last day of the month, fully paid     -> ZDA1 paid
            Due(db, A, "ZDA2", "2026-11-30", 500, 0);       // due on the last day, unpaid          -> not paid
            Due(db, A, "ZDA3", "2026-11-05", 1000, 400);    // part-paid                            -> not paid, owes 600
            Due(db, A, "ZDA4", "2026-11-10", 800, 800);     // cleared by a discount (600 cash + 200 discount) -> paid
            Due(db, A, "ZDA5", "2026-10-31", 300, 0);       // the day before the month
            Due(db, A, "ZDA5", "2026-12-01", 300, 0);       // the day after the month              -> no November due: in neither card
            Due(db, A, "ZDA6", "2026-11-03", 999, 0);       // inactive student
            Due(db, B, "ZDB1", "2026-11-03", 7000, 0);      // another school, unpaid

            // payments (UTC instants; India is UTC+5:30)
            Pay(db, A, "ZDA1", "2026-11-01", "2026-10-31 19:00:00", 1000);          // 00:30 IST on 1 Nov: this month
            Pay(db, A, "ZDA1", "2026-11-30", "2026-11-30 18:00:00", 500);           // 23:30 IST on 30 Nov: the last evening, still this month
            Pay(db, A, "ZDA3", "2026-11-05", "2026-11-06 06:00:00", 400);
            Pay(db, A, "ZDA4", "2026-11-10", "2026-11-10 06:00:00", 800, discount: 200);   // cash 600
            Pay(db, A, "ZDA2", "2026-11-30", "2026-11-07 06:00:00", 300, status: "Voided"); // voided: never counted
            Pay(db, A, "ZDA5", "2026-10-31", "2026-10-31 17:30:00", 200);           // 23:00 IST on 31 Oct: last month, this session
            Pay(db, A, "ZDA5", "2026-12-01", "2026-11-30 18:40:00", 100);           // 00:10 IST on 1 Dec: next month
            Pay(db, B, "ZDB1", "2026-11-03", "2026-11-10 06:00:00", 5000);          // another school

            // attendance (needs a class and a section)
            foreach (var s in new[] { A, B })
            {
                db.Exec($"insert into tb_classes (school_id, class_name, sequence_order, status_id) values ('{s}', 'C1', 1, {Active})");
                db.Exec($"insert into tb_sections (school_id, class_id, section_name, status_id) select '{s}', class_id, 'A', {Active} from tb_classes where school_id='{s}'");
            }
            // enrolments: "Total students" counts students ENROLLED in the viewed year, so every active student gets an enrolment in the school's year
            foreach (var s in new[] { A, B })
            {
                db.Exec($"insert into tb_academic_year_classes (school_id, academic_year_id, class_id, status_id) select '{s}', (select academic_year_id from tb_academic_years where school_id='{s}'), class_id, {Active} from tb_classes where school_id='{s}'");
                db.Exec($@"insert into tb_academic_year_sections (school_id, academic_year_id, class_id, section_id, status_id)
                           select '{s}', (select academic_year_id from tb_academic_years where school_id='{s}'), class_id, section_id, {Active} from tb_sections where school_id='{s}'");
                db.Exec($@"insert into tb_student_enrollments (school_id, student_id, academic_year_id, class_section_id, enrollment_date, admission_type_id, status_id)
                           select '{s}', st.student_id, (select academic_year_id from tb_academic_years where school_id='{s}'), (select min(section_id) from tb_sections where school_id='{s}'), '2026-06-01',
                                  st.admission_type_id, {Active} from tb_students st where st.school_id='{s}' and st.status_id={Active}");
            }
            void Mark(string school, string student, string date, string code) =>
                db.Exec($@"insert into tb_student_attendance (school_id, academic_year_id, section_id, class_id, student_id, attendance_date, attendance_status_id, marked_by)
                           select '{school}', (select academic_year_id from tb_academic_years where school_id='{school}'), s.section_id, s.class_id, '{student}', '{date}',
                                  (select attendance_status_id from lut_attendance_status where status_code='{code}'), 4 from tb_sections s where s.school_id='{school}'");
            Mark(A, "ZDA1", "2026-11-30", "PRESENT"); Mark(A, "ZDA2", "2026-11-30", "PRESENT"); Mark(A, "ZDA3", "2026-11-30", "ABSENT");
            Mark(A, "ZDA1", "2026-11-29", "ABSENT");                                   // another day
            Mark(B, "ZDB1", "2026-11-30", "PRESENT");                                  // another school

            // employees: grouped by employee type and designation name; the inactive one is in no count
            db.Exec($"insert into tb_employee_departments (school_id, department_name, status_id) values ('{A}', 'Dept', {Active}), ('{B}', 'Dept', {Active})");
            int e = 0;
            void Designation(string school, string name) => db.Exec($"insert into tb_employee_designations (school_id, designation_name, status_id) values ('{school}', '{name}', {Active})");
            void Emp(string school, string designation, string type, bool active = true) =>
                db.Exec($@"insert into tb_employees (employee_id, school_id, designation_id, department_id, employment_status_id, employee_type, first_name, last_name, date_of_birth, mobile_number, address, date_of_joining, status_id)
                           values ('ZE{school[^2..]}{++e:D3}', '{school}', (select designation_id from tb_employee_designations where school_id='{school}' and designation_name='{designation}'),
                                   (select department_id from tb_employee_departments where school_id='{school}'), (select min(employment_status_id) from lut_employment_status), '{type}', 'E', 'E{e}', '1990-01-01', '9876543210', 'x', '2020-01-01', {(active ? Active : Inactive)})");
            foreach (var n in new[] { "Class Teacher", "Senior Accountant", "Admission Counsellor", "School Driver", "Peon", "Teacher" }) Designation(A, n);
            Designation(B, "Teacher");
            Emp(A, "Class Teacher", "Teaching"); Emp(A, "Teacher", "Teaching"); Emp(A, "Teacher", "Teaching", active: false);
            Emp(A, "Senior Accountant", "Non-Teaching"); Emp(A, "Admission Counsellor", "Non-Teaching"); Emp(A, "School Driver", "Non-Teaching"); Emp(A, "Peon", "Non-Teaching");
            Emp(B, "Teacher", "Teaching");

            // vehicles
            void Vehicle(string school, string plate, bool active = true) =>
                db.Exec($@"insert into tb_vehicles (school_id, vehicle_label, registration_number, vehicle_type_id, ownership_id, status_id)
                           values ('{school}', '{plate}', '{plate}', (select min(vehicle_type_id) from lut_vehicle_type), (select min(ownership_id) from lut_vehicle_ownership), {(active ? Active : Inactive)})");
            Vehicle(A, "ZZ01A0001"); Vehicle(A, "ZZ01A0002"); Vehicle(A, "ZZ01A0003", active: false); Vehicle(B, "ZZ01B0001");
        }

        [DbFact]
        public async Task Month_and_session_come_from_today_in_India_and_the_current_academic_year()
        {
            using var db = new TaxDb();
            Seed(db);
            var d = await Get(db, A, LastDay);

            Assert.Equal(LastDay, d.Today);
            Assert.Equal(new DateOnly(2026, 11, 1), d.MonthStart);
            Assert.Equal(LastDay, d.MonthEnd);
            Assert.Equal("2026-27", d.Session!.Name);
            Assert.Equal(new DateOnly(2026, 4, 1), d.Session.StartDate);
            Assert.Equal(new DateOnly(2027, 3, 31), d.Session.EndDate);
        }

        [DbFact]
        public async Task Students_and_fees_cards()
        {
            using var db = new TaxDb();
            Seed(db);
            var d = await Get(db, A, LastDay);

            Assert.Equal(6, d.TotalStudents);                  // ZDA1,2,3,4,5,7 (ZDA6 is inactive; the other school's student is not counted)
            Assert.Equal(2, d.StudentsPaidThisMonth);          // ZDA1 (dues on the 1st and the 30th) and ZDA4 (cleared by a discount)
            Assert.Equal(2, d.StudentsNotPaidThisMonth);       // ZDA2 (due on the last day, unpaid) and ZDA3 (part-paid)
            // ZDA5 has dues only in October and December, ZDA7 none, ZDA6 is inactive: all in neither card
            Assert.Equal(500m + 600m, d.FeeOutstandingThisMonth);
        }

        [DbFact]
        public async Task Collected_is_cash_net_of_discount_by_India_date_and_excludes_voided_and_other_schools()
        {
            using var db = new TaxDb();
            Seed(db);
            var d = await Get(db, A, LastDay);

            // 1000 (00:30 IST on the 1st) + 500 (23:30 IST on the 30th) + 400 + 600 (800 less the 200 discount); the voided 300, the 23:00 IST 31 Oct 200,
            // the 00:10 IST 1 Dec 100 and the other school's 5000 are not in November
            Assert.Equal(2500m, d.FeeCollectedThisMonth);
            Assert.Equal(2500m, d.IncomeThisMonth);
            // the SESSION figure is everything received against this year's installments, whenever it was paid: November's 2500, the 200 paid at 23:00 IST on 31 Oct and the 100 paid
            // at 00:10 IST on 1 Dec (the voided 300 and the other school's 5000 are never in it)
            Assert.Equal(2800m, d.IncomeThisSession);
        }

        [DbFact]
        public async Task A_payment_after_today_is_not_counted_and_dues_later_this_month_still_are()
        {
            using var db = new TaxDb();
            Seed(db);
            var d = await Get(db, A, new DateOnly(2026, 11, 15));   // mid month: the 30 Nov payment is in the future of this "today"
            Assert.Equal(1000m + 400m + 600m, d.FeeCollectedThisMonth);
            Assert.Equal(1100m, d.FeeOutstandingThisMonth);        // dues are by month, not "till today"
            Assert.Equal(new DateOnly(2026, 11, 30), d.MonthEnd);
        }

        [DbFact]
        public async Task The_day_after_the_month_starts_a_new_month_with_only_its_own_dues_and_payments()
        {
            using var db = new TaxDb();
            Seed(db);
            var d = await Get(db, A, new DateOnly(2026, 12, 1));
            Assert.Equal(new DateOnly(2026, 12, 1), d.MonthStart);
            Assert.Equal(new DateOnly(2026, 12, 31), d.MonthEnd);
            Assert.Equal(0, d.StudentsPaidThisMonth);
            Assert.Equal(1, d.StudentsNotPaidThisMonth);           // ZDA5's 1 Dec due
            Assert.Equal(300m, d.FeeOutstandingThisMonth);
            Assert.Equal(100m, d.FeeCollectedThisMonth);           // the 00:10 IST payment on 1 Dec
        }

        [DbFact]
        public async Task Money_card_flags_say_expenses_and_other_income_are_not_set_up()
        {
            using var db = new TaxDb();
            Seed(db);
            var d = await Get(db, A, LastDay);
            Assert.False(d.ExpensesSetUp);
            Assert.Null(d.ExpensesThisMonth);
            Assert.Null(d.ExpensesThisSession);
            Assert.False(d.OtherIncomeSetUp);
        }

        [DbFact]
        public async Task People_cards()
        {
            using var db = new TaxDb();
            Seed(db);
            var d = await Get(db, A, LastDay);

            Assert.Equal(2, d.AttendancePresentToday);          // today's marks only: 2 present of 3 marked
            Assert.Equal(3, d.AttendanceMarkedToday);
            Assert.Equal(3, d.NewAdmissionsThisSession);        // ZDA1, ZDA3 (last day of the session), ZDA5; not the day before / after, not existing, not inactive

            Assert.Equal(6, d.Employees.Total);
            Assert.Equal(2, d.Employees.Teachers);
            Assert.Equal(1, d.Employees.Accountants);
            Assert.Equal(1, d.Employees.AdmissionStaff);
            Assert.Equal(1, d.Employees.Drivers);
            Assert.Equal(1, d.Employees.Other);
            Assert.Equal(2, d.ActiveVehicles);
        }

        [DbFact]
        public async Task Nothing_marked_today_reads_as_zero_marked_so_the_client_can_say_not_marked_yet()
        {
            using var db = new TaxDb();
            Seed(db);
            var d = await Get(db, A, new DateOnly(2026, 11, 28));
            Assert.Equal(0, d.AttendanceMarkedToday);
            Assert.Equal(0, d.AttendancePresentToday);
        }

        [DbFact]
        public async Task A_school_never_counts_another_schools_rows()
        {
            using var db = new TaxDb();
            Seed(db);
            var b = await Get(db, B, LastDay);
            Assert.Equal(1, b.TotalStudents);
            Assert.Equal(0, b.StudentsPaidThisMonth);
            Assert.Equal(1, b.StudentsNotPaidThisMonth);
            Assert.Equal(7000m, b.FeeOutstandingThisMonth);
            Assert.Equal(5000m, b.FeeCollectedThisMonth);
            Assert.Equal(1, b.AttendancePresentToday);
            Assert.Equal(1, b.AttendanceMarkedToday);
            Assert.Equal(1, b.NewAdmissionsThisSession);
            Assert.Equal(1, b.Employees.Total);
            Assert.Equal(1, b.Employees.Teachers);
            Assert.Equal(1, b.ActiveVehicles);
        }

        [DbFact]
        public async Task The_class_fee_report_month_view_agrees_with_the_not_paid_card()
        {
            using var db = new TaxDb();
            Seed(db);
            var year = db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id='{A}'");
            var classId = db.Scalar<long>($"select class_id from tb_classes where school_id='{A}'");
            // the seed already enrolled every active student of the school in its year (stage 1b counts enrolments)

            var month = (await db.Reports().GetClassReportAsync(A, classId, year, null, true, default, new DateOnly(2026, 11, 17))).Data!;
            Assert.Equal("2026-11", month.Month);
            Assert.Equal(new[] { "ZDA1", "ZDA2", "ZDA3", "ZDA4" }, month.Students.Select(r => r.StudentId).OrderBy(x => x));   // ZDA4 is enrolled by the seed and cleared by a discount; ZDA5 has no due in November
            Assert.Equal(1100m, month.TotalOutstanding);                                                              // the dashboard's outstanding figure
            Assert.Equal(2, month.Students.Count(r => r.Outstanding > 0));                                            // the dashboard's "not paid" count

            var whole = (await db.Reports().GetClassReportAsync(A, classId, year, null, true, default)).Data!;
            Assert.Null(whole.Month);
            Assert.Equal(6, whole.Students.Count);   // the whole-year view lists every enrolled student: the six active ones the seed enrolled (ZDA6 is inactive)
        }

        [DbFact]
        public async Task A_school_with_no_academic_year_has_no_session_figures_and_one_with_nothing_has_zeros()
        {
            using var db = new TaxDb();
            School(db, "ZZDC00000001");
            var d = await Get(db, "ZZDC00000001", LastDay);
            Assert.Null(d.Session);
            Assert.Null(d.IncomeThisSession);
            Assert.Null(d.NewAdmissionsThisSession);
            Assert.Equal(0, d.TotalStudents);
            Assert.Equal(0, d.StudentsPaidThisMonth);
            Assert.Equal(0, d.StudentsNotPaidThisMonth);
            Assert.Equal(0m, d.FeeCollectedThisMonth);
            Assert.Equal(0, d.ActiveVehicles);
            Assert.Equal(0, d.Employees.Total);
        }

        // ---------------------------------------------------------------- stage 1b: the year being viewed
        private static long YearOf(TaxDb db, string school, string name) => db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id='{school}' and year_name='{name}'");

        private static long NextYear(TaxDb db, string school)
        {
            db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('{school}', '2027-28', '2027-04-01', '2028-03-31', false, {Active})");
            return YearOf(db, school, "2027-28");
        }

        [DbFact]
        public async Task A_year_nobody_is_enrolled_in_shows_zero_students_and_no_fee_structure_not_the_old_years_numbers()
        {
            using var db = new TaxDb();
            Seed(db);
            var next = NextYear(db, A);
            // the school switches its current year to 2027-28, with nobody moved into it yet (the LAS case)
            db.Exec($"update tb_academic_years set is_current=false where school_id='{A}' and year_name='2026-27'");
            db.Exec($"update tb_academic_years set is_current=true where academic_year_id={next}");
            var d = await Get(db, A, new DateOnly(2027, 5, 10));
            Assert.Equal("2027-28", d.Session!.Name);
            Assert.True(d.Session.IsCurrent);
            Assert.Equal(0, d.TotalStudents);
            Assert.False(d.HasFeeSetup);
            Assert.Equal(0m, d.FeeCollectedThisMonth); Assert.Equal(0m, d.FeeOutstandingThisMonth);
            Assert.Equal(0, d.StudentsPaidThisMonth + d.StudentsNotPaidThisMonth);
            Assert.False(d.MonthClamped);

            // looking back at 2026-27 shows its own numbers, and does not change which year is current
            var old = (await Svc(db).GetAsync(A, default, new DateOnly(2026, 11, 30), YearOf(db, A, "2026-27"))).Data!;
            Assert.Equal(6, old.TotalStudents); Assert.True(old.HasFeeSetup); Assert.False(old.Session!.IsCurrent);
            Assert.Equal("2027-28", old.CurrentYearName);
            Assert.Equal(2, old.StudentsPaidThisMonth); Assert.Equal(2, old.StudentsNotPaidThisMonth);
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_academic_years where school_id='{A}' and is_current"));
            Assert.True(db.Scalar<bool>($"select is_current from tb_academic_years where academic_year_id={next}"));
        }

        [DbFact]
        public async Task Another_schools_year_is_a_404_and_never_leaks()
        {
            using var db = new TaxDb();
            Seed(db);
            var r = await Svc(db).GetAsync(A, default, LastDay, YearOf(db, B, "2026-27"));
            Assert.Equal(404, r.StatusCode);
            Assert.Null(r.Data);
            Assert.Equal(404, (await Svc(db).GetAsync(A, default, LastDay, 99999999)).StatusCode);
        }

        [DbFact]
        public async Task This_month_is_moved_into_the_viewed_year_when_today_is_outside_it_and_the_note_says_so()
        {
            using var db = new TaxDb();
            Seed(db);
            var after = await Get(db, A, new DateOnly(2027, 6, 15));       // today is after 2026-27
            Assert.True(after.MonthClamped);
            Assert.Equal(new DateOnly(2027, 3, 1), after.MonthStart); Assert.Equal(new DateOnly(2027, 3, 31), after.MonthEnd);
            Assert.Contains("March 2027", after.MonthNote); Assert.Contains("after the academic year 2026-27", after.MonthNote);
            var before = await Get(db, A, new DateOnly(2026, 2, 3));       // today is before it starts
            Assert.True(before.MonthClamped);
            Assert.Equal(new DateOnly(2026, 4, 1), before.MonthStart);
            Assert.Contains("before the academic year", before.MonthNote);
            var inside = await Get(db, A, LastDay);
            Assert.False(inside.MonthClamped); Assert.Null(inside.MonthNote);
        }

        [DbFact]
        public async Task Fees_received_count_only_the_viewed_years_installments_not_every_rupee_received()
        {
            using var db = new TaxDb();
            Seed(db);
            var next = NextYear(db, A);
            // a 2027-28 fee item with an installment due 5 Apr 2027 for ZDA1, paid on 5 Nov 2026 (an advance payment)
            db.Exec($@"insert into tb_student_fee_items (student_id, school_id, academic_year_id, fee_type_id, frequency_id, base_amount, net_amount)
                       values ('ZDA1', '{A}', {next}, (select fee_type_id from tb_fee_types where school_id='{A}' and fee_type_code='TZDA1'), (select min(frequency_id) from lut_fee_frequency), 700, 700)");
            db.Exec($@"insert into tb_student_fee_dues (student_fee_item_id, student_id, school_id, due_date, amount_due, amount_paid)
                       values ((select max(student_fee_item_id) from tb_student_fee_items where student_id='ZDA1' and academic_year_id={next}), 'ZDA1', '{A}', '2027-04-05', 700, 700)");
            db.Exec($@"insert into tb_fee_payments (student_id, school_id, amount, payment_mode, receipt_number, fee_amount, payment_date, status) values ('ZDA1', '{A}', 700, 'Cash', 'ZZ-ADV', 700, '2026-11-05 06:00:00+00', 'Active')");
            db.Exec($@"insert into tb_fee_payment_allocations (payment_id, fee_due_id, allocated_amount, concession_amount, fee_type_name_snapshot)
                       values ((select payment_id from tb_fee_payments where receipt_number='ZZ-ADV'), (select max(fee_due_id) from tb_student_fee_dues where student_id='ZDA1'), 700, 0, 'Tuition')");

            var thisYear = await Get(db, A, LastDay);
            Assert.Equal(2500m, thisYear.FeeCollectedThisMonth);          // the 700 advance is for 2027-28 installments: not here
            var nextYear = (await Svc(db).GetAsync(A, default, LastDay, next)).Data!;
            Assert.Equal(700m, nextYear.IncomeThisSession);               // it shows against the year it was paid for
            Assert.True(nextYear.HasFeeSetup);
        }
    }
}
