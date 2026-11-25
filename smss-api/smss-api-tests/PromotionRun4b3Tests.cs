using Microsoft.AspNetCore.Mvc;
using smss_api.Controllers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using Xunit;
using P = smss_api_tests.PromotionPreview4b2Tests;

namespace smss_api_tests
{
    /// <summary>
    /// Stage 4b-3: the promotion RUN (execute, override, audit), the pending list, and the guarantees around them. Same world as the preview tests (Docs/smss-st-4b1-RUN-ME.sql applied to the template).
    /// Dues and payments are throwaway rows on the scratch database only. [DbFact]: needs SMSS_TEST_PG and SMSS_TEST_TEMPLATE.
    /// </summary>
    public class PromotionRun4b3Tests
    {
        private const string Active = "(select sid from lut_status where sname='Active' and stype='general status')";

        private static PromotionExecuteRequest Exe(P.W w, long? cls = null, long? section = null, DateOnly? date = null, PromotionDecisionInput[]? edits = null,
            bool confirm = true, bool ack = true, string? reason = null, string[]? overrides = null, bool backdate = false)
        {
            var r = P.Req(w, cls, section, date, edits);
            return new PromotionExecuteRequest
            {
                SourceYearId = r.SourceYearId, TargetYearId = r.TargetYearId, ClassId = r.ClassId, SectionId = r.SectionId, PromotionDate = r.PromotionDate, Decisions = r.Decisions,
                Confirm = confirm, AcknowledgeNoFeeStructure = ack, OverrideReason = reason, OverrideStudentIds = overrides?.Select(s => w.School + s).ToList(), ConfirmBackdating = backdate,
            };
        }
        private static PromotionDecisionInput D(P.W w, string s, string decision) => new() { StudentId = w.School + s, Decision = decision };

        private static async Task<PromotionResultDto> Run(TaxDb db, PromotionExecuteRequest r, string school)
        {
            var x = await P.Svc(db).ExecuteAsync(school, TaxDb.UserA, r, default);
            Assert.True(x.Status, x.Message);
            return x.Data!;
        }

        private static string OldColumns(TaxDb db, P.W w) => db.Scalar<string>($@"select md5(coalesce(string_agg(concat_ws('|', enrollment_id, school_id, student_id, academic_year_id, class_section_id, roll_number, enrollment_date, admission_type_id, status_id, created_at, updated_at), E'\n' order by enrollment_id), ''))
            from tb_student_enrollments where school_id='{w.School}' and academic_year_id={w.S}");

        private static string Outcome(TaxDb db, P.W w, string s, long year) => db.Scalar<string>($"select coalesce(outcome, '-') from tb_student_enrollments where student_id='{w.School}{s}' and academic_year_id={year}");
        private static int Count(TaxDb db, string sql) => db.Scalar<int>(sql);
        private static int NewEnrol(TaxDb db, P.W w, string s) => Count(db, $"select count(*) from tb_student_enrollments where student_id='{w.School}{s}' and academic_year_id={w.T}");

        private static void FeeLines(TaxDb db, P.W w) =>
            db.Exec($@"insert into tb_fee_structure_details (fee_structure_id, fee_type_id, frequency_id, amount, due_day, effective_from)
                       select fs.fee_structure_id, (select fee_type_id from tb_fee_types where school_id='{w.School}' and fee_type_code='TUI'), (select frequency_id from lut_fee_frequency where frequency_code='MONTHLY'), 1000, 10, '2026-04-01'
                       from tb_fee_structures fs where fs.school_id='{w.School}' and fs.academic_year_id={w.T}");

        // ------------------------------------------------------------------------------------------------ the run and its audit
        [DbFact]
        public async Task A_run_needs_the_confirmation_and_the_fee_acknowledgement_and_a_refusal_writes_nothing()
        {
            using var db = new TaxDb();
            var w = P.Populate(db, "ZQA");
            var fp = P.Fp(db, w.School);
            var noConfirm = await P.Svc(db).ExecuteAsync(w.School, TaxDb.UserA, Exe(w, confirm: false), default);
            Assert.Equal(400, noConfirm.StatusCode);
            var noAck = await P.Svc(db).ExecuteAsync(w.School, TaxDb.UserA, Exe(w, ack: false), default);       // P10 goes to Class 4, which has no fee structure
            Assert.Equal(400, noAck.StatusCode); Assert.Contains("no fee structure", noAck.Message);
            Assert.Equal(400, (await P.Svc(db).ExecuteAsync(null, TaxDb.UserA, Exe(w), default)).StatusCode);
            Assert.Equal(fp, P.Fp(db, w.School));
        }

        [DbFact]
        public async Task The_run_adds_the_new_enrolments_records_the_outcome_only_on_the_old_ones_and_audits_every_student()
        {
            using var db = new TaxDb();
            var w = P.Populate(db, "ZQB");
            var old = OldColumns(db, w);
            var r = await Run(db, Exe(w), w.School);
            Assert.Equal((4, 1, 0, 0), (r.Promoted, r.Repeated, r.PassedOut, r.Left));        // P1 P5 P6 P10 up, P2 again
            Assert.Equal(2, r.Skipped);                                                          // P4 (dues), P7 (already enrolled)
            Assert.Equal(2, r.LeftOut);                                                          // P3 (no decision), P8 (outcome already recorded): not in the audit
            Assert.NotNull(r.BatchId);
            // new enrolments: the right class/section, roll, Active, the date is the year's first day
            Assert.Equal(1, Count(db, $"select count(*) from tb_student_enrollments where student_id='{w.School}P1' and academic_year_id={w.T} and class_section_id={w.C2A} and roll_number='1' and enrollment_date='2027-04-01' and status_id={Active}"));
            Assert.Equal(1, Count(db, $"select count(*) from tb_student_enrollments where student_id='{w.School}P2' and academic_year_id={w.T} and class_section_id={w.C1A}"));
            Assert.Equal(0, NewEnrol(db, w, "P3")); Assert.Equal(0, NewEnrol(db, w, "P4"));
            // the old enrolments keep class, section, roll and status; only the outcome is new
            Assert.Equal(old, OldColumns(db, w));
            Assert.Equal(("PROMOTED", "REPEATED", "-", "-"), (Outcome(db, w, "P1", w.S), Outcome(db, w, "P2", w.S), Outcome(db, w, "P3", w.S), Outcome(db, w, "P4", w.S)));
            Assert.Equal(1, Count(db, $"select count(*) from tb_student_enrollments where student_id='{w.School}P1' and academic_year_id={w.S} and outcome_at is not null"));
            // the audit: one batch, one item per student with a decision (7), with the amount owed at that moment
            Assert.Equal(1, Count(db, $"select count(*) from tb_promotion_batches where school_id='{w.School}' and created_by={TaxDb.UserA} and promotion_date='2027-03-01' and students_total=7 and promoted_count=4 and repeated_count=1 and skipped_count=2 and override_count=0 and no_fee_structure_acknowledged"));
            Assert.Equal(7, Count(db, $"select count(*) from tb_promotion_items where school_id='{w.School}'"));
            Assert.Equal(800m, db.Scalar<decimal>($"select outstanding_amount from tb_promotion_items where student_id='{w.School}P4'"));
            Assert.Contains("Unpaid dues", db.Scalar<string>($"select skip_reason from tb_promotion_items where student_id='{w.School}P4'"));
            Assert.Equal(0, Count(db, $"select count(*) from tb_promotion_items where student_id='{w.School}P4' and target_enrollment_id is not null"));
            Assert.Equal(1, Count(db, $"select count(*) from tb_promotion_items i join tb_student_enrollments e on e.enrollment_id=i.target_enrollment_id where i.student_id='{w.School}P1' and i.to_class_id={w.C2} and i.to_section_id={w.C2A} and i.roll_number='1' and i.decision='PROMOTE'"));
            Assert.Equal(1, Count(db, $"select count(*) from tb_promotion_items where student_id='{w.School}P7' and skip_reason is not null and decision='PROMOTE'"));
        }

        [DbFact]
        public async Task A_second_run_creates_nothing_and_writes_no_batch()
        {
            using var db = new TaxDb();
            var w = P.Populate(db, "ZQC");
            Assert.NotNull((await Run(db, Exe(w), w.School)).BatchId);
            var fp = P.Fp(db, w.School);
            var again = await Run(db, Exe(w), w.School);
            Assert.Null(again.BatchId);
            Assert.Equal((0, 0, 0, 0), (again.Promoted, again.Repeated, again.PassedOut, again.Left));
            Assert.Equal(fp, P.Fp(db, w.School));
        }

        [DbFact]
        public async Task Two_runs_at_once_queue_on_the_lock_and_the_second_changes_nothing()
        {
            using var db = new TaxDb();
            var w = P.Populate(db, "ZQD");
            var results = await Task.WhenAll(Task.Run(() => P.Svc(db).ExecuteAsync(w.School, TaxDb.UserA, Exe(w), default)), Task.Run(() => P.Svc(db).ExecuteAsync(w.School, TaxDb.UserA, Exe(w), default)));
            Assert.All(results, x => Assert.True(x.Status, x.Message));
            Assert.Equal(1, results.Count(x => x.Data!.BatchId != null));
            Assert.Equal(1, Count(db, $"select count(*) from tb_promotion_batches where school_id='{w.School}'"));
            Assert.Equal(1, Count(db, $"select count(*) from tb_student_enrollments where student_id='{w.School}P1' and academic_year_id={w.T}"));
            Assert.Equal(5, Count(db, $"select count(*) from tb_student_enrollments where school_id='{w.School}' and academic_year_id={w.T} and student_id in ('{w.School}P1','{w.School}P2','{w.School}P5','{w.School}P6','{w.School}P10')"));
        }

        // ------------------------------------------------------------------------------------------------ dues block and override
        [DbFact]
        public async Task A_blocked_student_is_overridden_only_when_ticked_with_a_reason_that_is_stored_and_no_payment_is_written()
        {
            using var db = new TaxDb();
            var w = P.Populate(db, "ZQE");
            var dues = db.Scalar<string>($"select md5(string_agg(d::text, '|' order by fee_due_id)) from tb_student_fee_dues d where school_id='{w.School}'");
            var payments = Count(db, "select count(*) from tb_fee_payments");
            Assert.Equal(400, (await P.Svc(db).ExecuteAsync(w.School, TaxDb.UserA, Exe(w, overrides: new[] { "P4" }), default)).StatusCode);                       // no reason
            Assert.Equal(400, (await P.Svc(db).ExecuteAsync(w.School, TaxDb.UserA, Exe(w, overrides: new[] { "P4" }, reason: "too short"), default)).StatusCode);    // 9 characters
            Assert.Equal(0, NewEnrol(db, w, "P4"));
            var r = await Run(db, Exe(w, overrides: new[] { "P4", "P1" }, reason: "Parents promised to pay in April"), w.School);   // P1 is not blocked: its tick is ignored
            Assert.Equal(1, r.Overridden);
            Assert.Equal(PromotionItemOutcome.Overridden, r.Items.Single(i => i.StudentId == w.School + "P4").Outcome);
            Assert.Equal(PromotionItemOutcome.Done, r.Items.Single(i => i.StudentId == w.School + "P1").Outcome);
            Assert.Equal(1, NewEnrol(db, w, "P4")); Assert.Equal("PROMOTED", Outcome(db, w, "P4", w.S));
            Assert.Equal(1, Count(db, $"select count(*) from tb_promotion_items where student_id='{w.School}P4' and override_applied and override_reason='Parents promised to pay in April' and outstanding_amount=800"));
            Assert.Equal(0, Count(db, $"select count(*) from tb_promotion_items where student_id='{w.School}P1' and override_applied"));
            Assert.Equal(1, Count(db, $"select count(*) from tb_promotion_batches where school_id='{w.School}' and override_count=1 and override_reason='Parents promised to pay in April'"));
            // nothing was paid and nothing was carried forward: the old dues are exactly as they were
            Assert.Equal(dues, db.Scalar<string>($"select md5(string_agg(d::text, '|' order by fee_due_id)) from tb_student_fee_dues d where school_id='{w.School}'"));
            Assert.Equal(payments, Count(db, "select count(*) from tb_fee_payments"));
        }

        [DbFact]
        public async Task The_dues_block_and_its_override_apply_to_pass_out_and_leave_too()
        {
            using var db = new TaxDb();
            var w = P.Populate(db, "ZQF");
            Due(db, w, "P3", "2026-09-01", 100, 0);
            var edits = new[] { D(w, "P4", "PASS_OUT"), D(w, "P3", "LEAVE") };
            var blocked = await Run(db, Exe(w, edits: edits), w.School);
            Assert.Equal(0, blocked.PassedOut + blocked.Left);
            Assert.Equal(("-", "-"), (Outcome(db, w, "P4", w.S), Outcome(db, w, "P3", w.S)));
            var r = await Run(db, Exe(w, edits: edits, overrides: new[] { "P4" }, reason: "Cleared in cash at the office"), w.School);   // only P4 is ticked
            Assert.Equal((1, 0, 1), (r.PassedOut, r.Left, r.Overridden));
            Assert.Equal(("PASSED_OUT", "-"), (Outcome(db, w, "P4", w.S), Outcome(db, w, "P3", w.S)));
            Assert.Equal(0, NewEnrol(db, w, "P4"));
        }

        private static void Due(TaxDb db, P.W w, string s, string date, decimal due, decimal paid) => P.Due(db, w, w.School + s, date, due, paid);

        // ------------------------------------------------------------------------------------------------ the other decisions, capacity and years
        [DbFact]
        public async Task Last_class_pass_out_leave_and_repeat_record_the_outcome_and_only_repeat_adds_an_enrolment()
        {
            using var db = new TaxDb();
            var w = P.Populate(db, "ZQG");
            db.Exec($"update tb_classes set next_class_id = null where class_id = {w.C2}");
            var r = await Run(db, Exe(w, cls: w.C2, edits: new[] { D(w, "Q1", "PASS_OUT"), D(w, "Q2", "LEAVE"), D(w, "Q3", "REPEAT") }), w.School);
            Assert.Equal((0, 1, 1, 1), (r.Promoted, r.Repeated, r.PassedOut, r.Left));
            Assert.Equal(("PASSED_OUT", "LEFT", "REPEATED"), (Outcome(db, w, "Q1", w.S), Outcome(db, w, "Q2", w.S), Outcome(db, w, "Q3", w.S)));
            Assert.Equal((0, 0, 1), (NewEnrol(db, w, "Q1"), NewEnrol(db, w, "Q2"), NewEnrol(db, w, "Q3")));
            Assert.Equal(1, Count(db, $"select count(*) from tb_promotion_items where student_id='{w.School}Q1' and decision='PASS_OUT' and target_enrollment_id is null and to_class_id is null"));
            Assert.Equal(w.C2, db.Scalar<long>($"select to_class_id from tb_promotion_items where student_id='{w.School}Q3'"));
            // a student who passed out or left is no longer a candidate, and the students table is untouched
            var again = await P.Svc(db).PreviewAsync(w.School, P.Req(w, cls: w.C2), default);
            Assert.Equal(PromotionRowStatus.AlreadyRecorded, again.Data!.Rows.Single(x => x.StudentId == w.School + "Q1").Status);
            Assert.Equal(0, Count(db, $"select count(*) from tb_students where school_id='{w.School}' and student_id in ('{w.School}Q1','{w.School}Q2') and status_id <> {Active}"));
        }

        [DbFact]
        public async Task A_full_section_leaves_the_extra_student_out_with_a_reason_and_the_rest_is_still_done()
        {
            using var db = new TaxDb();
            var w = P.Populate(db, "ZQH");
            var edit = new PromotionDecisionInput { StudentId = w.School + "P5", ToClassId = w.C2, ToSectionId = w.C2A };       // Class 2 A has 2 places: P1 and the blocked P4 hold them
            var r = await Run(db, Exe(w, edits: new[] { edit }), w.School);
            var p5 = r.Items.Single(i => i.StudentId == w.School + "P5");
            Assert.Equal(PromotionItemOutcome.Skipped, p5.Outcome); Assert.Contains("full", p5.Reason);
            Assert.Equal(1, NewEnrol(db, w, "P1"));
            Assert.True(Count(db, $"select count(*) from tb_student_enrollments where academic_year_id={w.T} and class_section_id={w.C2A}") <= 2);
        }

        [DbFact]
        public async Task A_closed_or_non_later_or_inactive_target_year_is_refused_and_nothing_is_written()
        {
            using var db = new TaxDb();
            var w = P.Populate(db, "ZQI");
            var fp = P.Fp(db, w.School);
            var backwards = Exe(w); (backwards.SourceYearId, backwards.TargetYearId) = (w.T, w.S);
            Assert.Equal(400, (await P.Svc(db).ExecuteAsync(w.School, TaxDb.UserA, backwards, default)).StatusCode);
            var same = Exe(w); same.TargetYearId = w.S;
            Assert.Equal(400, (await P.Svc(db).ExecuteAsync(w.School, TaxDb.UserA, same, default)).StatusCode);
            db.Exec($"update tb_academic_years set closed_at=now(), closed_by={TaxDb.UserA} where academic_year_id={w.T}");
            var closed = await P.Svc(db).ExecuteAsync(w.School, TaxDb.UserA, Exe(w), default);
            Assert.Equal(400, closed.StatusCode); Assert.Contains("closed", closed.Message);
            Assert.Equal(fp, P.Fp(db, w.School));
        }

        [DbFact]
        public async Task A_closed_source_year_is_allowed_and_only_the_outcome_of_its_enrolments_changes()
        {
            using var db = new TaxDb();
            var w = P.Populate(db, "ZQJ");
            db.Exec($"update tb_academic_years set is_current=false, closed_at=now(), closed_by={TaxDb.UserA} where academic_year_id={w.S}");
            var old = OldColumns(db, w);
            var r = await Run(db, Exe(w), w.School);
            Assert.True(r.Promoted > 0);
            Assert.Equal(old, OldColumns(db, w));                                      // every column of the closed year's enrolments except the outcome is exactly as it was
            Assert.Equal("PROMOTED", Outcome(db, w, "P1", w.S));
            Assert.Equal(1, NewEnrol(db, w, "P1"));
        }

        [Fact]
        public void Nothing_else_in_the_code_can_change_an_enrolment_so_a_closed_years_enrolments_only_ever_get_the_promotion_outcome()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "smss-api-db-layer"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var writers = new List<string>();
            foreach (var project in new[] { "smss-api-db-layer", "smss-api-service-layer", "smss-api" })
                foreach (var f in Directory.EnumerateFiles(Path.Combine(dir!.FullName, project), "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
                {
                    var text = File.ReadAllText(f);
                    bool changes = System.Text.RegularExpressions.Regex.IsMatch(text, @"StudentEnrollments\s*\.\s*(Add|AddAsync|Update|Remove|ExecuteUpdate|ExecuteDelete)")
                                   || System.Text.RegularExpressions.Regex.IsMatch(text, @"(UPDATE|DELETE\s+FROM|INSERT\s+INTO)\s+(public\.)?tb_student_enrollments", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (changes) writers.Add(Path.GetFileName(f));
                }
            // admission creates the FIRST enrolment of a new student (the services refuse a closed year); promotion adds the next year's and records the outcome. Nothing updates or deletes an enrolment.
            Assert.Equal(new[] { "AdmissionRepository.cs", "PromotionRepository.cs" }, writers.Distinct().OrderBy(x => x).ToArray());
            var promotion = File.ReadAllText(Directory.EnumerateFiles(dir!.FullName, "PromotionRepository.cs", SearchOption.AllDirectories).First());
            Assert.Contains("SET outcome = @o, outcome_at = CURRENT_TIMESTAMP WHERE enrollment_id = @id AND school_id = @school AND outcome IS NULL", promotion);
            Assert.DoesNotContain("DELETE FROM public.tb_student_enrollments", promotion);
            var admission = File.ReadAllText(Directory.EnumerateFiles(dir.FullName, "AdmissionRepository.cs", SearchOption.AllDirectories).First());
            Assert.DoesNotMatch(@"StudentEnrollments\s*\.\s*(Update|Remove)", admission);
        }

        // ------------------------------------------------------------------------------------------------ fees
        [DbFact]
        public async Task The_new_years_fees_are_assigned_after_the_commit_and_a_class_with_no_structure_gets_none()
        {
            using var db = new TaxDb();
            var w = P.Populate(db, "ZQK");
            FeeLines(db, w);
            var r = await Run(db, Exe(w), w.School);
            Assert.Equal(4, r.FeesAssigned);                 // P1 P5 P6 (Class 2) and P2 (Class 1 again)
            Assert.Equal(1, r.FeesNoStructure);              // P10 went to Class 4, which has no structure (acknowledged)
            Assert.Empty(r.FeeFailures);
            Assert.Equal(1, Count(db, $"select count(*) from tb_student_fee_structure where student_id='{w.School}P1' and academic_year_id={w.T}"));
            Assert.True(Count(db, $"select count(*) from tb_student_fee_items where student_id='{w.School}P1' and academic_year_id={w.T}") > 0);
            Assert.Equal(0, Count(db, $"select count(*) from tb_student_fee_structure where student_id='{w.School}P10' and academic_year_id={w.T}"));
        }

        [DbFact]
        public async Task A_fee_assignment_failure_is_listed_without_detail_and_never_undoes_the_promotion()
        {
            using var db = new TaxDb();
            var w = P.Populate(db, "ZQL");
            FeeLines(db, w);
            db.Exec($@"create function zq_boom() returns trigger language plpgsql as $$ begin if new.student_id = '{w.School}P1' then raise exception 'boom'; end if; return new; end $$;
                       create trigger zq_boom before insert on tb_student_fee_items for each row execute function zq_boom();");
            var r = await Run(db, Exe(w), w.School);
            var f = Assert.Single(r.FeeFailures);
            Assert.Equal(w.School + "P1", f.StudentId);
            Assert.DoesNotContain("boom", f.Reason);
            Assert.Equal(3, r.FeesAssigned);
            Assert.Equal(1, NewEnrol(db, w, "P1")); Assert.Equal("PROMOTED", Outcome(db, w, "P1", w.S));      // the promotion stands
            Assert.NotNull(r.BatchId);
        }

        /// <summary>The target year already started and is the current one: 2025-26 (closed-to-be source) and 2026-27 (target, started, current).</summary>
        private static void TargetStarted(TaxDb db, P.W w, bool targetCurrent = true)
        {
            db.Exec($"update tb_academic_years set is_current=false where academic_year_id={w.S}");
            db.Exec($"update tb_academic_years set start_date='2025-04-01', end_date='2026-03-31' where academic_year_id={w.S}");
            db.Exec($"update tb_academic_years set start_date='2026-04-01', end_date='2027-03-31', is_current={(targetCurrent ? "true" : "false")} where academic_year_id={w.T}");
            if (!targetCurrent) db.Exec($"update tb_academic_years set is_current=true where academic_year_id={w.S}");
        }

        [DbFact]
        public async Task When_the_target_year_has_started_the_run_needs_the_back_dating_confirmation_and_a_reason_and_then_logs_it()
        {
            using var db = new TaxDb();
            var w = P.Populate(db, "ZQM");
            FeeLines(db, w);
            TargetStarted(db, w);
            var date = new DateOnly(2026, 9, 1);
            var fp = P.Fp(db, w.School);
            Assert.Equal(400, (await P.Svc(db).ExecuteAsync(w.School, TaxDb.UserA, Exe(w, date: date, backdate: false, reason: "Moving the class up late"), default)).StatusCode);   // no confirmation
            Assert.Equal(400, (await P.Svc(db).ExecuteAsync(w.School, TaxDb.UserA, Exe(w, date: date, backdate: true), default)).StatusCode);                                      // no reason
            Assert.Equal(fp, P.Fp(db, w.School));
            Assert.Equal(0, Count(db, $"select count(*) from tb_fee_backdating_log where school_id='{w.School}'"));
            var r = await Run(db, Exe(w, date: date, backdate: true, reason: "Moving the class up late"), w.School);
            Assert.True(r.Backdated);
            Assert.Equal(5, r.FeesAssigned);                 // P4 is not blocked on 1 Sep: its first due falls after that date
            Assert.Equal(5, Count(db, $"select count(*) from tb_fee_backdating_log where school_id='{w.School}' and kind='STUDENT' and academic_year_id={w.T} and reason='Moving the class up late' and created_by={TaxDb.UserA}"));
            Assert.Equal(1, Count(db, $"select count(*) from tb_promotion_batches where school_id='{w.School}' and override_reason='Moving the class up late'"));
            Assert.True(DateOnly.Parse(db.Scalar<string>($"select min(d.due_date)::text from tb_student_fee_dues d join tb_student_fee_items i using(student_fee_item_id) where i.student_id='{w.School}P1' and i.academic_year_id={w.T}")) < smss_api_service_layer.helper.SchoolClock.Today());
        }

        [DbFact]
        public async Task A_target_year_that_has_started_but_is_not_the_current_one_is_refused()
        {
            using var db = new TaxDb();
            var w = P.Populate(db, "ZQN");
            TargetStarted(db, w, targetCurrent: false);
            var fp = P.Fp(db, w.School);
            var x = await P.Svc(db).ExecuteAsync(w.School, TaxDb.UserA, Exe(w, date: new DateOnly(2026, 9, 1), backdate: true, reason: "Moving the class up late"), default);
            Assert.Equal(400, x.StatusCode); Assert.Contains("current academic year", x.Message);
            Assert.Equal(fp, P.Fp(db, w.School));
        }

        // ------------------------------------------------------------------------------------------------ schools and roles
        [DbFact]
        public async Task Another_schools_year_is_a_404_and_the_other_school_is_never_touched()
        {
            using var db = new TaxDb();
            var a = P.Populate(db, "ZQO"); var b = P.Populate(db, "ZQP");
            var fp = P.Fp(db, b.School);
            var bad = Exe(a); bad.TargetYearId = b.T;
            Assert.Equal(404, (await P.Svc(db).ExecuteAsync(a.School, TaxDb.UserA, bad, default)).StatusCode);
            Assert.NotNull((await Run(db, Exe(a), a.School)).BatchId);
            Assert.Equal(fp, P.Fp(db, b.School));
            Assert.Equal(0, Count(db, $"select count(*) from tb_promotion_batches where school_id='{b.School}'"));
        }

        public static IEnumerable<object[]> RoleCases() => Fake.AllRoles.Concat(new[] { "Driver" }).Select(r => new object[] { r });

        [Theory, MemberData(nameof(RoleCases))]
        public async Task Only_the_School_Admin_and_the_Principal_may_run_a_promotion_or_see_the_pending_list(string role)
        {
            var svc = Fake.Of<IPromotionService>();
            var c = Fake.WithUser(new PromotionController(svc), Fake.User("S", 5, role));
            var run = Fake.Code(await c.Execute(new PromotionExecuteRequest { SourceYearId = 1, TargetYearId = 2, ClassId = 3, Confirm = true }, default));
            var pending = Fake.Code(await c.Pending(false, default));
            var allowed = role is "School Admin" or "Principal";
            Assert.Equal(!allowed, run == 403); Assert.Equal(!allowed, pending == 403);
            Assert.Equal(allowed, Fake.ProxyOf(svc).Calls.Contains("ExecuteAsync"));
            Assert.Equal(allowed, Fake.ProxyOf(svc).Calls.Contains("GetPendingAsync"));
        }

        // ------------------------------------------------------------------------------------------------ pending promotion
        [DbFact]
        public async Task The_pending_list_holds_students_of_the_year_before_the_current_one_who_have_no_enrolment_in_it_and_has_not_passed_out_or_left()
        {
            using var db = new TaxDb();
            var w = P.Populate(db, "ZQQ");
            var none = (await P.Svc(db).GetPendingAsync(w.School, true, default)).Data!;       // the current year is the oldest: no earlier year, the tile is hidden
            Assert.False(none.HasEarlierYear); Assert.Equal(0, none.Total); Assert.Empty(none.Rows);
            db.Exec($"update tb_academic_years set is_current=false where academic_year_id={w.S}");
            db.Exec($"update tb_academic_years set is_current=true where academic_year_id={w.T}");
            var p = (await P.Svc(db).GetPendingAsync(w.School, true, default)).Data!;
            Assert.True(p.HasEarlierYear); Assert.Equal(("2026-27", "2027-28"), (p.EarlierYearName, p.CurrentYearName));
            Assert.Equal(10, p.Total);                                                          // P1-P6, P10 and Q1-Q3 (P7 is enrolled, P8 passed out, P9 inactive)
            Assert.Equal(10, p.Rows.Count);
            Assert.DoesNotContain(p.Rows, r => r.StudentId is var s && (s == w.School + "P7" || s == w.School + "P8" || s == w.School + "P9"));
            var p4 = p.Rows.Single(r => r.StudentId == w.School + "P4");
            Assert.Equal(("BLOCKED", 500m), (p4.Status, p4.Outstanding));                       // the 2027-02-01 due is not yet due today
            Assert.Equal("NO_DECISION", p.Rows.Single(r => r.StudentId == w.School + "P3").Status);
            Assert.Equal(1, p.Blocked);
            Assert.Equal(10, (await P.Svc(db).GetPendingAsync(w.School, false, default)).Data!.Total);
            Assert.Empty((await P.Svc(db).GetPendingAsync(w.School, false, default)).Data!.Rows);
            // a student who is promoted or who leaves drops off the list
            db.Exec($"update tb_academic_years set is_current=false where academic_year_id={w.T}");
            db.Exec($"update tb_academic_years set is_current=true where academic_year_id={w.S}");
            await Run(db, Exe(w, edits: new[] { D(w, "P3", "LEAVE") }), w.School);
            db.Exec($"update tb_academic_years set is_current=false where academic_year_id={w.S}");
            db.Exec($"update tb_academic_years set is_current=true where academic_year_id={w.T}");
            var after = (await P.Svc(db).GetPendingAsync(w.School, true, default)).Data!;
            Assert.DoesNotContain(after.Rows, r => r.StudentId == w.School + "P3");             // left
            Assert.DoesNotContain(after.Rows, r => r.StudentId == w.School + "P1");             // promoted
            Assert.Contains(after.Rows, r => r.StudentId == w.School + "P4");                   // still blocked
        }

        [DbFact]
        public async Task The_pending_list_is_per_school_and_a_school_with_one_year_has_no_tile()
        {
            using var db = new TaxDb();
            var a = P.Populate(db, "ZQR"); var b = P.Build(db, "ZQS");
            Assert.False((await P.Svc(db).GetPendingAsync(b.School, false, default)).Data!.HasEarlierYear);
            Assert.Equal(400, (await P.Svc(db).GetPendingAsync(null, false, default)).StatusCode);
            db.Exec($"update tb_academic_years set is_current=false where academic_year_id={a.S}; update tb_academic_years set is_current=true where academic_year_id={a.T}");
            Assert.Equal(0, (await P.Svc(db).GetPendingAsync(b.School, true, default)).Data!.Total);
        }
    }
}
