using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using smss_api.Controllers;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;
using P = smss_api_tests.PromotionPreview4b2Tests;
using R = smss_api_tests.PromotionRun4b3Tests;

namespace smss_api_tests
{
    /// <summary>
    /// Stage 4b-4: the correction of ONE promotion, the history checks around promotion, passed-out and left students, and the lists that now mean "enrolled in the current-flagged year".
    /// Needs Docs/smss-st-4b4-RUN-ME.sql applied to the template (SMSS_TEST_TEMPLATE). Payments, marks and the like are throwaway rows on the scratch database only. [DbFact]: needs SMSS_TEST_PG.
    /// </summary>
    public class PromotionCorrection4b4Tests
    {
        private const string Reason = "Promoted to the wrong class by mistake";
        private static PromotionCorrectionRequest Req(P.W w, string student, long? year = null, string? reason = Reason, bool confirm = true) =>
            new() { StudentId = w.School + student, SourceYearId = year ?? w.S, Reason = reason, Confirm = confirm };

        private static async Task<PromotionCorrectionResultDto> Fix(TaxDb db, P.W w, string student)
        {
            var x = await P.Svc(db).CorrectAsync(w.School, TaxDb.UserA, Req(w, student), default);
            Assert.True(x.Status, x.Message);
            return x.Data!;
        }

        /// <summary>The world after one promotion run with fees: P1 is in Class 2 A of the new year with fee items and dues.</summary>
        private static async Task<P.W> Promoted(TaxDb db, string id)
        {
            var w = P.Populate(db, id);
            R.FeeLines(db, w);
            await R.Run(db, R.Exe(w), w.School);
            return w;
        }

        private static string Rows(TaxDb db, string sql) => db.Scalar<string>($"select coalesce(md5(string_agg(x::text, '|' order by x::text)), '') from ({sql}) x");
        private static string Everything(TaxDb db, P.W w) => P.Fp(db, w.School);

        // ------------------------------------------------------------------------------------------------ the correction itself
        [DbFact]
        public async Task A_promotion_is_corrected_the_new_enrolment_and_its_unpaid_fees_go_the_outcome_is_cleared_and_the_audit_row_says_who_when_why_and_what()
        {
            using var db = new TaxDb();
            var w = await Promoted(db, "ZRA");
            Assert.True(db.Scalar<int>($"select count(*) from tb_student_fee_items where student_id='{w.School}P1' and academic_year_id={w.T}") > 0);
            var oldColumns = R.OldColumns(db, w);
            var students = Rows(db, $"select * from tb_students where school_id='{w.School}'");
            var others = Rows(db, $"select * from tb_student_enrollments where school_id='{w.School}' and student_id <> '{w.School}P1'");
            var check = (await P.Svc(db).CheckCorrectionAsync(w.School, w.School + "P1", w.S, default)).Data!;
            Assert.True(check.CanCorrect); Assert.Empty(check.Blockers);
            Assert.Equal(("Class 2", "A", true), (check.WillRemove.ClassName, check.WillRemove.SectionName, check.WillRemove.Enrolment));
            Assert.True(check.WillRemove.FeeItems > 0 && check.WillRemove.Dues > 0 && check.WillRemove.FeeStructure);

            var r = await Fix(db, w, "P1");
            Assert.Equal("PROMOTED", r.OutcomeCleared);
            Assert.True(r.Removed.Enrolment); Assert.Equal(check.WillRemove.FeeItems, r.Removed.FeeItems); Assert.Equal(check.WillRemove.Dues, r.Removed.Dues);
            Assert.Equal(0, R.NewEnrol(db, w, "P1"));
            Assert.Equal(0, R.Count(db, $"select count(*) from tb_student_fee_items where student_id='{w.School}P1' and academic_year_id={w.T}"));
            Assert.Equal(0, R.Count(db, $"select count(*) from tb_student_fee_structure where student_id='{w.School}P1' and academic_year_id={w.T}"));
            Assert.Equal("-", R.Outcome(db, w, "P1", w.S));
            Assert.Equal(1, R.Count(db, $"select count(*) from tb_student_enrollments where student_id='{w.School}P1' and academic_year_id={w.S} and outcome is null and outcome_at is null"));
            Assert.Equal(oldColumns, R.OldColumns(db, w));                                                  // the old enrolment keeps everything else
            Assert.Equal(students, Rows(db, $"select * from tb_students where school_id='{w.School}'"));    // the student record is untouched
            Assert.Equal(others, Rows(db, $"select * from tb_student_enrollments where school_id='{w.School}' and student_id <> '{w.School}P1'"));   // nobody else changed
            // the audit: who, when, why, what, linked to the promotion batch and item
            Assert.Equal(1, R.Count(db, $@"select count(*) from tb_promotion_corrections c join tb_promotion_items i on i.item_id=c.item_id and i.school_id=c.school_id and i.batch_id=c.batch_id
                where c.school_id='{w.School}' and c.student_id='{w.School}P1' and c.reason='{Reason}' and c.corrected_by={TaxDb.UserA} and c.corrected_at is not null and c.outcome_cleared='PROMOTED' and c.decision='PROMOTE'
                  and c.source_enrollment_id=i.source_enrollment_id and i.reversed_at is not null and i.target_enrollment_id is null and c.removed_enrollment_id is not null
                  and c.removed->>'className'='Class 2' and (c.removed->>'feeItems')::int = {r.Removed.FeeItems} and (c.removed->>'dues')::int = {r.Removed.Dues}"));
            Assert.Equal(1, R.Count(db, $"select count(*) from tb_promotion_batches where school_id='{w.School}'"));                       // the promotion batch itself is kept
        }

        [DbFact]
        public async Task After_a_correction_the_student_can_be_promoted_again_through_the_normal_run()
        {
            using var db = new TaxDb();
            var w = await Promoted(db, "ZRB");
            await Fix(db, w, "P1");
            var again = await R.Run(db, R.Exe(w), w.School);
            Assert.Equal(1, again.Promoted);                                                                  // only P1: everyone else is already enrolled
            Assert.Equal(1, R.NewEnrol(db, w, "P1"));
            Assert.Equal("PROMOTED", R.Outcome(db, w, "P1", w.S));
            Assert.Equal(2, R.Count(db, $"select count(*) from tb_promotion_items where student_id='{w.School}P1' and skip_reason is null"));            // the reversed one and the new one
            Assert.Equal(1, R.Count(db, $"select count(*) from tb_promotion_items where student_id='{w.School}P1' and skip_reason is null and reversed_at is null"));
            // a second correction of the same promotion corrects the NEW one, and a third finds nothing
            await Fix(db, w, "P1");
            var none = await P.Svc(db).CorrectAsync(w.School, TaxDb.UserA, Req(w, "P1"), default);
            Assert.Equal(409, none.StatusCode); Assert.Contains("nothing to correct", none.Message);
            Assert.Equal(2, R.Count(db, $"select count(*) from tb_promotion_corrections where school_id='{w.School}' and student_id='{w.School}P1'"));
        }

        [DbFact]
        public async Task A_reason_of_ten_characters_and_the_confirmation_are_needed_and_a_refusal_changes_nothing()
        {
            using var db = new TaxDb();
            var w = await Promoted(db, "ZRC");
            var fp = Everything(db, w);
            Assert.Equal(400, (await P.Svc(db).CorrectAsync(w.School, TaxDb.UserA, Req(w, "P1", reason: null), default)).StatusCode);
            Assert.Equal(400, (await P.Svc(db).CorrectAsync(w.School, TaxDb.UserA, Req(w, "P1", reason: "too short"), default)).StatusCode);
            Assert.Equal(400, (await P.Svc(db).CorrectAsync(w.School, TaxDb.UserA, Req(w, "P1", confirm: false), default)).StatusCode);
            Assert.Equal(400, (await P.Svc(db).CorrectAsync(null, TaxDb.UserA, Req(w, "P1"), default)).StatusCode);
            Assert.Equal(fp, Everything(db, w));
        }

        // ------------------------------------------------------------------------------------------------ what blocks it
        [DbFact]
        public async Task Attendance_payments_marks_transport_and_concessions_in_the_new_year_each_refuse_the_correction_and_say_so_plainly()
        {
            using var db = new TaxDb();
            var w = await Promoted(db, "ZRD");
            var s = w.School + "P1";
            var cases = new (string Name, string Apply, string Undo, string Expect)[]
            {
                ("attendance",
                 $"insert into tb_student_attendance (school_id, academic_year_id, section_id, class_id, student_id, attendance_date, attendance_status_id, marked_by) select '{w.School}', {w.T}, {w.C2A}, {w.C2}, '{s}', '2027-04-05', (select min(attendance_status_id) from lut_attendance_status), {TaxDb.UserA}",
                 $"delete from tb_student_attendance where student_id='{s}'", "attendance record"),
                ("payment allocation",
                 $@"insert into tb_fee_payments (student_id, school_id, amount, payment_mode, receipt_number, fee_amount) values ('{s}', '{w.School}', 100, 'CASH', 'ZRD-1', 100);
                    insert into tb_fee_payment_allocations (payment_id, fee_due_id, allocated_amount, fee_type_name_snapshot) select (select payment_id from tb_fee_payments where receipt_number='ZRD-1'), min(d.fee_due_id), 100, 'Tuition' from tb_student_fee_dues d join tb_student_fee_items i using(student_fee_item_id) where i.student_id='{s}' and i.academic_year_id={w.T}",
                 "delete from tb_fee_payment_allocations where payment_id=(select payment_id from tb_fee_payments where receipt_number='ZRD-1'); delete from tb_fee_payments where receipt_number='ZRD-1'", "payment has been recorded"),
                ("a paid due",
                 $"update tb_student_fee_dues set amount_paid = 10 where fee_due_id = (select min(d.fee_due_id) from tb_student_fee_dues d join tb_student_fee_items i using(student_fee_item_id) where i.student_id='{s}' and i.academic_year_id={w.T})",
                 $"update tb_student_fee_dues set amount_paid = 0 where student_id='{s}'", "payment has been recorded"),
                ("marks",
                 $@"set session_replication_role = replica;
                    insert into tb_class_assessments (assessment_id, school_id, academic_year_id, class_id, section_id, subject_id, assessment_type_id, employee_id, title, test_date, max_marks, status_id) overriding system value values (990001, '{w.School}', {w.T}, {w.C2}, {w.C2A}, 1, 1, 'E1', 'T', '2027-05-01', 10, 1);
                    insert into tb_student_assessment_marks (assessment_id, student_id, obtained_marks) values (990001, '{s}', 7);",
                 "set session_replication_role = replica; delete from tb_student_assessment_marks where assessment_id=990001; delete from tb_class_assessments where assessment_id=990001", "Marks or report-card"),
                ("transport",
                 $"set session_replication_role = replica; insert into tb_student_transport (school_id, student_id, academic_year_id, area_id, status_id) values ('{w.School}', '{s}', {w.T}, 1, 1)",
                 $"delete from tb_student_transport where student_id='{s}'", "transport"),
                ("concession",
                 $@"insert into tb_fee_concessions (school_id, concession_name, concession_code, concession_type, value) values ('{w.School}', 'Sibling discount', 'SIB', 'PERCENTAGE', 10);
                    insert into tb_student_fee_concessions (student_id, concession_id, academic_year_id) select '{s}', concession_id, {w.T} from tb_fee_concessions where school_id='{w.School}' and concession_code='SIB'",
                 $"delete from tb_student_fee_concessions where student_id='{s}'; delete from tb_fee_concessions where school_id='{w.School}' and concession_code='SIB'", "Sibling discount"),
            };
            foreach (var c in cases)
            {
                db.Exec(c.Apply);
                var fp = Everything(db, w);
                var check = (await P.Svc(db).CheckCorrectionAsync(w.School, s, w.S, default)).Data!;
                Assert.False(check.CanCorrect, c.Name);
                Assert.Contains(check.Blockers, b => b.Contains(c.Expect, StringComparison.OrdinalIgnoreCase));
                var refused = await P.Svc(db).CorrectAsync(w.School, TaxDb.UserA, Req(w, "P1"), default);
                Assert.Equal(409, refused.StatusCode); Assert.Contains(c.Expect, refused.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(fp, Everything(db, w));                       // nothing was touched
                db.Exec(c.Undo);
            }
            Assert.True((await P.Svc(db).CheckCorrectionAsync(w.School, s, w.S, default)).Data!.CanCorrect);   // with the blockers gone it is allowed
        }

        [DbFact]
        public async Task A_closed_new_year_refuses_and_a_closed_old_year_is_allowed()
        {
            using var db = new TaxDb();
            var w = await Promoted(db, "ZRE");
            db.Exec($"update tb_academic_years set is_current=false, closed_at=now(), closed_by={TaxDb.UserA} where academic_year_id={w.S}");
            var old = R.OldColumns(db, w);
            await Fix(db, w, "P1");                                                                  // the OLD year is closed: its outcome may be cleared, nothing else of it changes
            Assert.Equal(old, R.OldColumns(db, w));
            Assert.Equal("-", R.Outcome(db, w, "P1", w.S));
            db.Exec($"update tb_academic_years set closed_at=now(), closed_by={TaxDb.UserA} where academic_year_id={w.T}");
            var fp = Everything(db, w);
            var refused = await P.Svc(db).CorrectAsync(w.School, TaxDb.UserA, Req(w, "P2"), default);
            Assert.Equal(409, refused.StatusCode); Assert.Contains("closed", refused.Message);
            Assert.Equal(fp, Everything(db, w));
        }

        [DbFact]
        public async Task Only_what_a_promotion_run_recorded_can_be_corrected()
        {
            using var db = new TaxDb();
            var w = await Promoted(db, "ZRF");
            var fp = Everything(db, w);
            var p8 = await P.Svc(db).CorrectAsync(w.School, TaxDb.UserA, Req(w, "P8"), default);          // PASSED_OUT written by hand, never by a run
            Assert.Equal(409, p8.StatusCode); Assert.Contains("not recorded by a promotion run", p8.Message);
            var p3 = await P.Svc(db).CorrectAsync(w.School, TaxDb.UserA, Req(w, "P3"), default);          // never promoted
            Assert.Equal(409, p3.StatusCode); Assert.Contains("nothing to correct", p3.Message);
            Assert.Equal(404, (await P.Svc(db).CorrectAsync(w.School, TaxDb.UserA, Req(w, "NOPE"), default)).StatusCode);
            var fromNew = await P.Svc(db).CorrectAsync(w.School, TaxDb.UserA, Req(w, "P1", year: w.T), default);   // the NEW year's enrolment has no outcome: there is nothing to correct out of it
            Assert.Equal(409, fromNew.StatusCode); Assert.Contains("nothing to correct", fromNew.Message);
            Assert.Equal(fp, Everything(db, w));
        }

        [DbFact]
        public async Task A_passed_out_or_left_student_can_be_corrected_too_there_is_no_enrolment_to_remove()
        {
            using var db = new TaxDb();
            var w = P.Populate(db, "ZRG");
            db.Exec($"update tb_classes set next_class_id = null where class_id = {w.C2}");
            await R.Run(db, R.Exe(w, cls: w.C2, edits: new[] { R.D(w, "Q1", "PASS_OUT"), R.D(w, "Q2", "LEAVE") }), w.School);
            Assert.Equal(("PASSED_OUT", "LEFT"), (R.Outcome(db, w, "Q1", w.S), R.Outcome(db, w, "Q2", w.S)));
            var r = await Fix(db, w, "Q1");
            Assert.False(r.Removed.Enrolment); Assert.Equal("PASSED_OUT", r.OutcomeCleared);
            Assert.Equal("-", R.Outcome(db, w, "Q1", w.S));
            await Fix(db, w, "Q2");
            var again = await R.Run(db, R.Exe(w, cls: w.C2, edits: new[] { R.D(w, "Q1", "REPEAT") }), w.School);   // Q1 can now be repeated instead
            Assert.Equal(1, again.Repeated);
            Assert.Equal(1, R.NewEnrol(db, w, "Q1"));
        }

        [DbFact]
        public async Task Another_schools_student_is_a_404_and_two_corrections_at_once_do_it_once()
        {
            using var db = new TaxDb();
            var a = await Promoted(db, "ZRH"); var b = await Promoted(db, "ZRI");
            var fp = Everything(db, b);
            var x = await P.Svc(db).CorrectAsync(a.School, TaxDb.UserA, new PromotionCorrectionRequest { StudentId = b.School + "P1", SourceYearId = a.S, Reason = Reason, Confirm = true }, default);
            Assert.Equal(404, x.StatusCode);
            var y = await P.Svc(db).CorrectAsync(a.School, TaxDb.UserA, new PromotionCorrectionRequest { StudentId = b.School + "P1", SourceYearId = b.S, Reason = Reason, Confirm = true }, default);
            Assert.Equal(404, y.StatusCode);
            Assert.Equal(fp, Everything(db, b));
            var both = await Task.WhenAll(Task.Run(() => P.Svc(db).CorrectAsync(a.School, TaxDb.UserA, Req(a, "P1"), default)), Task.Run(() => P.Svc(db).CorrectAsync(a.School, TaxDb.UserA, Req(a, "P1"), default)));
            Assert.Equal(1, both.Count(r => r.Status));
            Assert.Equal(1, R.Count(db, $"select count(*) from tb_promotion_corrections where school_id='{a.School}' and student_id='{a.School}P1'"));
        }

        public static IEnumerable<object[]> RoleCases() => Fake.AllRoles.Concat(new[] { "Driver" }).Select(r => new object[] { r });

        [Theory, MemberData(nameof(RoleCases))]
        public async Task Only_the_School_Admin_and_the_Principal_may_check_or_correct_a_promotion(string role)
        {
            var svc = Fake.Of<IPromotionService>();
            var c = Fake.WithUser(new PromotionController(svc), Fake.User("S", 5, role));
            var check = Fake.Code(await c.CheckCorrection("ST1", 1, default));
            var run = Fake.Code(await c.Correct(new PromotionCorrectionRequest { StudentId = "ST1", SourceYearId = 1, Reason = Reason, Confirm = true }, default));
            var allowed = role is "School Admin" or "Principal";
            Assert.Equal(!allowed, check == 403); Assert.Equal(!allowed, run == 403);
            Assert.Equal(allowed, Fake.ProxyOf(svc).Calls.Contains("CorrectAsync"));
        }

        // ------------------------------------------------------------------------------------------------ history after a promotion
        [DbFact]
        public async Task After_a_promotion_the_old_year_looks_exactly_as_before_and_both_years_show_for_the_student()
        {
            using var db = new TaxDb();
            var w = P.Populate(db, "ZRJ");
            R.FeeLines(db, w);
            // the old year's fees, a receipt and attendance exist before the run
            P.Due(db, w, w.School + "P1", "2026-06-10", 200, 200);
            db.Exec($"insert into tb_fee_payments (student_id, school_id, amount, payment_mode, receipt_number, fee_amount) values ('{w.School}P1', '{w.School}', 200, 'CASH', 'ZRJ-1', 200)");
            db.Exec($"insert into tb_fee_payment_allocations (payment_id, fee_due_id, allocated_amount, fee_type_name_snapshot) select (select payment_id from tb_fee_payments where receipt_number='ZRJ-1'), min(d.fee_due_id), 200, 'Tuition' from tb_student_fee_dues d where d.student_id='{w.School}P1'");
            db.Exec($"insert into tb_student_attendance (school_id, academic_year_id, section_id, class_id, student_id, attendance_date, attendance_status_id, marked_by) select '{w.School}', {w.S}, {w.C1A}, {w.C1}, '{w.School}P1', '2026-06-01', (select min(attendance_status_id) from lut_attendance_status), {TaxDb.UserA}");
            var oldYear = $@"select i::text from tb_student_fee_items i where i.school_id='{w.School}' and i.academic_year_id={w.S}
                             union all select d::text from tb_student_fee_dues d join tb_student_fee_items i using(student_fee_item_id) where i.school_id='{w.School}' and i.academic_year_id={w.S}
                             union all select p::text from tb_fee_payments p where p.school_id='{w.School}' union all select a::text from tb_fee_payment_allocations a
                             union all select t::text from tb_student_attendance t where t.school_id='{w.School}' and t.academic_year_id={w.S}
                             union all select e::text from tb_student_enrollments e where e.school_id='{w.School}' and e.academic_year_id={w.S} and false
                             union all select s.student_id || s.admission_number from tb_students s where s.school_id='{w.School}'";
            var before = Rows(db, oldYear);
            var c = db.Ctx();
            var dash = new SchoolAdminDashboardService(new SchoolAdminDashboardRepository(c), new SchoolDashboardRepository(c), NullLogger<SchoolAdminDashboardService>.Instance);
            var today = new DateOnly(2026, 11, 25);
            var dashBefore = JsonSerializer.Serialize((await dash.GetAsync(w.School, default, today, w.S)).Data);
            var rosterBefore = (await new StudentRepository(db.Ctx()).GetEnrolledStudentsAsync(w.School, w.S, w.C1A, default)).Select(r => (r.StudentId, r.RollNumber)).ToList();

            await R.Run(db, R.Exe(w), w.School);

            Assert.Equal(before, Rows(db, oldYear));                                                              // fees, dues, receipts, attendance and the identity of the old year: exactly as before
            var c2 = db.Ctx();
            var dashAfter = JsonSerializer.Serialize((await new SchoolAdminDashboardService(new SchoolAdminDashboardRepository(c2), new SchoolDashboardRepository(c2), NullLogger<SchoolAdminDashboardService>.Instance).GetAsync(w.School, default, today, w.S)).Data);
            Assert.Equal(dashBefore, dashAfter);                                                                   // the old year's dashboard shows the same figures
            Assert.Equal(rosterBefore, (await new StudentRepository(db.Ctx()).GetEnrolledStudentsAsync(w.School, w.S, w.C1A, default)).Select(r => (r.StudentId, r.RollNumber)).ToList());   // the old roster is whole
            // the student profile's year tabs: both years, and the identity never changes
            var tabs = await new StudentRepository(db.Ctx()).GetEnrollmentsAsync(w.School, w.School + "P1", default);
            Assert.Equal(new[] { w.S, w.T }, tabs.Select(t => t.AcademicYearId).OrderBy(x => x).ToArray());
            Assert.Equal(("P1-N", w.School + "P1"), (db.Scalar<string>($"select admission_number from tb_students where student_id='{w.School}P1'").Replace(w.School, ""), db.Scalar<string>($"select student_id from tb_students where student_id='{w.School}P1'")));
            var years = ((await new AcademicYearService(new AcademicYearRepository(db.Ctx()), NullLogger<AcademicYearService>.Instance).GetAllAsync(w.School)).Data as IEnumerable<AcademicYearResponse>)!.ToList();   // the year selector lists both
            Assert.Contains(years, y => y.AcademicYearId == w.S); Assert.Contains(years, y => y.AcademicYearId == w.T);
        }

        [DbFact]
        public async Task Promotion_never_touches_guardian_links_portal_access_documents_or_the_student_record()
        {
            using var db = new TaxDb();
            var w = P.Populate(db, "ZRK");
            string[] tables = { "tb_students", "tb_student_guardians", "tb_guardians", "tb_student_documents" };
            string Where(string t) => t == "tb_student_guardians" ? $"student_id in (select student_id from tb_students where school_id='{w.School}')" : $"school_id='{w.School}'";
            string Fp() => string.Join("|", tables.Select(t => Rows(db, $"select * from {t} where {Where(t)}")));
            string Portal() => Rows(db, $"select student_id, guardian_id, portal_access, is_primary from tb_student_guardians l join tb_students s using(student_id) where s.school_id='{w.School}'");
            var before = Fp(); var portal = Portal();
            await R.Run(db, R.Exe(w, edits: new[] { R.D(w, "P2", "PASS_OUT") }), w.School);
            Assert.Equal(before, Fp());
            Assert.Equal(portal, Portal());
            await Fix(db, w, "P1");
            Assert.Equal(before, Fp());                                                                            // and a correction is just as careful
        }

        // ------------------------------------------------------------------------------------------------ passed out and left
        [DbFact]
        public async Task A_student_who_passed_out_or_left_keeps_their_status_leaves_the_current_year_and_stays_in_the_old_year()
        {
            using var db = new TaxDb();
            var w = P.Populate(db, "ZRL");
            var status = Rows(db, $"select student_id, status_id from tb_students where school_id='{w.School}'");
            await R.Run(db, R.Exe(w, cls: w.C1, edits: new[] { R.D(w, "P2", "PASS_OUT"), R.D(w, "P3", "LEAVE") }), w.School);
            Assert.Equal(status, Rows(db, $"select student_id, status_id from tb_students where school_id='{w.School}'"));   // tb_students.status is unchanged
            Assert.Equal(("PASSED_OUT", "LEFT"), (R.Outcome(db, w, "P2", w.S), R.Outcome(db, w, "P3", w.S)));
            // the old year still shows both (the roster, the student's year tabs)
            var roster = await new StudentRepository(db.Ctx()).GetEnrolledStudentsAsync(w.School, w.S, w.C1A, default);
            Assert.Contains(roster, r => r.StudentId == w.School + "P2"); Assert.Contains(roster, r => r.StudentId == w.School + "P3");
            Assert.Single(await new StudentRepository(db.Ctx()).GetEnrollmentsAsync(w.School, w.School + "P2", default));
            // once the new year is the current one they are in none of its rosters, lists or counts
            db.Exec($"update tb_academic_years set is_current=false where academic_year_id={w.S}");
            db.Exec($"update tb_academic_years set is_current=true where academic_year_id={w.T}");
            Assert.Empty((await new StudentRepository(db.Ctx()).GetEnrolledStudentsAsync(w.School, w.T, w.C1A, default)).Where(r => r.StudentId == w.School + "P2" || r.StudentId == w.School + "P3"));
            var pending = (await P.Svc(db).GetPendingAsync(w.School, true, default)).Data!;
            Assert.DoesNotContain(pending.Rows, r => r.StudentId == w.School + "P2" || r.StudentId == w.School + "P3");
            var docs = await new StudentDocumentRepository(db.Ctx()).ListPendingAsync(w.School, db.Scalar<int>($"select sid from lut_status where sname='Active' and stype='general status'"), db.Scalar<int>($"select sid from lut_status where sname='Active' and stype='general status'"), null, null, null, default);
            Assert.DoesNotContain(docs, d => d.StudentId == w.School + "P2" || d.StudentId == w.School + "P3");
        }

        [DbFact]
        public async Task The_pending_documents_list_needs_an_active_student_AND_an_active_enrolment_in_the_current_flagged_year()
        {
            using var db = new TaxDb();
            var w = P.Populate(db, "ZRM");
            var active = db.Scalar<int>("select sid from lut_status where sname='Active' and stype='general status'");
            db.Exec($@"insert into tb_document_types (school_id, document_name, document_code, applies_to, is_required, required_from, status_id) values ('{w.School}', 'Birth certificate', 'BC', 'STUDENT', true, '2020-01-01', {active})");
            var docsRepo = () => new StudentDocumentRepository(db.Ctx());
            var inCurrent = await docsRepo().ListPendingAsync(w.School, active, active, null, null, null, default);
            Assert.Contains(inCurrent, d => d.StudentId == w.School + "P1");                                      // enrolled in the current-flagged year (2026-27)
            Assert.DoesNotContain(inCurrent, d => d.StudentId == w.School + "X1");                                // X1 is only enrolled in the NEXT year: not on the list yet
            Assert.Equal(inCurrent.Count, await docsRepo().CountPendingStudentsAsync(w.School, active, active, default));
            // an INACTIVE student who is still enrolled is excluded (and comes back when reactivated)
            db.Exec($"update tb_students set status_id=(select sid from lut_status where sname='Inactive' and stype='general status') where student_id='{w.School}P1'");
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_student_enrollments where student_id='{w.School}P1' and academic_year_id={w.S} and status_id={active}"));   // still enrolled
            Assert.DoesNotContain(await docsRepo().ListPendingAsync(w.School, active, active, null, null, null, default), d => d.StudentId == w.School + "P1");
            Assert.Equal(inCurrent.Count - 1, await docsRepo().CountPendingStudentsAsync(w.School, active, active, default));
            db.Exec($"update tb_students set status_id={active} where student_id='{w.School}P1'");
            Assert.Contains(await docsRepo().ListPendingAsync(w.School, active, active, null, null, null, default), d => d.StudentId == w.School + "P1");
            db.Exec($"update tb_academic_years set is_current=false where academic_year_id={w.S}; update tb_academic_years set is_current=true where academic_year_id={w.T}");
            var inNext = await docsRepo().ListPendingAsync(w.School, active, active, null, null, null, default);
            Assert.DoesNotContain(inNext, d => d.StudentId == w.School + "P1");                                   // P1 has no enrolment in the new current year: off the list
            Assert.Contains(inNext, d => d.StudentId == w.School + "X1");                                         // X1 does
            Assert.Equal(inNext.Count, await docsRepo().CountPendingStudentsAsync(w.School, active, active, default));
            db.Exec($"update tb_academic_years set is_current=false where school_id='{w.School}'");               // no current year: nobody is "enrolled in the current year"
            Assert.Empty(await docsRepo().ListPendingAsync(w.School, active, active, null, null, null, default));
            Assert.Equal(0, await docsRepo().CountPendingStudentsAsync(w.School, active, active, default));
        }

        [DbFact]
        public async Task Subscription_billing_counts_students_enrolled_in_the_current_flagged_year_so_a_year_switch_moves_the_count_and_passed_out_students_drop_out()
        {
            using var db = new TaxDb();
            var w = P.Populate(db, "ZRN");
            var active = db.Scalar<int>("select sid from lut_status where sname='Active' and stype='general status'");
            var repo = () => new SubscriptionRepository(db.Ctx());
            var before = (await repo().CountBillableUsersAsync(w.School, active, default)).Students;
            Assert.Equal(db.Scalar<int>($"select count(distinct student_id) from tb_student_enrollments e join tb_students s using(student_id) where e.school_id='{w.School}' and e.academic_year_id={w.S} and e.status_id={active} and s.status_id={active}"), before);
            await R.Run(db, R.Exe(w, edits: new[] { R.D(w, "P2", "PASS_OUT") }), w.School);
            Assert.Equal(before, (await repo().CountBillableUsersAsync(w.School, active, default)).Students);       // the run alone changes nothing: the old year is still the current one
            db.Exec($"update tb_academic_years set is_current=false where academic_year_id={w.S}; update tb_academic_years set is_current=true where academic_year_id={w.T}");
            var after = (await repo().CountBillableUsersAsync(w.School, active, default)).Students;
            Assert.Equal(db.Scalar<int>($"select count(distinct student_id) from tb_student_enrollments e where e.school_id='{w.School}' and e.academic_year_id={w.T}"), after);   // only who is in the new year
            Assert.True(after < before);                                                                              // the passed-out student and the not-yet-promoted ones are not billed
        }
    }
}
