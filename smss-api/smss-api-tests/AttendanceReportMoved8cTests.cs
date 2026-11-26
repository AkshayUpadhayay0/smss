using Microsoft.Extensions.Logging.Abstractions;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// The monthly attendance report lists students from the attendance rows' OWN section (they store it), not only from the current enrolment: a student who moved sections mid-month appears in BOTH
    /// sections' reports, each only for the days they were there. [DbFact]: template smss_tpl_8c. School A, year 41, Class 5: sections A (60) and B (61).
    /// </summary>
    public class AttendanceReportMoved8cTests
    {
        private const string A = TaxDb.SchoolA;
        private const long UserA = TaxDb.UserA;
        private const long SecA = 60, SecB = 61, Class5 = 54;
        private static string Q(string s) => "'" + s.Replace("'", "''") + "'";

        private static AttendanceService Attendance(TaxDb db) => new(new AttendanceRepository(db.Ctx()), Fake.Of<IClassTeacherService>(), NullLogger<AttendanceService>.Instance);
        private static SectionChangeService Sections(TaxDb db) => new(new SectionChangeRepository(db.Ctx()), NullLogger<SectionChangeService>.Instance);

        private static string Student(TaxDb db, int n = 0) =>
            db.Scalar<string>($"select student_id from tb_student_enrollments where school_id = {Q(A)} and academic_year_id = {TaxDb.YearA} and class_section_id = {SecA} and outcome is null order by student_id offset {n} limit 1");

        private static void Mark(TaxDb db, string student, long section, string date, string code) =>
            db.Exec($@"insert into tb_student_attendance (school_id, academic_year_id, section_id, class_id, student_id, attendance_date, attendance_status_id, marked_by, marked_at)
                       values ({Q(A)}, {TaxDb.YearA}, {section}, {Class5}, {Q(student)}, date '{date}', (select attendance_status_id from lut_attendance_status where status_code = '{code}'), {UserA}, now())");

        private static async Task<MonthlyAttendanceReportDto> Report(TaxDb db, long section)
        {
            var r = await Attendance(db).GetMonthlyReportAsync(A, UserA, true, section, 2026, 10, TaxDb.YearA, default);
            Assert.True(r.Status, r.Message);
            return r.Data!;
        }

        private static string Cell(MonthlyAttendanceReportDto r, string student, int day) => r.Students.Single(s => s.StudentId == student).Marks[day - 1];

        /// <summary>A student marked in A on 5 to 9 October (Mon to Fri), moved to B effective 12 October, marked in B on 12 to 16 October.</summary>
        private static async Task<string> MovedMidMonth(TaxDb db)
        {
            var s = Student(db);
            db.Exec($"update tb_student_enrollments set enrollment_date = date '2026-04-01' where school_id = {Q(A)} and student_id = {Q(s)} and academic_year_id = {TaxDb.YearA}");
            foreach (var d in new[] { "2026-10-05", "2026-10-06", "2026-10-07", "2026-10-08" }) Mark(db, s, SecA, d, "PRESENT");
            Mark(db, s, SecA, "2026-10-09", "ABSENT");
            var moved = await Sections(db).ChangeAsync(A, UserA, new ChangeSectionRequest { StudentId = s, ToSectionId = SecB, Reason = "Timetable clash with tuition", EffectiveDate = new DateOnly(2026, 10, 12), Confirm = true }, default);
            Assert.True(moved.Status, moved.Message);
            foreach (var d in new[] { "2026-10-12", "2026-10-13", "2026-10-14", "2026-10-15" }) Mark(db, s, SecB, d, "PRESENT");
            Mark(db, s, SecB, "2026-10-16", "ABSENT");
            return s;
        }

        [DbFact]
        public async Task A_student_moved_mid_month_is_in_both_sections_reports_for_the_right_dates_once_each()
        {
            using var db = new TaxDb(); var s = await MovedMidMonth(db);
            var a = await Report(db, SecA); var b = await Report(db, SecB);
            Assert.Single(a.Students, x => x.StudentId == s); Assert.Single(b.Students, x => x.StudentId == s);                    // no duplicates, in each report once
            // section A: the days they were there are marked, the days after the move are not theirs
            Assert.Equal(new[] { "Present", "Present", "Present", "Present", "Absent" }, Enumerable.Range(5, 5).Select(d => Cell(a, s, d)));
            Assert.All(Enumerable.Range(12, 5), d => Assert.Equal("NotEnrolled", Cell(a, s, d)));
            var ra = a.Students.Single(x => x.StudentId == s);
            Assert.Equal(4, ra.PresentDays); Assert.Equal(1, ra.AbsentDays); Assert.Equal(80.0m, ra.Percentage);
            // section B: the days before the move are not theirs, the days after are marked
            Assert.All(Enumerable.Range(1, 11), d => Assert.Equal("NotEnrolled", Cell(b, s, d)));
            Assert.Equal(new[] { "Present", "Present", "Present", "Present", "Absent" }, Enumerable.Range(12, 5).Select(d => Cell(b, s, d)));
            var rb = b.Students.Single(x => x.StudentId == s);
            Assert.Equal(4, rb.PresentDays); Assert.Equal(1, rb.AbsentDays); Assert.Equal(80.0m, rb.Percentage);
            // a working day after the move that nobody marked counts as "not marked" in B only, never in A
            Assert.Equal("NotMarked", Cell(b, s, 19)); Assert.Equal("NotEnrolled", Cell(a, s, 19));
            Assert.Equal(4, ra.NotMarkedDays); Assert.True(rb.NotMarkedDays >= 5);   // A: the working days 1, 2, 3 and 10 October nobody marked
            Assert.Equal(b.Students.Count(x => x.StudentId == s) + a.Students.Count(x => x.StudentId == s), 2);
        }

        [DbFact]
        public async Task The_attendance_rows_are_unchanged_and_other_students_and_an_unmoved_students_report_are_exactly_as_before()
        {
            using var db = new TaxDb(); var s = Student(db); var other = Student(db, 1);
            foreach (var d in new[] { "2026-10-05", "2026-10-06" }) { Mark(db, s, SecA, d, "PRESENT"); Mark(db, other, SecA, d, "PRESENT"); }
            var beforeReport = await Report(db, SecA);
            var otherBefore = System.Text.Json.JsonSerializer.Serialize(beforeReport.Students.Single(x => x.StudentId == other));
            var rowsBefore = db.Scalar<string>("select md5(string_agg(concat_ws('|', a.*), ',' order by attendance_id)) from tb_student_attendance a");
            Assert.True((await Sections(db).ChangeAsync(A, UserA, new ChangeSectionRequest { StudentId = s, ToSectionId = SecB, Reason = "Timetable clash with tuition", EffectiveDate = new DateOnly(2026, 10, 12), Confirm = true }, default)).Status);
            Assert.Equal(rowsBefore, db.Scalar<string>("select md5(string_agg(concat_ws('|', a.*), ',' order by attendance_id)) from tb_student_attendance a"));   // no attendance row was touched
            var after = await Report(db, SecA);
            Assert.Equal(otherBefore, System.Text.Json.JsonSerializer.Serialize(after.Students.Single(x => x.StudentId == other)));                    // a student who did not move: identical
            Assert.Equal(beforeReport.Students.Count, after.Students.Count);                                                                         // the moved one is still listed here for the days they were
            Assert.Equal("Present", Cell(after, s, 5)); Assert.Equal("Present", Cell(after, s, 6));
        }

        [DbFact]
        public async Task A_student_who_moved_back_is_one_row_with_both_stretches_and_a_student_moved_after_the_month_is_unaffected()
        {
            using var db = new TaxDb(); var s = Student(db); var late = Student(db, 1);
            db.Exec($"update tb_student_enrollments set enrollment_date = date '2026-04-01' where student_id in ({Q(s)}, {Q(late)}) and academic_year_id = {TaxDb.YearA}");
            Mark(db, s, SecA, "2026-10-05", "PRESENT"); Mark(db, late, SecA, "2026-10-05", "PRESENT");
            Assert.True((await Sections(db).ChangeAsync(A, UserA, new ChangeSectionRequest { StudentId = s, ToSectionId = SecB, Reason = "Timetable clash with tuition", EffectiveDate = new DateOnly(2026, 10, 12), Confirm = true }, default)).Status);
            Mark(db, s, SecB, "2026-10-12", "PRESENT");
            Assert.True((await Sections(db).ChangeAsync(A, UserA, new ChangeSectionRequest { StudentId = s, ToSectionId = SecA, Reason = "Moved back to the first section", EffectiveDate = new DateOnly(2026, 10, 19), Confirm = true }, default)).Status);
            Mark(db, s, SecA, "2026-10-20", "PRESENT");
            var a = await Report(db, SecA); var b = await Report(db, SecB);
            Assert.Single(a.Students, x => x.StudentId == s);
            Assert.Equal("Present", Cell(a, s, 5)); Assert.Equal("NotEnrolled", Cell(a, s, 13)); Assert.Equal("Present", Cell(a, s, 20));        // A: 1 to 11, and from the 19th
            Assert.Equal("Present", Cell(b, s, 12)); Assert.Equal("NotEnrolled", Cell(b, s, 5)); Assert.Equal("NotEnrolled", Cell(b, s, 21));    // B: only 12 to 18
            // moved into B only AFTER the month: still wholly in A's report, not in B's
            Assert.True((await Sections(db).ChangeAsync(A, UserA, new ChangeSectionRequest { StudentId = late, ToSectionId = SecB, Reason = "Timetable clash with tuition", EffectiveDate = new DateOnly(2026, 11, 2), Confirm = true }, default)).Status);
            var a2 = await Report(db, SecA); var b2 = await Report(db, SecB);
            Assert.Equal("Present", Cell(a2, late, 5)); Assert.Equal("NotMarked", Cell(a2, late, 14));                                          // still in A all of October
            Assert.DoesNotContain(b2.Students, x => x.StudentId == late);
        }
    }
}
