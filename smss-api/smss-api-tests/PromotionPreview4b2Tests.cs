using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using smss_api.Controllers;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Stage 4b-2: the promotion PREVIEW (read-only). Decisions prefilled from the class teacher, next class / same-named section / roll numbers, the dues block (due on or before the promotion date, all four
    /// decisions), capacity, already-enrolled skips, the no-fee-structure warning, "no decision yet", the 500 limit, closed / non-later target, cross-school 404 and the role matrix. Dues are throwaway rows
    /// made on the scratch database only. [DbFact]: needs SMSS_TEST_PG and a template that has Docs/smss-st-4b1-RUN-ME.sql applied (SMSS_TEST_TEMPLATE).
    /// </summary>
    public class PromotionPreview4b2Tests
    {
        private const string Active = "(select sid from lut_status where sname='Active' and stype='general status')";
        private const string Inactive = "(select sid from lut_status where sname='Inactive' and stype='general status')";
        private const string NewAdm = "(select admission_type_id from lut_admission_type where type_code='NEW_ADMISSION')";

        private static PromotionService Svc(TaxDb db) => new(new PromotionRepository(db.Ctx()), NullLogger<PromotionService>.Instance);

        private sealed class W
        {
            public string School = "";
            public long S, T, C1, C2, C3, C4, C1A, C1B, C2A, C2B, C3A, C4A;
        }

        private static void Student(TaxDb db, string school, string id, bool active = true) =>
            db.Exec($@"insert into tb_students (student_id, school_id, admission_number, first_name, last_name, date_of_birth, admission_type_id, admission_date, status_id)
                       values ('{id}', '{school}', '{id}-N', 'Stu', '{id}', '2015-01-01', {NewAdm}, '2026-04-01', {(active ? Active : Inactive)})");

        private static void Enrol(TaxDb db, string school, string id, long year, long section, string? roll, string? outcome = null) =>
            db.Exec($@"insert into tb_student_enrollments (school_id, student_id, academic_year_id, class_section_id, roll_number, enrollment_date, admission_type_id, status_id, outcome, outcome_at)
                       values ('{school}', '{id}', {year}, {section}, {(roll == null ? "null" : $"'{roll}'")}, '2026-04-01', {NewAdm}, {Active}, {(outcome == null ? "null" : $"'{outcome}'")}, {(outcome == null ? "null" : "now()")})");

        private static void Teacher(TaxDb db, W w, string student, long section, string decision, string? promotedTo = null) =>
            db.Exec($@"insert into tb_report_year_student (scheme_id, student_id, section_id, school_id, promotion_decision, promoted_to)
                       select scheme_id, '{student}', {section}, '{w.School}', '{decision}', {(promotedTo == null ? "null" : $"'{promotedTo}'")} from tb_report_schemes where school_id='{w.School}' and academic_year_id={w.S}");

        private static void Due(TaxDb db, W w, string student, string date, decimal due, decimal paid)
        {
            db.Exec($@"insert into tb_student_fee_items (student_id, school_id, academic_year_id, fee_type_id, frequency_id, base_amount, net_amount)
                       select '{student}', '{w.School}', {w.S}, (select fee_type_id from tb_fee_types where school_id='{w.School}' and fee_type_code='TUI'), (select min(frequency_id) from lut_fee_frequency), 1000, 1000
                       where not exists (select 1 from tb_student_fee_items where student_id='{student}' and academic_year_id={w.S})");
            db.Exec($@"insert into tb_student_fee_dues (student_fee_item_id, student_id, school_id, due_date, amount_due, amount_paid)
                       select student_fee_item_id, '{student}', '{w.School}', '{date}', {due}, {paid} from tb_student_fee_items where student_id='{student}' and academic_year_id={w.S}");
        }

        /// <summary>
        /// 2026-27 (source, current) and 2027-28 (target). Class 1 -> Class 2 -> Class 3 (next_class_id); Class 4 is offered but is nobody's next. The target offers Class 1 (A, B), Class 2 (A with 2 places, B),
        /// Class 4 (A) and NOT Class 3. Class 1 and Class 2 have a fee structure in the target; Class 4 has none.
        /// </summary>
        private static W Build(TaxDb db, string id)
        {
            var w = new W { School = id };
            db.Exec($"insert into tb_schools (school_id, school_code, school_name, school_status_id) values ('{id}', '{id}-C', 'Promotion test {id}', {Active})");
            db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('{id}', '2026-27', '2026-04-01', '2027-03-31', true, {Active}), ('{id}', '2027-28', '2027-04-01', '2028-03-31', false, {Active})");
            w.S = db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id='{id}' and year_name='2026-27'");
            w.T = db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id='{id}' and year_name='2027-28'");
            for (var i = 1; i <= 4; i++) db.Exec($"insert into tb_classes (school_id, class_name, sequence_order, status_id) values ('{id}', 'Class {i}', {i}, {Active})");
            long Cl(int i) => db.Scalar<long>($"select class_id from tb_classes where school_id='{id}' and class_name='Class {i}'");
            (w.C1, w.C2, w.C3, w.C4) = (Cl(1), Cl(2), Cl(3), Cl(4));
            db.Exec($"update tb_classes set next_class_id={w.C2} where class_id={w.C1}");
            db.Exec($"update tb_classes set next_class_id={w.C3} where class_id={w.C2}");
            foreach (var c in new[] { w.C1, w.C2, w.C3, w.C4 }) foreach (var s in new[] { "A", "B" }) db.Exec($"insert into tb_sections (school_id, class_id, section_name, status_id) values ('{id}', {c}, '{s}', {Active})");
            long Sec(long c, string n) => db.Scalar<long>($"select section_id from tb_sections where class_id={c} and section_name='{n}'");
            (w.C1A, w.C1B, w.C2A, w.C2B, w.C3A, w.C4A) = (Sec(w.C1, "A"), Sec(w.C1, "B"), Sec(w.C2, "A"), Sec(w.C2, "B"), Sec(w.C3, "A"), Sec(w.C4, "A"));
            // the source year offers everything; the target offers Class 1 (A, B), Class 2 (A max 2, B), Class 4 (A)
            db.Exec($"insert into tb_academic_year_classes (school_id, academic_year_id, class_id, status_id) select '{id}', {w.S}, class_id, {Active} from tb_classes where school_id='{id}'");
            db.Exec($"insert into tb_academic_year_sections (school_id, academic_year_id, class_id, section_id, status_id) select '{id}', {w.S}, class_id, section_id, {Active} from tb_sections where school_id='{id}'");
            foreach (var c in new[] { w.C1, w.C2, w.C4 }) db.Exec($"insert into tb_academic_year_classes (school_id, academic_year_id, class_id, status_id) values ('{id}', {w.T}, {c}, {Active})");
            foreach (var (c, s, max) in new[] { (w.C1, w.C1A, "null"), (w.C1, w.C1B, "null"), (w.C2, w.C2A, "2"), (w.C2, w.C2B, "null"), (w.C4, w.C4A, "null") })
                db.Exec($"insert into tb_academic_year_sections (school_id, academic_year_id, class_id, section_id, max_strength, status_id) values ('{id}', {w.T}, {c}, {s}, {max}, {Active})");
            db.Exec($"insert into tb_fee_types (school_id, fee_type_name, fee_type_code, isactive) values ('{id}', 'Tuition', 'TUI', true)");
            foreach (var c in new[] { w.C1, w.C2 }) db.Exec($"insert into tb_fee_structures (school_id, academic_year_id, class_id, structure_name, isactive) values ('{id}', {w.T}, {c}, 'Fees', true)");
            db.Exec($"insert into tb_report_schemes (school_id, academic_year_id, scheme_name, evaluation_mode, template_key, status_id) values ('{id}', {w.S}, 'Year', 'MARKS', 'classic-card', {Active})");
            return w;
        }

        /// <summary>Class 1 students (see each comment) and a few in Class 2.</summary>
        private static W Populate(TaxDb db, string id)
        {
            var w = Build(db, id);
            foreach (var s in new[] { "P1", "P2", "P3", "P4", "P5", "P6", "P7", "P8", "P9", "P10", "Q1", "Q2", "Q3", "X1" }) Student(db, id, id + s, active: s != "P9");
            string S(string s) => id + s;
            Enrol(db, id, S("P1"), w.S, w.C1A, "1"); Teacher(db, w, S("P1"), w.C1A, "PROMOTED");                         // promote to Class 2 A
            Enrol(db, id, S("P2"), w.S, w.C1A, "2"); Teacher(db, w, S("P2"), w.C1A, "DETAINED");                         // repeat Class 1 A
            Enrol(db, id, S("P3"), w.S, w.C1A, "3");                                                                      // no decision
            Enrol(db, id, S("P4"), w.S, w.C1A, "4"); Teacher(db, w, S("P4"), w.C1A, "PROMOTED");                         // dues
            Due(db, w, S("P4"), "2026-10-01", 500, 0); Due(db, w, S("P4"), "2027-02-01", 300, 0);
            Enrol(db, id, S("P5"), w.S, w.C1B, "1"); Teacher(db, w, S("P5"), w.C1B, "PROMOTED", "Class 2");              // the teacher agrees with next
            Enrol(db, id, S("P6"), w.S, w.C1B, "2"); Teacher(db, w, S("P6"), w.C1B, "PROMOTED", "Class 3");              // not offered in the target
            Enrol(db, id, S("P7"), w.S, w.C1B, "3"); Teacher(db, w, S("P7"), w.C1B, "PROMOTED");                         // already enrolled in the target
            Enrol(db, id, S("P7"), w.T, w.C2B, "9");
            Enrol(db, id, S("P8"), w.S, w.C1B, "4", outcome: "PASSED_OUT");                                               // outcome already recorded
            Enrol(db, id, S("P9"), w.S, w.C1B, "5");                                                                      // inactive student: not a candidate
            Enrol(db, id, S("P10"), w.S, w.C1A, "5"); Teacher(db, w, S("P10"), w.C1A, "PROMOTED", "Class 4");            // the teacher names another offered class
            Enrol(db, id, S("Q1"), w.S, w.C2A, "1"); Teacher(db, w, S("Q1"), w.C2A, "PROMOTED");                         // Class 2 -> Class 3 is not offered
            Enrol(db, id, S("Q2"), w.S, w.C2A, "2");
            Enrol(db, id, S("Q3"), w.S, w.C2B, "1");
            Enrol(db, id, S("X1"), w.T, w.C2B, "5");                                                                      // already holds roll 5 in Class 2 B of the target year
            return w;
        }

        private static PromotionPreviewRequest Req(W w, long? cls = null, long? section = null, DateOnly? date = null, PromotionDecisionInput[]? edits = null) =>
            new() { SourceYearId = w.S, TargetYearId = w.T, ClassId = cls ?? w.C1, SectionId = section, PromotionDate = date ?? new DateOnly(2027, 3, 1), Decisions = (edits ?? Array.Empty<PromotionDecisionInput>()).ToList() };

        private static PromotionRowDto Row(PromotionPreviewDto d, W w, string s) => d.Rows.Single(r => r.StudentId == w.School + s);
        private static async Task<PromotionPreviewDto> Run(TaxDb db, PromotionPreviewRequest r, string school)
        {
            var x = await Svc(db).PreviewAsync(school, r, default);
            Assert.True(x.Status, x.Message);
            return x.Data!;
        }
        private static string Fp(TaxDb db, string school) => db.Scalar<string>($@"select md5(coalesce(string_agg(x, '|' order by x), '')) from (
            select e::text x from tb_student_enrollments e where school_id='{school}' union all select d::text from tb_student_fee_dues d where school_id='{school}'
            union all select b::text from tb_promotion_batches b where school_id='{school}' union all select i::text from tb_promotion_items i where school_id='{school}'
            union all select f::text from tb_student_fee_structure f where school_id='{school}') q");

        // ------------------------------------------------------------------------------------------------ the rows
        [DbFact]
        public async Task The_teachers_decisions_prefill_and_no_decision_means_left_out()
        {
            using var db = new TaxDb();
            var w = Populate(db, "ZPA");
            var d = await Run(db, Req(w), w.School);
            Assert.Equal(("CLASS", "Class 1", "Class 2", false), (d.Scope, d.ClassName, d.NextClassName, d.IsLastClass));
            Assert.Equal(("PROMOTE", "TEACHER"), (Row(d, w, "P1").Decision, Row(d, w, "P1").DecisionSource));
            Assert.Equal("PROMOTED", Row(d, w, "P1").TeacherDecision);
            Assert.Equal(("REPEAT", "TEACHER"), (Row(d, w, "P2").Decision, Row(d, w, "P2").DecisionSource));
            var p3 = Row(d, w, "P3");
            Assert.Null(p3.Decision); Assert.Equal(PromotionRowStatus.NoDecision, p3.Status); Assert.Equal("NONE", p3.DecisionSource);
            Assert.Contains(p3.Notes, n => n.StartsWith("No decision yet"));
            Assert.Null(p3.Target);
            Assert.DoesNotContain(d.Rows, r => r.StudentId == w.School + "P9");      // an inactive student is not in the run
            Assert.Equal(1, d.Counts.NoDecision);
        }

        [DbFact]
        public async Task The_next_class_and_the_same_named_section_are_the_defaults_and_a_repeat_stays_in_the_class()
        {
            using var db = new TaxDb();
            var w = Populate(db, "ZPB");
            var d = await Run(db, Req(w), w.School);
            var p1 = Row(d, w, "P1");
            Assert.Equal((w.C2, w.C2A), (p1.Target!.ClassId, p1.Target.SectionId));
            Assert.Equal((w.C2, w.C2B), (Row(d, w, "P5").Target!.ClassId, Row(d, w, "P5").Target!.SectionId));   // B stays B
            var p2 = Row(d, w, "P2");
            Assert.Equal((w.C1, w.C1A), (p2.Target!.ClassId, p2.Target.SectionId));
            Assert.Equal(w.C2, Row(d, w, "P3").PromoteDefault!.ClassId);                                          // the screen can switch to Promote at once
            Assert.Equal(w.C1, Row(d, w, "P3").RepeatDefault!.ClassId);
            Assert.Equal(PromotionRowStatus.Ready, p1.Status);
        }

        [DbFact]
        public async Task The_teachers_class_is_used_only_on_an_exact_offered_match_and_a_conflict_with_the_next_class_is_noted()
        {
            using var db = new TaxDb();
            var w = Populate(db, "ZPC");
            var d = await Run(db, Req(w), w.School);
            var agrees = Row(d, w, "P5");                       // wrote "Class 2" = the next class: no note
            Assert.Equal(w.C2, agrees.Target!.ClassId); Assert.Empty(agrees.Notes);
            var other = Row(d, w, "P10");                       // wrote "Class 4", offered, not the next class: used, with a note
            Assert.Equal(w.C4, other.Target!.ClassId);
            Assert.Contains(other.Notes, n => n.Contains("Class 4") && n.Contains("next class is Class 2"));
            var unknown = Row(d, w, "P6");                      // wrote "Class 3", not offered in the target year: the next class is used, with a note
            Assert.Equal(w.C2, unknown.Target!.ClassId);
            Assert.Contains(unknown.Notes, n => n.Contains("Class 3") && n.Contains("not a class"));
        }

        [DbFact]
        public async Task Roll_numbers_are_the_next_free_in_the_new_section_and_are_never_copied_from_the_old_one()
        {
            using var db = new TaxDb();
            var w = Populate(db, "ZPD");
            var d = await Run(db, Req(w), w.School);
            Assert.Equal("1", Row(d, w, "P1").RollNumber);      // Class 2 A is empty in the target year
            Assert.Equal("10", Row(d, w, "P5").RollNumber);     // Class 2 B holds 5 and 9: the next after the highest
            Assert.Equal("11", Row(d, w, "P6").RollNumber);
            Assert.Null(Row(d, w, "P3").RollNumber);            // no decision, no roll
        }

        [DbFact]
        public async Task A_roll_number_continues_after_the_highest_in_the_section_and_a_typed_clash_needs_attention()
        {
            using var db = new TaxDb();
            var w = Populate(db, "ZPE");
            var d = await Run(db, Req(w, edits: new[] { new PromotionDecisionInput { StudentId = w.School + "P6", RollNumber = "05" } }), w.School);
            Assert.Equal("10", Row(d, w, "P5").RollNumber);     // Class 2 B holds 5 (X1) and 9 (P7): the next is after the highest
            var p6 = Row(d, w, "P6");                           // "05" is the same roll as 5
            Assert.Equal(PromotionRowStatus.NeedsAttention, p6.Status);
            Assert.Contains(p6.Issues, i => i.Contains("already taken"));
            var ok = await Run(db, Req(w, edits: new[] { new PromotionDecisionInput { StudentId = w.School + "P6", RollNumber = "20" } }), w.School);
            Assert.Equal("20", Row(ok, w, "P6").RollNumber);
            Assert.Equal("10", Row(ok, w, "P5").RollNumber);
            Assert.Equal(PromotionRowStatus.Ready, Row(ok, w, "P6").Status);
        }

        // ------------------------------------------------------------------------------------------------ dues
        [DbFact]
        public async Task Unpaid_dues_up_to_the_promotion_date_block_with_the_per_due_list_and_later_dues_are_only_information()
        {
            using var db = new TaxDb();
            var w = Populate(db, "ZPF");
            var d = await Run(db, Req(w, date: new DateOnly(2026, 11, 25)), w.School);
            var p4 = Row(d, w, "P4");
            Assert.Equal(PromotionRowStatus.Blocked, p4.Status);
            Assert.True(p4.BlockedByDues);
            Assert.Equal(500m, p4.Outstanding); Assert.Equal(300m, p4.NotYetDue);
            var due = Assert.Single(p4.Dues);
            Assert.Equal(("Tuition", new DateOnly(2026, 10, 1), 500m), (due.FeeType, due.DueDate, due.Outstanding));
            var later = await Run(db, Req(w, date: new DateOnly(2027, 3, 1)), w.School);          // the second due is now on or before the date too
            Assert.Equal(800m, Row(later, w, "P4").Outstanding); Assert.Equal(2, Row(later, w, "P4").Dues.Count); Assert.Equal(0m, Row(later, w, "P4").NotYetDue);
            var early = await Run(db, Req(w, date: new DateOnly(2026, 9, 1)), w.School);           // before every due: nothing blocks
            Assert.Equal(PromotionRowStatus.Ready, Row(early, w, "P4").Status);
            Assert.Equal(800m, Row(early, w, "P4").NotYetDue);
        }

        [DbFact]
        public async Task A_partly_paid_due_counts_only_what_is_left_and_the_block_applies_to_every_decision()
        {
            using var db = new TaxDb();
            var w = Populate(db, "ZPG");
            Due(db, w, w.School + "P2", "2026-08-01", 400, 150);       // P2 repeats: 250 left
            Due(db, w, w.School + "P3", "2026-08-01", 100, 100);       // fully paid: no block
            var edits = new[]
            {
                new PromotionDecisionInput { StudentId = w.School + "P4", Decision = "PASS_OUT" },
                new PromotionDecisionInput { StudentId = w.School + "P3", Decision = "LEAVE" },
            };
            var d = await Run(db, Req(w, edits: edits), w.School);
            Assert.Equal((PromotionRowStatus.Blocked, 250m), (Row(d, w, "P2").Status, Row(d, w, "P2").Outstanding));   // REPEAT
            Assert.Equal(PromotionRowStatus.Blocked, Row(d, w, "P4").Status);                                           // PASS_OUT
            Assert.Equal(PromotionRowStatus.Ready, Row(d, w, "P3").Status);                                             // LEAVE, nothing owed
            Assert.Equal("LEAVE", Row(d, w, "P3").Decision);
            Assert.Equal("ADMIN", Row(d, w, "P3").DecisionSource);
            Assert.Null(Row(d, w, "P3").Target);
        }

        // ------------------------------------------------------------------------------------------------ capacity, offering, last class, fees
        [DbFact]
        public async Task A_section_with_a_limit_is_full_for_the_rows_beyond_it_and_a_section_without_a_limit_never_is()
        {
            using var db = new TaxDb();
            var w = Populate(db, "ZPH");
            // Class 2 A has 2 places: P1 and P4 (blocked, but it keeps its place) take them; P5 asked for A too
            var d = await Run(db, Req(w, edits: new[] { new PromotionDecisionInput { StudentId = w.School + "P5", ToClassId = w.C2, ToSectionId = w.C2A } }), w.School);
            Assert.Equal(PromotionRowStatus.NeedsAttention, Row(d, w, "P5").Status);
            Assert.Contains(Row(d, w, "P5").Issues, i => i.Contains("full") && i.Contains("2 students"));
            var opt = d.TargetOptions.Single(o => o.ClassId == w.C2).Sections.Single(s => s.SectionId == w.C2A);
            Assert.Equal((2, 0), (opt.MaxStrength, opt.Enrolled));
            Assert.Equal(PromotionRowStatus.Ready, Row(d, w, "P6").Status);        // Class 2 B has no limit
        }

        [DbFact]
        public async Task A_class_the_target_year_does_not_offer_and_the_last_class_need_attention_or_a_different_choice()
        {
            using var db = new TaxDb();
            var w = Populate(db, "ZPI");
            var d = await Run(db, Req(w, cls: w.C2), w.School);                       // Class 2 -> Class 3 is not offered in the target year
            var q1 = Row(d, w, "Q1");
            Assert.Equal(PromotionRowStatus.NeedsAttention, q1.Status);
            Assert.Contains(q1.Issues, i => i.Contains("Class 3") && i.Contains("not offered"));
            Assert.Equal(PromotionRowStatus.NoDecision, Row(d, w, "Q2").Status);
            db.Exec($"update tb_classes set next_class_id = null where class_id = {w.C2}");           // now it is the last class
            var last = await Run(db, Req(w, cls: w.C2, edits: new[] {
                new PromotionDecisionInput { StudentId = w.School + "Q2", Decision = "PASS_OUT" }, new PromotionDecisionInput { StudentId = w.School + "Q3", Decision = "REPEAT" } }), w.School);
            Assert.True(last.IsLastClass); Assert.Null(last.NextClassId);
            Assert.Contains(Row(last, w, "Q1").Issues, i => i.Contains("last class"));
            Assert.Equal(PromotionRowStatus.Ready, Row(last, w, "Q2").Status);
            Assert.Equal(PromotionRowStatus.Ready, Row(last, w, "Q3").Status);
            Assert.Equal(w.C2, Row(last, w, "Q3").Target!.ClassId);
        }

        [DbFact]
        public async Task A_student_already_enrolled_or_already_recorded_is_skipped_and_nothing_is_ever_changed()
        {
            using var db = new TaxDb();
            var w = Populate(db, "ZPJ");
            var fp = Fp(db, w.School);
            var d = await Run(db, Req(w), w.School);
            Assert.Equal(PromotionRowStatus.AlreadyEnrolled, Row(d, w, "P7").Status);
            Assert.Equal(PromotionRowStatus.AlreadyRecorded, Row(d, w, "P8").Status);
            Assert.Null(Row(d, w, "P7").Target);
            Assert.Equal(fp, Fp(db, w.School));       // a preview writes nothing, whatever it shows
            await Run(db, Req(w, edits: new[] { new PromotionDecisionInput { StudentId = w.School + "P3", Decision = "PROMOTE" } }), w.School);
            Assert.Equal(fp, Fp(db, w.School));
        }

        [DbFact]
        public async Task The_fee_structure_warning_names_the_target_classes_that_have_none_and_only_for_rows_that_would_be_placed()
        {
            using var db = new TaxDb();
            var w = Populate(db, "ZPK");
            var d = await Run(db, Req(w), w.School);
            Assert.Equal(new[] { "Class 4" }, d.ClassesWithoutFeeStructure);          // P10 goes to Class 4, which has no fee structure in 2027-28
            Assert.True(d.NeedsFeeAcknowledgement);
            Assert.False(d.TargetOptions.Single(o => o.ClassId == w.C4).HasFeeStructure);
            Assert.True(d.TargetOptions.Single(o => o.ClassId == w.C2).HasFeeStructure);
            var none = await Run(db, Req(w, section: w.C1B), w.School);               // Class 1 B alone: everyone goes to Class 2 (or nowhere)
            Assert.Empty(none.ClassesWithoutFeeStructure); Assert.False(none.NeedsFeeAcknowledgement);
        }

        [DbFact]
        public async Task A_run_is_one_section_or_the_whole_class_and_over_500_asks_for_sections()
        {
            using var db = new TaxDb();
            var w = Populate(db, "ZPL");
            var section = await Run(db, Req(w, section: w.C1A), w.School);
            Assert.Equal(("SECTION", "A"), (section.Scope, section.SectionName));
            Assert.All(section.Rows, r => Assert.Equal(w.C1A, r.SourceSectionId));
            db.Exec($@"insert into tb_students (student_id, school_id, admission_number, first_name, last_name, date_of_birth, admission_type_id, admission_date, status_id)
                       select 'ZPLB' || g, '{w.School}', 'ZPLB' || g, 'Bulk', g::text, '2015-01-01', {NewAdm}, '2026-04-01', {Active} from generate_series(1, 501) g");
            db.Exec($@"insert into tb_student_enrollments (school_id, student_id, academic_year_id, class_section_id, roll_number, enrollment_date, admission_type_id, status_id)
                       select '{w.School}', 'ZPLB' || g, {w.S}, {w.C1B}, (100 + g)::text, '2026-04-01', {NewAdm}, {Active} from generate_series(1, 501) g");
            var tooMany = await Svc(db).PreviewAsync(w.School, Req(w), default);
            Assert.Equal(400, tooMany.StatusCode);
            Assert.Contains("one section at a time", tooMany.Message);
            Assert.Equal(400, (await Svc(db).PreviewAsync(w.School, Req(w, section: w.C1B), default)).StatusCode);   // 501 in the section is still too many
            Assert.True((await Svc(db).PreviewAsync(w.School, Req(w, section: w.C1A), default)).Status);
        }

        // ------------------------------------------------------------------------------------------------ years, schools, roles
        [DbFact]
        public async Task The_target_must_be_a_later_open_active_year_and_the_source_may_be_closed()
        {
            using var db = new TaxDb();
            var w = Populate(db, "ZPM");
            var same = Req(w); same.TargetYearId = w.S;
            Assert.Equal(400, (await Svc(db).PreviewAsync(w.School, same, default)).StatusCode);
            var backwards = Req(w); (backwards.SourceYearId, backwards.TargetYearId) = (w.T, w.S);
            var back = await Svc(db).PreviewAsync(w.School, backwards, default);
            Assert.Equal(400, back.StatusCode); Assert.Contains("must start after", back.Message);
            db.Exec($"update tb_academic_years set is_current=false, closed_at=now(), closed_by={TaxDb.UserA} where academic_year_id={w.S}");   // the source year closes: still fine
            var closedSource = await Run(db, Req(w), w.School);
            Assert.True(closedSource.SourceYearClosed);
            db.Exec($"update tb_academic_years set closed_at=now(), closed_by={TaxDb.UserA} where academic_year_id={w.T}");
            var closedTarget = await Svc(db).PreviewAsync(w.School, Req(w), default);
            Assert.Equal(400, closedTarget.StatusCode); Assert.Contains("closed", closedTarget.Message);
            db.Exec($"update tb_academic_years set closed_at=null, closed_by=null, status_id={Inactive} where academic_year_id={w.T}");
            Assert.Equal(400, (await Svc(db).PreviewAsync(w.School, Req(w), default)).StatusCode);
        }

        [DbFact]
        public async Task Another_schools_year_class_or_section_is_a_404_and_a_missing_school_is_a_400()
        {
            using var db = new TaxDb();
            var a = Populate(db, "ZPN"); var b = Build(db, "ZPO");
            var svc = Svc(db);
            var otherYear = Req(a); otherYear.TargetYearId = b.T;
            Assert.Equal(404, (await svc.PreviewAsync(a.School, otherYear, default)).StatusCode);
            var otherSource = Req(a); otherSource.SourceYearId = b.S;
            Assert.Equal(404, (await svc.PreviewAsync(a.School, otherSource, default)).StatusCode);
            Assert.Equal(404, (await svc.PreviewAsync(a.School, Req(a, cls: b.C1), default)).StatusCode);
            Assert.Equal(404, (await svc.PreviewAsync(a.School, Req(a, section: b.C1A), default)).StatusCode);
            Assert.Equal(404, (await svc.PreviewAsync(a.School, Req(a, section: a.C2A), default)).StatusCode);       // a section of ANOTHER class of the same school
            Assert.Equal(400, (await svc.PreviewAsync(null, Req(a), default)).StatusCode);
            // a decision for a student of another school (or one not in this run) is ignored: it names nobody in the answer
            var ghost = await Run(db, Req(a, edits: new[] { new PromotionDecisionInput { StudentId = "ZPOP1", Decision = "LEAVE" } }), a.School);
            Assert.DoesNotContain(ghost.Rows, r => r.StudentId == "ZPOP1");
        }

        [DbFact]
        public async Task An_unknown_decision_or_a_class_given_to_pass_out_needs_attention()
        {
            using var db = new TaxDb();
            var w = Populate(db, "ZPP");
            var d = await Run(db, Req(w, edits: new[] {
                new PromotionDecisionInput { StudentId = w.School + "P1", Decision = "EXPEL" },
                new PromotionDecisionInput { StudentId = w.School + "P2", Decision = "PASS_OUT", ToClassId = w.C2 },
                new PromotionDecisionInput { StudentId = w.School + "P3", Decision = "PROMOTE", ToClassId = w.C1 },
                new PromotionDecisionInput { StudentId = w.School + "P4", Decision = "REPEAT", ToClassId = w.C2 } }), w.School);
            Assert.All(new[] { "P1", "P2", "P3", "P4" }, s => Assert.Equal(PromotionRowStatus.NeedsAttention, Row(d, w, s).Status));
            Assert.Contains(Row(d, w, "P3").Issues, i => i.Contains("different class"));
        }

        public static IEnumerable<object[]> RoleCases() => Fake.AllRoles.Concat(new[] { "Driver" }).Select(r => new object[] { r });

        [Theory, MemberData(nameof(RoleCases))]
        public async Task Only_the_School_Admin_and_the_Principal_may_preview_a_promotion(string role)
        {
            var svc = Fake.Of<IPromotionService>();
            var c = Fake.WithUser(new PromotionController(svc), Fake.User("S", 5, role));
            var code = Fake.Code(await c.Preview(new PromotionPreviewRequest { SourceYearId = 1, TargetYearId = 2, ClassId = 3 }, default));
            var allowed = role is "School Admin" or "Principal";
            Assert.Equal(!allowed, code == 403);
            Assert.Equal(allowed, Fake.ProxyOf(svc).Calls.Contains("PreviewAsync"));
        }

        // ------------------------------------------------------------------------------------------------ the 4b-1 schema, as the API relies on it
        [DbFact]
        public void The_schema_refuses_what_promotion_must_never_store()
        {
            using var db = new TaxDb();
            var w = Populate(db, "ZPQ");
            string Try(string sql) { try { db.Exec(sql); return "ok"; } catch (Exception e) { return e.GetBaseException().Message.Split('\n')[0]; } }
            var e1 = db.Scalar<long>($"select enrollment_id from tb_student_enrollments where student_id='ZPQP1'");
            Assert.Contains("chk_tb_enrollments_outcome", Try($"update tb_student_enrollments set outcome='EXPELLED', outcome_at=now() where enrollment_id={e1}"));
            Assert.Contains("chk_tb_enrollments_outcome_pair", Try($"update tb_student_enrollments set outcome='LEFT' where enrollment_id={e1}"));
            Assert.Equal("ok", Try($"update tb_student_enrollments set outcome='PROMOTED', outcome_at=now() where enrollment_id={e1}"));
            Assert.Contains("chk_tb_promotion_batches_reason", Try($"insert into tb_promotion_batches (school_id, source_year_id, target_year_id, scope, class_id, promotion_date, created_by, override_count, override_reason) values ('{w.School}', {w.S}, {w.T}, 'CLASS', {w.C1}, '2027-03-01', {TaxDb.UserA}, 1, 'short')"));
            Assert.Contains("chk_tb_promotion_batches_scope", Try($"insert into tb_promotion_batches (school_id, source_year_id, target_year_id, scope, class_id, promotion_date, created_by) values ('{w.School}', {w.S}, {w.T}, 'SECTION', {w.C1}, '2027-03-01', {TaxDb.UserA})"));
            Assert.Contains("fk_tb_promotion_batches_target", Try($"insert into tb_promotion_batches (school_id, source_year_id, target_year_id, scope, class_id, promotion_date, created_by) values ('{w.School}', {w.S}, 999999, 'CLASS', {w.C1}, '2027-03-01', {TaxDb.UserA})"));
            Assert.Equal("ok", Try($"insert into tb_promotion_batches (school_id, source_year_id, target_year_id, scope, class_id, promotion_date, created_by) values ('{w.School}', {w.S}, {w.T}, 'CLASS', {w.C1}, '2027-03-01', {TaxDb.UserA})"));
            var b = db.Scalar<long>($"select batch_id from tb_promotion_batches where school_id='{w.School}'");
            Assert.Contains("chk_tb_promotion_items_target", Try($"insert into tb_promotion_items (batch_id, school_id, student_id, source_enrollment_id, decision, from_class_id, from_section_id) values ({b}, '{w.School}', 'ZPQP1', {e1}, 'PROMOTE', {w.C1}, {w.C1A})"));
            Assert.Contains("chk_tb_promotion_items_override", Try($"insert into tb_promotion_items (batch_id, school_id, student_id, source_enrollment_id, decision, from_class_id, from_section_id, override_applied) values ({b}, '{w.School}', 'ZPQP1', {e1}, 'LEAVE', {w.C1}, {w.C1A}, true)"));
            Assert.Equal("ok", Try($"insert into tb_promotion_items (batch_id, school_id, student_id, source_enrollment_id, decision, from_class_id, from_section_id) values ({b}, '{w.School}', 'ZPQP1', {e1}, 'LEAVE', {w.C1}, {w.C1A})"));
            Assert.Contains("uq_tb_promotion_items_batch_student", Try($"insert into tb_promotion_items (batch_id, school_id, student_id, source_enrollment_id, decision, from_class_id, from_section_id) values ({b}, '{w.School}', 'ZPQP1', {e1}, 'LEAVE', {w.C1}, {w.C1A})"));
            // another school's enrolment can never be pointed at
            Build(db, "ZPR"); Student(db, "ZPR", "ZPRS1"); Enrol(db, "ZPR", "ZPRS1", db.Scalar<long>("select academic_year_id from tb_academic_years where school_id='ZPR' and year_name='2026-27'"), db.Scalar<long>("select section_id from tb_sections s join tb_classes c using(class_id) where c.school_id='ZPR' and c.class_name='Class 1' and section_name='A'"), "1");
            var foreign = db.Scalar<long>("select enrollment_id from tb_student_enrollments where student_id='ZPRS1'");
            Assert.Contains("fk_tb_promotion_items_source", Try($"insert into tb_promotion_items (batch_id, school_id, student_id, source_enrollment_id, decision, from_class_id, from_section_id) values ({b}, '{w.School}', 'ZPQP2', {foreign}, 'LEAVE', {w.C1}, {w.C1A})"));
        }
    }
}
