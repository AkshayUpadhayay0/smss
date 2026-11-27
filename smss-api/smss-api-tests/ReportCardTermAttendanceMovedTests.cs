using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.helper.reportcard;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// The report card's AUTO term attendance counts a student's days from the attendance rows themselves (they store their own section): a student who changed sections during the term keeps the days they were
    /// marked in the earlier section. [DbFact]: template smss_tpl_8c. School A, year 41, scheme 19 (Class 5, Term I 1 Apr to 29 Sep 2026), sections A (60) and B (61).
    /// </summary>
    public class ReportCardTermAttendanceMovedTests
    {
        private const string A = TaxDb.SchoolA;
        private const long UserA = TaxDb.UserA;
        private const long SecA = 60, SecB = 61, Scheme = 19, Term1 = 35, Class5 = 54;
        private static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        private const string ActiveSql = "(select sid from lut_status where sname = 'Active' and stype = 'general status')";

        private static SectionChangeService Sections(TaxDb db) => new(new SectionChangeRepository(db.Ctx()), NullLogger<SectionChangeService>.Instance);

        private static ReportResultBuilder Builder(TaxDb db) =>
            new(new ReportSetupRepository(db.Ctx()), new ReportMarksRepository(db.Ctx()), new ReportResultRepository(db.Ctx()), new AttendanceRepository(db.Ctx()), new StudentIdCardRepository(db.Ctx()),
                new ReportCardSettingsRepository(db.Ctx()), NullLogger<ReportResultBuilder>.Instance);

        private static string Student(TaxDb db, int n) =>
            db.Scalar<string>($"select student_id from tb_student_enrollments where school_id = {Q(A)} and academic_year_id = {TaxDb.YearA} and class_section_id = {SecA} and outcome is null order by student_id offset {n} limit 1");

        private static void Mark(TaxDb db, string student, long section, string date, string code) =>
            db.Exec($@"insert into tb_student_attendance (school_id, academic_year_id, section_id, class_id, student_id, attendance_date, attendance_status_id, marked_by, marked_at)
                       values ({Q(A)}, {TaxDb.YearA}, {section}, {Class5}, {Q(student)}, date '{date}', (select attendance_status_id from lut_attendance_status where status_code = '{code}'), {UserA}, now())");

        private static async Task<(int Working, int Present)?> Days(TaxDb db, long section, string student)
        {
            var set = await Builder(db).BuildAsync(A, Scheme, section, "YEAR", null, db.Scalar<int>($"select {ActiveSql}"), SchoolClock.Today(), default);
            Assert.NotNull(set);
            if (!set!.Models.TryGetValue(student, out var model)) return null;
            var t = model.TermSummaries.Single(x => x.TermId == Term1);
            return (t.WorkingDays ?? -1, t.PresentDays ?? -1);
        }

        private static void Prepare(TaxDb db, params string[] students)
        {
            db.Exec($"update tb_report_schemes set attendance_source = 'AUTO' where scheme_id = {Scheme}");
            foreach (var s in students) db.Exec($"update tb_student_enrollments set enrollment_date = date '2026-04-01' where school_id = {Q(A)} and student_id = {Q(s)} and academic_year_id = {TaxDb.YearA}");
        }

        [DbFact]
        public async Task A_student_who_moved_sections_mid_term_keeps_the_days_marked_before_the_move_and_nothing_is_counted_twice()
        {
            using var db = new TaxDb(); var s = Student(db, 0);
            Prepare(db, s);
            foreach (var d in new[] { "2026-08-03", "2026-08-04", "2026-08-05", "2026-08-06" }) Mark(db, s, SecA, d, "PRESENT");
            Mark(db, s, SecA, "2026-08-07", "ABSENT");
            Assert.Equal((5, 4), await Days(db, SecA, s));                                                                                     // before the move: 4 present of 5 marked days
            Assert.True((await Sections(db).ChangeAsync(A, UserA, new ChangeSectionRequest { StudentId = s, ToSectionId = SecB, Reason = "Timetable clash with tuition", EffectiveDate = new DateOnly(2026, 8, 10), Confirm = true }, default)).Status);
            foreach (var d in new[] { "2026-08-10", "2026-08-11", "2026-08-12" }) Mark(db, s, SecB, d, "PRESENT");
            Mark(db, s, SecB, "2026-08-13", "ABSENT");
            // a day wrongly marked in BOTH sections (if the database allows it) still counts once
            try { Mark(db, s, SecB, "2026-08-04", "PRESENT"); } catch (PostgresException) { /* one mark per student and day: even better */ }
            Assert.Equal((9, 7), await Days(db, SecB, s));                                                                                     // 5 + 4 marked days, 4 + 3 present: the earlier days are kept
            Assert.Null(await Days(db, SecA, s));                                                                                              // and he is not on the old section's cards any more
        }

        [DbFact]
        public async Task A_student_who_never_moved_gets_exactly_the_same_card_attendance_as_before()
        {
            using var db = new TaxDb(); var s = Student(db, 0); var other = Student(db, 1);
            Prepare(db, s, other);
            foreach (var d in new[] { "2026-08-03", "2026-08-04", "2026-08-05" }) { Mark(db, s, SecA, d, "PRESENT"); Mark(db, other, SecA, d, "PRESENT"); }
            Mark(db, other, SecA, "2026-08-06", "ABSENT");
            var before = await Days(db, SecA, other);
            var modelBefore = JsonSerializer.Serialize((await Builder(db).BuildAsync(A, Scheme, SecA, "YEAR", null, db.Scalar<int>($"select {ActiveSql}"), SchoolClock.Today(), default))!.Models[other].TermSummaries.Single(t => t.TermId == Term1));
            Assert.Equal((4, 3), before);
            Assert.True((await Sections(db).ChangeAsync(A, UserA, new ChangeSectionRequest { StudentId = s, ToSectionId = SecB, Reason = "Timetable clash with tuition", EffectiveDate = new DateOnly(2026, 8, 10), Confirm = true }, default)).Status);
            Assert.Equal(before, await Days(db, SecA, other));
            var modelAfter = JsonSerializer.Serialize((await Builder(db).BuildAsync(A, Scheme, SecA, "YEAR", null, db.Scalar<int>($"select {ActiveSql}"), SchoolClock.Today(), default))!.Models[other].TermSummaries.Single(t => t.TermId == Term1));
            // the rank is among the section's students (one fewer after the move), so it is masked; every other part of the term block, the attendance included, is identical
            string NoRank(string m) => System.Text.RegularExpressions.Regex.Replace(m, "\"Rank(edCount)?\":(null|[0-9]+)", "\"Rank$1\":0");
            Assert.Equal(NoRank(modelBefore), NoRank(modelAfter));
        }

        [DbFact]
        public async Task A_published_card_is_not_changed_and_a_student_with_one_cannot_be_moved()
        {
            using var db = new TaxDb(); var s = Student(db, 0); var moved = Student(db, 1);
            Prepare(db, s, moved);
            db.Exec($@"insert into tb_report_publications (school_id, scheme_id, section_id, class_id, scope, version, published_by, config_snapshot, student_count) values ({Q(A)}, {Scheme}, {SecA}, {Class5}, 'YEAR', 1, {UserA}, '{{}}'::jsonb, 1)");
            var pub = db.Scalar<long>("select max(publication_id) from tb_report_publications");
            db.Exec($@"insert into tb_report_publication_students (publication_id, school_id, student_id, student_name, admission_number, result_status, percentage, card_model) values ({pub}, {Q(A)}, {Q(s)}, 'N', '1', 'PASS', 71.5, '{{""attendance"":""as published""}}'::jsonb)");
            var published = db.Scalar<string>("select md5(string_agg(concat_ws('|', ps.*), ',' order by pub_student_id)) from tb_report_publication_students ps");
            Mark(db, moved, SecA, "2026-08-03", "PRESENT");
            Assert.True((await Sections(db).ChangeAsync(A, UserA, new ChangeSectionRequest { StudentId = moved, ToSectionId = SecB, Reason = "Timetable clash with tuition", EffectiveDate = new DateOnly(2026, 8, 10), Confirm = true }, default)).Status);
            await Days(db, SecA, s); await Days(db, SecB, moved);                                                                              // drafts are recomputed freely...
            Assert.Equal(published, db.Scalar<string>("select md5(string_agg(concat_ws('|', ps.*), ',' order by pub_student_id)) from tb_report_publication_students ps"));   // ...a published card is a frozen copy
            Assert.Equal(409, (await Sections(db).ChangeAsync(A, UserA, new ChangeSectionRequest { StudentId = s, ToSectionId = SecB, Reason = "Timetable clash with tuition", Confirm = true }, default)).StatusCode);
        }

        [DbFact]
        public async Task The_class_teachers_attendance_preview_agrees_with_the_card_for_a_moved_student()
        {
            using var db = new TaxDb(); var s = Student(db, 0);
            Prepare(db, s);
            foreach (var d in new[] { "2026-08-03", "2026-08-04" }) Mark(db, s, SecA, d, "PRESENT");
            Assert.True((await Sections(db).ChangeAsync(A, UserA, new ChangeSectionRequest { StudentId = s, ToSectionId = SecB, Reason = "Timetable clash with tuition", EffectiveDate = new DateOnly(2026, 8, 10), Confirm = true }, default)).Status);
            Mark(db, s, SecB, "2026-08-10", "ABSENT");
            // the shared rule used by both the card and the preview
            var attendance = new AttendanceRepository(db.Ctx());
            var roster = await attendance.GetReportRosterAsync(A, TaxDb.YearA, SecB, default);
            var here = await attendance.GetMonthMarksAsync(A, SecB, new DateOnly(2026, 4, 1), new DateOnly(2026, 9, 29), default);
            var elsewhere = await attendance.GetMarksInOtherSectionsAsync(A, TaxDb.YearA, SecB, roster.Select(r => r.StudentId).ToList(), new DateOnly(2026, 4, 1), new DateOnly(2026, 9, 29), default);
            var calc = TermAttendanceRules.Compute(new DateOnly(2026, 4, 1), new DateOnly(2026, 9, 29), SchoolClock.Today(), new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), new List<smss_api_db_layer.@interface.HolidayRow>(), roster, here, db.Scalar<int>($"select {ActiveSql}"), elsewhere);
            var row = calc.ByStudent[s];
            Assert.Equal(2, row.Present); Assert.Equal(1, row.Absent);
            Assert.DoesNotContain(elsewhere, m => m.Date == new DateOnly(2026, 8, 10));                                                          // only the OTHER section's marks come from elsewhere
        }
    }
}
