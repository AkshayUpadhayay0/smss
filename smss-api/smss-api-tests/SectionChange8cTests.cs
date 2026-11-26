using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using smss_api.Controllers;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.helper.reportcard;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Stage 8c: the mid-year SECTION change (same class, same year). [DbFact]: needs SMSS_TEST_PG and the template with the 8c migration (smss_tpl_8c). School A, year 41: Class 5 has sections A (60) and B (61).
    /// </summary>
    public class SectionChange8cTests
    {
        private const string A = TaxDb.SchoolA;
        private const string B = TaxDb.SchoolB;
        private const long UserA = TaxDb.UserA;
        private const long SecA = 60, SecB = 61, Scheme5 = 19;
        private static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        private const string ActiveSql = "(select sid from lut_status where sname = 'Active' and stype = 'general status')";

        private static SectionChangeService Svc(TaxDb db) => new(new SectionChangeRepository(db.Ctx()), NullLogger<SectionChangeService>.Instance);
        private static LeavingService Leaving(TaxDb db) => new(new LeavingRepository(db.Ctx()), NullLogger<LeavingService>.Instance);

        private static string InA(TaxDb db, int n = 0) =>
            db.Scalar<string>($"select student_id from tb_student_enrollments where school_id = {Q(A)} and academic_year_id = {TaxDb.YearA} and class_section_id = {SecA} and outcome is null order by student_id offset {n} limit 1");

        private static ChangeSectionRequest Req(string s, long to = SecB, string reason = "Timetable clash with tuition", string? roll = null, DateOnly? date = null, bool confirm = true) =>
            new() { StudentId = s, ToSectionId = to, Reason = reason, RollNumber = roll, EffectiveDate = date, Confirm = confirm };

        private static long EnrolmentOf(TaxDb db, string s) => db.Scalar<long>($"select enrollment_id from tb_student_enrollments where school_id = {Q(A)} and student_id = {Q(s)} and academic_year_id = {TaxDb.YearA}");
        private static long SectionOf(TaxDb db, string s) => db.Scalar<long>($"select class_section_id from tb_student_enrollments where enrollment_id = {EnrolmentOf(db, s)}");
        private static string? RollOf(TaxDb db, string s) => db.Scalar<string?>($"select roll_number from tb_student_enrollments where enrollment_id = {EnrolmentOf(db, s)}");

        /// <summary>A hash of everything about the student that a section change must NOT touch.</summary>
        private static string Untouched(TaxDb db, string s) => db.Scalar<string>($@"select md5(concat_ws('#',
            (select coalesce(string_agg(concat_ws('|', i.*), ',' order by i.student_fee_item_id), '') from tb_student_fee_items i where i.student_id = {Q(s)}),
            (select coalesce(string_agg(concat_ws('|', d.*), ',' order by d.fee_due_id), '') from tb_student_fee_dues d where d.student_id = {Q(s)}),
            (select coalesce(string_agg(concat_ws('|', a.*), ',' order by a.attendance_id), '') from tb_student_attendance a where a.student_id = {Q(s)}),
            (select coalesce(string_agg(concat_ws('|', m.*), ',' order by m.mark_id), '') from tb_report_marks m where m.student_id = {Q(s)}),
            (select coalesce(string_agg(concat_ws('|', t.*), ',' order by t.student_id), '') from tb_students t where t.student_id = {Q(s)}),
            (select coalesce(string_agg(concat_ws('|', e.enrollment_id, e.academic_year_id, e.enrollment_date, e.admission_type_id, e.status_id, e.outcome), ',' order by e.enrollment_id), '') from tb_student_enrollments e where e.student_id = {Q(s)})))");

        // ------------------------------------------------------------------------------------------------ the change itself
        [DbFact]
        public async Task A_change_updates_the_single_enrolment_in_place_writes_the_history_row_and_touches_nothing_else_of_the_student_or_anyone_else()
        {
            using var db = new TaxDb(); var s = InA(db);
            var oldRoll = RollOf(db, s); var enrol = EnrolmentOf(db, s); var before = Untouched(db, s);
            var enrolmentsBefore = db.Scalar<int>("select count(*) from tb_student_enrollments");
            var others = db.Scalar<string>($"select md5(string_agg(concat_ws('|', e.*), ',' order by enrollment_id)) from tb_student_enrollments e where student_id <> {Q(s)}");
            var r = await Svc(db).ChangeAsync(A, UserA, Req(s), default);
            Assert.True(r.Status, r.Message);
            Assert.Equal(SecB, SectionOf(db, s)); Assert.Equal(enrol, EnrolmentOf(db, s));                                                 // the SAME enrolment, now in B
            Assert.Equal(enrolmentsBefore, db.Scalar<int>("select count(*) from tb_student_enrollments"));                                 // no new enrolment
            var expectedRoll = (db.Scalar<int>($"select coalesce(max(roll_number::int), 0) from tb_student_enrollments where class_section_id = {SecB} and academic_year_id = {TaxDb.YearA} and student_id <> {Q(s)} and roll_number ~ '^[0-9]+$'") + 1).ToString();
            Assert.Equal(expectedRoll, RollOf(db, s)); Assert.Equal(expectedRoll, r.Data!.ToRoll); Assert.Equal(oldRoll, r.Data.FromRoll);
            Assert.Equal(before, Untouched(db, s));                                                                                         // fees, attendance, marks, the student record and the class are untouched
            Assert.Equal(others, db.Scalar<string>($"select md5(string_agg(concat_ws('|', e.*), ',' order by enrollment_id)) from tb_student_enrollments e where student_id <> {Q(s)}"));
            Assert.Equal(1, db.Scalar<int>($@"select count(*) from tb_student_section_changes where student_id = {Q(s)} and enrollment_id = {enrol} and academic_year_id = {TaxDb.YearA} and from_section_id = {SecA} and to_section_id = {SecB}
                                              and from_roll is not distinct from {(oldRoll == null ? "null" : Q(oldRoll))} and to_roll = {Q(expectedRoll)} and effective_date = current_date and reason = 'Timetable clash with tuition' and changed_by = {UserA}"));
        }

        [DbFact]
        public async Task The_reason_the_confirmation_the_date_and_the_target_are_checked_and_nothing_changes_when_a_check_fails()
        {
            using var db = new TaxDb(); var s = InA(db); var before = Untouched(db, s);
            Assert.Equal(400, (await Svc(db).ChangeAsync(A, UserA, Req(s, reason: "too short"), default)).StatusCode);
            Assert.Equal(400, (await Svc(db).ChangeAsync(A, UserA, Req(s, confirm: false), default)).StatusCode);
            Assert.Equal(409, (await Svc(db).ChangeAsync(A, UserA, Req(s, date: SchoolClock.Today().AddDays(1)), default)).StatusCode);                          // not in the future
            var enrolled = DateOnly.Parse(db.Scalar<string>($"select enrollment_date::text from tb_student_enrollments where enrollment_id = {EnrolmentOf(db, s)}"));
            Assert.Equal(409, (await Svc(db).ChangeAsync(A, UserA, Req(s, date: enrolled.AddDays(-1)), default)).StatusCode);                                     // not before the enrolment
            Assert.Equal(409, (await Svc(db).ChangeAsync(A, UserA, Req(s, to: SecA), default)).StatusCode);                                                       // already there
            Assert.Equal(409, (await Svc(db).ChangeAsync(A, UserA, Req(s, to: 62), default)).StatusCode);                                                         // a section of ANOTHER class (Class 6)
            Assert.Equal(409, (await Svc(db).ChangeAsync(A, UserA, Req(s, to: 999999), default)).StatusCode);                                                     // not a section at all
            db.Exec($"update tb_academic_year_sections set status_id = (select sid from lut_status where sname = 'Inactive' and stype = 'general status') where academic_year_id = {TaxDb.YearA} and section_id = {SecB}");   // the year does not offer B
            var notOffered = await Svc(db).ChangeAsync(A, UserA, Req(s), default);
            Assert.Equal(409, notOffered.StatusCode); Assert.Contains("not offered", notOffered.Message);
            Assert.Equal(SecA, SectionOf(db, s)); Assert.Equal(before, Untouched(db, s));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_student_section_changes"));
        }

        [DbFact]
        public async Task A_leaver_a_closed_year_and_a_student_with_a_published_report_card_are_refused_with_the_reason()
        {
            using var db = new TaxDb(); var left = InA(db); var closedS = InA(db, 1); var published = InA(db, 2);
            // left: not on the live roll
            db.Exec($"update tb_student_fee_dues set amount_paid = amount_due where student_id = {Q(left)}");
            Assert.True((await Leaving(db).RecordAsync(A, UserA, new RecordLeavingRequest { StudentId = left, Type = "LEFT", Reason = "Moved", Confirm = true }, default)).Status);
            var l = await Svc(db).ChangeAsync(A, UserA, Req(left), default);
            Assert.Equal(409, l.StatusCode); Assert.Contains("not on the live roll", l.Message); Assert.Equal(SecA, SectionOf(db, left));
            // a published card in this year: refused, and the card is exactly as it was
            db.Exec($@"insert into tb_report_publications (school_id, scheme_id, section_id, class_id, scope, version, published_by, config_snapshot, student_count)
                       values ({Q(A)}, {Scheme5}, {SecA}, 54, 'YEAR', 1, {UserA}, '{{}}'::jsonb, 1)");
            var pub = db.Scalar<long>("select max(publication_id) from tb_report_publications");
            db.Exec($@"insert into tb_report_publication_students (publication_id, school_id, student_id, student_name, admission_number, result_status, percentage, card_model) values ({pub}, {Q(A)}, {Q(published)}, 'N', '1', 'PASS', 71.5, '{{}}'::jsonb)");
            var cardsBefore = db.Scalar<string>($"select md5(string_agg(concat_ws('|', ps.*), ',')) from tb_report_publication_students ps where student_id = {Q(published)}");
            var p = await Svc(db).ChangeAsync(A, UserA, Req(published), default);
            Assert.Equal(409, p.StatusCode); Assert.Contains("published report card", p.Message); Assert.Equal(SecA, SectionOf(db, published));
            Assert.Equal(cardsBefore, db.Scalar<string>($"select md5(string_agg(concat_ws('|', ps.*), ',')) from tb_report_publication_students ps where student_id = {Q(published)}"));
            var cards = await new ReportResultService(Builder(db), new ReportResultRepository(db.Ctx()), new ReportMarksRepository(db.Ctx()), new StudentRepository(db.Ctx()), null!, new ReportCardSettingsRepository(db.Ctx()), null!, NullLogger<ReportResultService>.Instance)
                .CardsForStudentAsync(A, published, default);
            Assert.Equal(1, cards.Data!.Count); Assert.Equal(71.5m, cards.Data[0].Percent); Assert.Equal("A", cards.Data[0].SectionName);                                               // the published card still resolves
            // a closed year
            db.Exec($"update tb_academic_years set is_current = false where academic_year_id = {TaxDb.YearA}");
            db.Exec($"update tb_academic_years set closed_at = now(), closed_by = {UserA}, close_note = 'Closed for the section change test' where academic_year_id = {TaxDb.YearA}");
            var c = await Svc(db).ChangeAsync(A, UserA, Req(closedS), default);
            Assert.Equal(409, c.StatusCode); Assert.Contains("closed", c.Message); Assert.Equal(SecA, SectionOf(db, closedS));
        }

        // ------------------------------------------------------------------------------------------------ capacity and roll numbers
        [DbFact]
        public async Task The_capacity_of_the_target_section_is_checked_and_a_seat_frees_when_someone_leaves()
        {
            using var db = new TaxDb(); var s = InA(db); var s2 = InA(db, 1);
            var enrolledInB = db.Scalar<int>($"select count(*) from tb_student_enrollments where class_section_id = {SecB} and academic_year_id = {TaxDb.YearA}");
            db.Exec($"update tb_academic_year_sections set max_strength = {enrolledInB} where academic_year_id = {TaxDb.YearA} and section_id = {SecB}");
            var full = await Svc(db).ChangeAsync(A, UserA, Req(s), default);
            Assert.Equal(409, full.StatusCode); Assert.Contains("is full", full.Message); Assert.Equal(SecA, SectionOf(db, s));
            var check = (await Svc(db).CheckAsync(A, s, default)).Data!;
            Assert.True(check.Targets.Single(t => t.SectionId == SecB).IsFull);
            // one of B's students leaves: the seat is free again (the shared live-enrolment rule)
            var inB = db.Scalar<string>($"select student_id from tb_student_enrollments where class_section_id = {SecB} and academic_year_id = {TaxDb.YearA} order by student_id limit 1");
            db.Exec($"update tb_student_fee_dues set amount_paid = amount_due where student_id = {Q(inB)}");
            Assert.True((await Leaving(db).RecordAsync(A, UserA, new RecordLeavingRequest { StudentId = inB, Type = "LEFT", Reason = "Moved", Confirm = true }, default)).Status);
            Assert.True((await Svc(db).ChangeAsync(A, UserA, Req(s), default)).Status);
            Assert.Equal(409, (await Svc(db).ChangeAsync(A, UserA, Req(s2), default)).StatusCode);                                            // full again
            db.Exec($"update tb_academic_year_sections set max_strength = null where academic_year_id = {TaxDb.YearA} and section_id = {SecB}");   // no limit: always fine
            Assert.True((await Svc(db).ChangeAsync(A, UserA, Req(s2), default)).Status);
        }

        [DbFact]
        public async Task A_typed_roll_must_be_free_in_the_new_section_a_leavers_roll_is_still_taken_and_numeric_rolls_compare_without_leading_zeros()
        {
            using var db = new TaxDb(); var s = InA(db); var s2 = InA(db, 1); var s3 = InA(db, 2);
            var takenInB = db.Scalar<string>($"select roll_number from tb_student_enrollments where class_section_id = {SecB} and academic_year_id = {TaxDb.YearA} and roll_number ~ '^[0-9]+$' order by roll_number::int limit 1");
            var clash = await Svc(db).ChangeAsync(A, UserA, Req(s, roll: "0" + takenInB), default);
            Assert.Equal(409, clash.StatusCode); Assert.Contains("already used", clash.Message); Assert.Equal(SecA, SectionOf(db, s));
            // a leaver's roll is still taken
            var inB = db.Scalar<string>($"select student_id from tb_student_enrollments where class_section_id = {SecB} and academic_year_id = {TaxDb.YearA} and roll_number = {Q(takenInB)}");
            db.Exec($"update tb_student_fee_dues set amount_paid = amount_due where student_id = {Q(inB)}");
            Assert.True((await Leaving(db).RecordAsync(A, UserA, new RecordLeavingRequest { StudentId = inB, Type = "LEFT", Reason = "Moved", Confirm = true }, default)).Status);
            Assert.Equal(409, (await Svc(db).ChangeAsync(A, UserA, Req(s, roll: takenInB), default)).StatusCode);
            var ok = await Svc(db).ChangeAsync(A, UserA, Req(s, roll: "B-77"), default);
            Assert.True(ok.Status, ok.Message); Assert.Equal("B-77", RollOf(db, s));
            Assert.Equal(409, (await Svc(db).ChangeAsync(A, UserA, Req(s2, roll: "b-77"), default)).StatusCode);                              // case-insensitive, like the unique index
            // blank = the next free number, above every numeric roll including the leaver's
            var max = db.Scalar<int>($"select max(roll_number::int) from tb_student_enrollments where class_section_id = {SecB} and academic_year_id = {TaxDb.YearA} and roll_number ~ '^[0-9]+$'");
            Assert.True((await Svc(db).ChangeAsync(A, UserA, Req(s3), default)).Status);
            Assert.Equal((max + 1).ToString(), RollOf(db, s3));
            Assert.Equal(0, db.Scalar<int>($@"select count(*) from (select 1 from tb_student_enrollments where class_section_id = {SecB} and academic_year_id = {TaxDb.YearA} and roll_number is not null
                                              group by public.fn_roll_key(roll_number) having count(*) > 1) x"));                                                // roll numbers are unique per year and section
        }

        [DbFact]
        public async Task Changing_back_is_just_another_change_and_the_history_lists_every_one_newest_first()
        {
            using var db = new TaxDb(); var s = InA(db); var oldRoll = RollOf(db, s);
            Assert.True((await Svc(db).ChangeAsync(A, UserA, Req(s, SecB, "Moved to B for a subject choice"), default)).Status);
            Assert.True((await Svc(db).ChangeAsync(A, UserA, Req(s, SecA, "Moved back to A, the choice changed", oldRoll), default)).Status);   // its old roll was free again
            Assert.Equal(SecA, SectionOf(db, s)); Assert.Equal(oldRoll, RollOf(db, s));
            var h = (await Svc(db).HistoryAsync(A, s, default)).Data!;
            Assert.Equal(new[] { "B>A", "A>B" }, h.Select(x => x.FromSection + ">" + x.ToSection));
            Assert.Equal(new[] { "Moved back to A, the choice changed", "Moved to B for a subject choice" }, h.Select(x => x.Reason));
            Assert.All(h, x => Assert.Equal("2026-27", x.YearName));
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_student_section_changes where student_id = {Q(s)}"));                      // nothing is ever deleted
        }

        // ------------------------------------------------------------------------------------------------ who may, and whose student
        [DbFact]
        public async Task Only_a_School_Admin_or_a_Principal_may_check_change_or_read_the_history_and_another_schools_student_is_404()
        {
            using var db = new TaxDb(); var s = InA(db);
            int Code(IActionResult r) => Fake.Code(r);
            SectionChangesController C(string school, params string[] roles) => Fake.WithUser(new SectionChangesController(Svc(db)), Fake.User(school, school == A ? UserA : TaxDb.UserB, roles));
            foreach (var role in new[] { "Teacher", "Accountant", "Student", "Guardian", "Admission Staff", "Store Keeper" })
            {
                Assert.Equal(403, Code(await C(A, role).Check(s, default)));
                Assert.Equal(403, Code(await C(A, role).Change(Req(s), default)));
                Assert.Equal(403, Code(await C(A, role).History(s, default)));
            }
            Assert.Equal(SecA, SectionOf(db, s));
            Assert.Equal(404, Code(await C(B, "School Admin").Check(s, default)));
            Assert.Equal(404, Code(await C(B, "Principal").Change(Req(s), default)));
            Assert.Equal(404, Code(await C(B, "School Admin").History(s, default)));
            Assert.Equal(SecA, SectionOf(db, s));
            Assert.Equal(200, Code(await C(A, "Principal").Check(s, default)));
            Assert.Equal(200, Code(await C(A, "School Admin").Change(Req(s), default)));
            Assert.Equal(200, Code(await C(A, "Principal").History(s, default)));
        }

        // ------------------------------------------------------------------------------------------------ two at once
        [DbFact]
        public async Task Two_changes_into_a_section_with_one_seat_left_at_the_same_moment_let_exactly_one_through_and_roll_numbers_never_clash()
        {
            using var db = new TaxDb(); var s = InA(db); var s2 = InA(db, 1);
            var n = db.Scalar<int>($"select count(*) from tb_student_enrollments where class_section_id = {SecB} and academic_year_id = {TaxDb.YearA}");
            db.Exec($"update tb_academic_year_sections set max_strength = {n + 1} where academic_year_id = {TaxDb.YearA} and section_id = {SecB}");
            var results = await Task.WhenAll(new[] { s, s2 }.Select(x => Task.Run(() => Svc(db).ChangeAsync(A, UserA, Req(x), default))));
            Assert.Equal(new[] { 200, 409 }, results.Select(r => r.StatusCode).OrderBy(c => c));
            Assert.Equal(n + 1, db.Scalar<int>($"select count(*) from tb_student_enrollments where class_section_id = {SecB} and academic_year_id = {TaxDb.YearA}"));
            // and with room for both, two at once get two different roll numbers
            db.Exec($"update tb_academic_year_sections set max_strength = null where academic_year_id = {TaxDb.YearA} and section_id = {SecB}");
            var s3 = InA(db, 0); var s4 = InA(db, 1);
            var both = await Task.WhenAll(new[] { s3, s4 }.Select(x => Task.Run(() => Svc(db).ChangeAsync(A, UserA, Req(x), default))));
            Assert.All(both, r => Assert.True(r.Status, r.Message));
            Assert.NotEqual(RollOf(db, s3), RollOf(db, s4));
        }

        [DbFact]
        public async Task The_same_student_changed_twice_at_the_same_moment_is_moved_once()
        {
            using var db = new TaxDb(); var s = InA(db);
            var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => Svc(db).ChangeAsync(A, UserA, Req(s), default))));
            Assert.Equal(new[] { 200, 409 }, results.Select(r => r.StatusCode).OrderBy(c => c));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_student_section_changes where student_id = {Q(s)}"));
        }

        // ------------------------------------------------------------------------------------------------ nothing breaks for the student afterwards
        private static ReportResultBuilder Builder(TaxDb db)
        {
            var c = db.Ctx();
            return new ReportResultBuilder(new ReportSetupRepository(c), new ReportMarksRepository(db.Ctx()), new ReportResultRepository(db.Ctx()), new AttendanceRepository(db.Ctx()), new StudentIdCardRepository(db.Ctx()),
                new ReportCardSettingsRepository(db.Ctx()), NullLogger<ReportResultBuilder>.Instance);
        }

        [DbFact]
        public async Task Marks_entered_in_A_still_make_the_same_report_result_in_B_with_the_activity_grades_term_and_year_inputs_that_were_entered_under_A()
        {
            using var db = new TaxDb(); var s = InA(db);
            var active = db.Scalar<int>($"select {ActiveSql}");
            var before = (await Builder(db).BuildAsync(A, Scheme5, SecA, "YEAR", null, active, SchoolClock.Today(), default))!;
            var model = JsonSerializer.Serialize(before.Models[s]); var rollBefore = RollOf(db, s);
            var year = before.Results[s].Year;
            Assert.True(year.Percent > 0);
            Assert.True(db.Scalar<int>($"select count(*) from tb_report_student_activity_grades where scheme_id = {Scheme5} and student_id = {Q(s)} and section_id = {SecA}") > 0);   // the inputs exist, under A
            Assert.True((await Svc(db).ChangeAsync(A, UserA, Req(s), default)).Status);
            var inB = (await Builder(db).BuildAsync(A, Scheme5, SecB, "YEAR", null, active, SchoolClock.Today(), default))!;
            var inA = (await Builder(db).BuildAsync(A, Scheme5, SecA, "YEAR", null, active, SchoolClock.Today(), default))!;
            Assert.Contains(s, inB.Results.Keys); Assert.DoesNotContain(s, inA.Results.Keys);                                                  // he is on B's roll now, not A's
            var after = inB.Results[s].Year;
            Assert.Equal(year.Percent, after.Percent); Assert.Equal(year.Obtained, after.Obtained); Assert.Equal(year.Grade, after.Grade); Assert.Equal(year.Status, after.Status);   // the same result from the same marks
            var modelAfter = JsonSerializer.Serialize(inB.Models[s]);
            // the card model differs only in the section name and the roll number: the activity grades, term inputs and year inputs came with him
            // the rank is among the section's students and the class teacher is the section's: both are rightly the new section's, so they are masked
            string Norm(string m, string? roll) => System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(m, "\"ClassTeacher\":\"[^\"]*\"", "\"ClassTeacher\":\"T\""), "\"Rank(edCount)?\":(null|[0-9]+)", "\"Rank$1\":0").Replace("\"A\"", "\"S\"").Replace("\"B\"", "\"S\"").Replace(roll == null ? "" : $"\"{roll}\"", "\"R\"");
            Assert.Equal(Norm(model, rollBefore), Norm(modelAfter, RollOf(db, s)));
        }

        [DbFact]
        public async Task The_entry_screens_and_the_student_views_follow_the_new_section_and_the_old_results_stay_visible()
        {
            using var db = new TaxDb(); var s = InA(db);
            var active = db.Scalar<int>($"select {ActiveSql}");
            var term = db.Scalar<long>($"select term_id from tb_report_term_student where scheme_id = {Scheme5} and student_id = {Q(s)} limit 1");
            Assert.True((await Svc(db).ChangeAsync(A, UserA, Req(s), default)).Status);
            // the report entry screens read the rows entered under A for a student now in B (so a re-save updates them instead of colliding on the key)
            var marksRepo = new ReportMarksRepository(db.Ctx());
            Assert.Contains(await marksRepo.GetActivityGradesAsync(term, SecB, false, default), g => g.StudentId == s);
            Assert.Contains(await marksRepo.GetTermStudentsAsync(term, SecB, false, default), g => g.StudentId == s);
            Assert.Contains(await marksRepo.GetYearStudentsAsync(Scheme5, SecB, false, default), g => g.StudentId == s);
            // marks are keyed by student and exam subject: he is on B's roster with his marks
            var roster = (await marksRepo.GetRosterAsync(A, TaxDb.YearA, SecB, active, default)).Select(r => r.StudentId).ToList();
            Assert.Contains(s, roster);
            var examSubject = db.Scalar<long>($"select es.exam_subject_id from tb_report_marks m join tb_report_exam_subjects es on es.exam_subject_id = m.exam_subject_id where m.student_id = {Q(s)} limit 1");
            Assert.Contains(await marksRepo.GetMarksAsync(A, examSubject, SecB, roster, false, default), m => m.StudentId == s);
            // class-teacher access moves with the section; homework, timetable and the ID card follow the new section
            var teacherA = db.Scalar<string>($"select employee_id from tb_class_teacher_assignments where school_id = {Q(A)} and academic_year_id = {TaxDb.YearA} and section_id = {SecA} and status_id = {ActiveSql} limit 1");
            var teacherB = db.Scalar<string>($"select employee_id from tb_class_teacher_assignments where school_id = {Q(A)} and academic_year_id = {TaxDb.YearA} and section_id = {SecB} and status_id = {ActiveSql} limit 1");
            var docs = new StudentDocumentRepository(db.Ctx());
            Assert.True(!string.IsNullOrEmpty(teacherA) && !string.IsNullOrEmpty(teacherB) && teacherA != teacherB);
            Assert.False(await docs.IsClassTeacherOfStudentAsync(A, teacherA, s, active, default)); Assert.True(await docs.IsClassTeacherOfStudentAsync(A, teacherB, s, active, default));
            Assert.Equal(SecB, await new HomeworkRepository(db.Ctx()).GetStudentSectionAsync(A, s, TaxDb.YearA, active, default));
            Assert.Equal(SecB, await new AssessmentRepository(db.Ctx()).GetStudentSectionAsync(A, s, TaxDb.YearA, active, default));
            var card = await new StudentIdCardRepository(db.Ctx()).GetStudentAsync(A, TaxDb.YearA, s, default);
            Assert.Equal("B", card!.SectionName);
            var grid = await new TimetableRepository(db.Ctx()).GetStudentSectionAsync(A, s, TaxDb.YearA, active, default);
            Assert.Equal(SecB, grid);
        }

        [DbFact]
        public async Task A_class_test_taken_in_A_stays_in_the_students_own_results_after_the_move()
        {
            using var db = new TaxDb(); var s = InA(db);
            var active = db.Scalar<int>($"select {ActiveSql}");
            // a class test given to section A (Class 5), with this student's mark in it
            db.Exec($@"insert into tb_class_assessments (school_id, academic_year_id, class_id, section_id, subject_id, assessment_type_id, employee_id, title, test_date, max_marks, status_id)
                       select school_id, academic_year_id, 54, {SecA}, subject_id, assessment_type_id, employee_id, 'Unit test before the move', test_date, max_marks, status_id from tb_class_assessments where assessment_id = 11");
            var test = db.Scalar<long>("select max(assessment_id) from tb_class_assessments");
            db.Exec($"insert into tb_student_assessment_marks (assessment_id, student_id, obtained_marks, is_absent) values ({test}, {Q(s)}, 7, false)");
            var before = await new AssessmentRepository(db.Ctx()).GetStudentResultsAsync(A, s, TaxDb.YearA, SecA, active, null, null, default);
            Assert.Contains(before, r => r.AssessmentId == test);
            Assert.True((await Svc(db).ChangeAsync(A, UserA, Req(s), default)).Status);
            var after = await new AssessmentRepository(db.Ctx()).GetStudentResultsAsync(A, s, TaxDb.YearA, SecB, active, null, null, default);
            Assert.Contains(after, r => r.AssessmentId == test);                                                                              // still in the student's own results
        }
    }
}
