using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using smss_api.Controllers;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using Xunit;
using L = smss_api_tests.LateFee5aTests;
using P = smss_api_tests.PromotionPreview4b2Tests;

namespace smss_api_tests
{
    /// <summary>
    /// Stage 5b: the late fee is RECORDED at payment. The server recomputes it inside the payment transaction after the per-school receipt lock; the client sends only applyLateFee (and a note when waiving), never an amount.
    /// Totals, tax and concession, the per-due rows, the receipt line, void, concurrency, the readers that must not move, and old receipts byte for byte. Throwaway rows on the scratch database only.
    /// Needs Docs/smss-st-5a1-RUN-ME.sql applied to the template. [DbFact]: needs SMSS_TEST_PG and SMSS_TEST_TEMPLATE.
    /// </summary>
    public class LateFee5bTests
    {
        private static readonly DateOnly DueDate = new(2026, 9, 1);
        private static int DaysLate => SchoolClock.Today().DayNumber - DueDate.DayNumber;

        internal static RecordPaymentRequest Pay(long due, decimal amount, bool apply = true, string? note = null, long? tax = null, long? concession = null, string? reason = null) => new()
        {
            Amount = amount, PaymentMode = "Cash", Allocations = new() { new() { FeeDueId = due, Amount = amount } }, ApplyLateFee = apply, LateFeeNote = note, TaxTypeId = tax, ConcessionId = concession, ConcessionReason = reason,
        };

        internal static async Task<FeeReceiptDto> Rec(TaxDb db, string school, string student, RecordPaymentRequest r)
        {
            var x = await db.Payments().RecordPaymentAsync(school, student, TaxDb.UserA, r, default);
            Assert.True(x.Status, x.Message);
            return x.Data!;
        }

        /// <summary>A school with a student owing Tuition 1000 (due 1 Sep, flagged for the late fee) and Bus 500 (not flagged).</summary>
        internal static async Task<(P.W W, string Student, long Tui, long Bus)> World(TaxDb db, string id, string type = "FLAT", decimal amount = 50, decimal? percent = null, int grace = 5, decimal? cap = null, bool tax = false)
        {
            var (w, s, tui, bus) = L.Student(db, id);
            var saved = await L.Svc(db).SaveRuleAsync(w.School, TaxDb.UserA, L.Save(type: type, amount: type == "PERCENT" ? null : amount, percent: percent, grace: grace, cap: cap, tax: tax, types: new[] { L.Type(db, w.School, "TUI") }), default);
            Assert.True(saved.Status, saved.Message);
            return (w, s, tui, bus);
        }

        private static string Money(TaxDb db, string sql) => db.Scalar<string>(sql);
        private static string Fp(TaxDb db, string school) => db.Scalar<string>($@"select md5(coalesce(string_agg(x, '|' order by x), '')) from (
            select p::text x from tb_fee_payments p where school_id='{school}' union all select l::text from tb_fee_payment_late_fees l where school_id='{school}'
            union all select a::text from tb_fee_payment_allocations a join tb_fee_payments p using(payment_id) where p.school_id='{school}' union all select d::text from tb_student_fee_dues d where school_id='{school}') q");

        // ------------------------------------------------------------------------------------------------ recording
        [DbFact]
        public async Task The_server_charges_the_late_fee_it_worked_out_stores_it_with_the_rule_and_leaves_the_allocations_and_dues_alone()
        {
            using var db = new TaxDb();
            var (w, s, tui, _) = await World(db, "ZBA");
            var r = await Rec(db, w.School, s, Pay(tui, 1000));
            Assert.Equal((1000m, 1050m, 1050m), (r.FeeAmount, r.Amount, r.TotalAmount));
            var lf = r.LateFee!;
            Assert.Equal(("APPLIED", 50m, 0m), (lf.Decision, lf.Amount, lf.TaxAmount));
            Assert.Contains("₹50 for each late installment", lf.RuleText); Assert.Contains("5 grace days", lf.RuleText);
            var line = Assert.Single(lf.Lines);
            Assert.Equal(("Tuition", DaysLate, DaysLate - 5, 1000m, 50m), (line.FeeType, line.DaysLate, line.DaysCharged, line.BaseAmount, line.Amount));
            // the payment row, the per-due row (with the rule snapshot) and the unchanged allocations and due
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_fee_payments where payment_id={r.PaymentId} and amount=1050 and fee_amount=1000 and late_fee_amount=50 and late_fee_tax_amount=0 and tax_amount=0 and late_fee_decision='APPLIED' and late_fee_decided_by={TaxDb.UserA} and late_fee_rule_snapshot is not null"));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_fee_payment_late_fees where payment_id={r.PaymentId} and fee_due_id={tui} and amount=50 and base_amount=1000 and computed_total=50 and already_collected=0 and rule_type='FLAT' and rule_amount=50 and grace_days=5 and days_late={DaysLate} and days_charged={DaysLate - 5}"));
            Assert.Equal(1000m, db.Scalar<decimal>($"select sum(allocated_amount) from tb_fee_payment_allocations where payment_id={r.PaymentId}"));   // a late fee is not allocated against a due
            Assert.Equal((1000m, 1000m), (db.Scalar<decimal>($"select amount_due from tb_student_fee_dues where fee_due_id={tui}"), db.Scalar<decimal>($"select amount_paid from tb_student_fee_dues where fee_due_id={tui}")));
            // the history and the receipt agree
            var h = (await db.Payments().GetPaymentsAsync(w.School, s, default)).Data!.Single();
            Assert.Equal((1050m, 1000m, 50m), (h.Amount, h.FeeAmount, h.LateFee!.Amount));
        }

        [DbFact]
        public async Task Tax_applies_to_the_late_fee_only_when_the_school_setting_says_so_and_the_setting_is_snapshotted()
        {
            using var db = new TaxDb();
            var (a, sa, tuiA, _) = await World(db, "ZBB", tax: false);
            var (b, sb, tuiB, _) = await World(db, "ZBC", tax: true);
            long Tax(string school) => db.Scalar<long>($"insert into tb_tax_types (school_id, tax_name, tax_code, rate_percent, is_active) values ('{school}', 'GST', 'GST', 18, true) returning tax_type_id");
            var off = await Rec(db, a.School, sa, Pay(tuiA, 1000, tax: Tax(a.School)));
            Assert.Equal((180m, 0m, 1230m), (off.TaxAmount, off.LateFee!.TaxAmount, off.Amount));          // 18% of the fee only
            var on = await Rec(db, b.School, sb, Pay(tuiB, 1000, tax: Tax(b.School)));
            Assert.Equal((189m, 9m, 1239m), (on.TaxAmount, on.LateFee!.TaxAmount, on.Amount));              // 18% of 1000 + 18% of 50
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_fee_payments where payment_id={on.PaymentId} and late_fee_tax_amount=9 and tax_amount=189 and amount = fee_amount + late_fee_amount + tax_amount and (late_fee_rule_snapshot->>'taxApplies')='true'"));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_fee_payments where payment_id={off.PaymentId} and late_fee_tax_amount=0 and (late_fee_rule_snapshot->>'taxApplies')='false'"));
            var noTaxType = await Rec(db, b.School, sb, Pay(db.Scalar<long>($"select min(fee_due_id) from tb_student_fee_dues where student_id='{sb}' and amount_due > amount_paid"), 500));                    // the setting says tax applies but no tax type was chosen: no tax
            Assert.Equal(0m, noTaxType.TaxAmount);
        }

        [DbFact]
        public async Task A_concession_never_discounts_the_late_fee_and_a_percent_rule_uses_the_amount_applied_before_the_discount()
        {
            using var db = new TaxDb();
            var (w, s, tui, _) = await World(db, "ZBD", type: "PERCENT", percent: 10, grace: 0);
            var c = db.Scalar<long>($"insert into tb_fee_concessions (school_id, concession_name, concession_code, concession_type, value) values ('{w.School}', 'Sibling', 'SIB', 'PERCENTAGE', 10) returning concession_id");
            var r = await Rec(db, w.School, s, Pay(tui, 1000, concession: c, reason: "sibling"));
            Assert.Equal((100m, 900m, 100m, 1000m), (r.ConcessionAmount, r.FeeAmount, r.LateFee!.Amount, r.Amount));   // the fee is 900 after the discount; the late fee is 10% of the 1000 applied, undiscounted
        }

        [DbFact]
        public async Task Waiving_needs_a_note_and_records_who_decided_what_would_have_been_charged_and_charges_nothing()
        {
            using var db = new TaxDb();
            var (w, s, tui, _) = await World(db, "ZBE");
            var fp = Fp(db, w.School);
            var none = await db.Payments().RecordPaymentAsync(w.School, s, TaxDb.UserA, Pay(tui, 1000, apply: false), default);
            Assert.Equal(400, none.StatusCode); Assert.Contains("would be charged", none.Message);
            Assert.Equal(400, (await db.Payments().RecordPaymentAsync(w.School, s, TaxDb.UserA, Pay(tui, 1000, apply: false, note: "no"), default)).StatusCode);        // under 5 characters
            Assert.Equal(400, (await db.Payments().RecordPaymentAsync(w.School, s, TaxDb.UserA, Pay(tui, 1000, apply: false, note: "      "), default)).StatusCode);
            Assert.Equal(fp, Fp(db, w.School));                                                                                                                        // a refusal writes nothing
            var r = await Rec(db, w.School, s, Pay(tui, 1000, apply: false, note: "  Fee waived for a poor family  "));
            Assert.Equal((1000m, 1000m), (r.Amount, r.FeeAmount));
            var lf = r.LateFee!;
            Assert.Equal(("WAIVED", 0m, "Fee waived for a poor family", 50m), (lf.Decision, lf.Amount, lf.Note, lf.WaivedAmount));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_fee_payments where payment_id={r.PaymentId} and late_fee_decision='WAIVED' and late_fee_amount=0 and late_fee_decided_by={TaxDb.UserA} and late_fee_note='Fee waived for a poor family'"));
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_fee_payment_late_fees where payment_id={r.PaymentId}"));   // nothing charged: no per-due row
            // the student never sees a waiver, its note or who decided
            var entity = await db.Ctx().FeePayments.AsNoTracking().SingleAsync(p => p.PaymentId == r.PaymentId);
            Assert.Null(PaymentLateFee.Build(entity, new List<smss_api_db_layer.@interface.PaymentLateFeeLineRow>(), "Admin", staffView: false));
            Assert.NotNull(PaymentLateFee.Build(entity, new List<smss_api_db_layer.@interface.PaymentLateFeeLineRow>(), "Admin", staffView: true));
        }

        [DbFact]
        public async Task With_the_rule_off_or_nothing_late_there_is_no_late_fee_and_no_extra_requirement_even_when_not_applying()
        {
            using var db = new TaxDb();
            var (off, so, tuiO, _) = L.Student(db, "ZBF");                                                    // no rule was ever saved
            var a = await Rec(db, off.School, so, Pay(tuiO, 1000, apply: false));                            // no note: nothing would have been charged
            Assert.Null(a.LateFee); Assert.Equal(1000m, a.Amount);
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_fee_payments where payment_id={a.PaymentId} and late_fee_decision is null and late_fee_note is null and late_fee_decided_by is null and late_fee_rule_snapshot is null"));
            var (inGrace, si, tuiI, _) = await World(db, "ZBG", grace: 365);                                  // on, but nothing is late beyond a year of grace
            var b = await Rec(db, inGrace.School, si, Pay(tuiI, 1000, apply: false));
            Assert.Null(b.LateFee);
            var (other, sx, tuiX, busX) = await World(db, "ZBH");                                             // Bus is late but its fee type is not flagged
            var c = await Rec(db, other.School, sx, Pay(busX, 500, apply: false));
            Assert.Null(c.LateFee); Assert.Equal(500m, c.Amount);
        }

        [DbFact]
        public async Task A_late_fee_amount_sent_by_the_client_is_never_read()
        {
            using var db = new TaxDb();
            var (w, s, tui, _) = await World(db, "ZBI");
            Assert.DoesNotContain(typeof(RecordPaymentRequest).GetProperties(), p => p.Name.Contains("LateFeeAmount", StringComparison.OrdinalIgnoreCase) || p.Name == "LateFee");
            var json = $"{{\"amount\":1000,\"paymentMode\":\"Cash\",\"allocations\":[{{\"feeDueId\":{tui},\"amount\":1000}}],\"lateFeeAmount\":9999,\"lateFee\":{{\"amount\":1}},\"lateFeeTaxAmount\":5,\"applyLateFee\":true}}";
            var req = JsonSerializer.Deserialize<RecordPaymentRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var r = await Rec(db, w.School, s, req);
            Assert.Equal((50m, 1050m), (r.LateFee!.Amount, r.Amount));                                       // the server's figure
        }

        [DbFact]
        public async Task A_due_paid_in_two_partial_payments_is_charged_once_a_voided_payment_frees_it_and_a_voided_receipt_still_prints_its_line()
        {
            using var db = new TaxDb();
            var (w, s, tui, _) = await World(db, "ZBJ");
            var p1 = await Rec(db, w.School, s, Pay(tui, 400));
            Assert.Equal(50m, p1.LateFee!.Amount);
            var p2 = await Rec(db, w.School, s, Pay(tui, 600));                                              // the second part of the same due: already collected
            Assert.Null(p2.LateFee); Assert.Equal(600m, p2.Amount);
            var voided = await db.Payments().VoidPaymentAsync(w.School, p1.PaymentId, TaxDb.UserA, new VoidPaymentRequest { VoidReason = "wrong amount" }, default);
            Assert.True(voided.Status, voided.Message);
            Assert.Equal(50m, voided.Data!.LateFee!.Amount);                                                  // the voided receipt keeps its line
            Assert.Equal(600m, db.Scalar<decimal>($"select amount_paid from tb_student_fee_dues where fee_due_id={tui}"));
            var p3 = await Rec(db, w.School, s, Pay(tui, 400));                                              // pay again: the voided payment collected nothing, so it is charged
            Assert.Equal(50m, p3.LateFee!.Amount);
            Assert.Equal(50m, db.Scalar<decimal>($"select coalesce(sum(l.amount),0) from tb_fee_payment_late_fees l join tb_fee_payments p using(payment_id) where p.status='Active' and l.fee_due_id={tui}"));
            Assert.Equal(1000m, db.Scalar<decimal>($"select amount_paid from tb_student_fee_dues where fee_due_id={tui}"));
            var old = (await db.Payments().GetReceiptAsync(w.School, p1.PaymentId, default)).Data!;
            Assert.Equal(("Voided", "APPLIED"), (old.Status, old.LateFee!.Decision));
        }

        [DbFact]
        public async Task A_cap_per_due_counts_what_earlier_non_voided_payments_collected()
        {
            using var db = new TaxDb();
            var (w, s, tui, _) = await World(db, "ZBK", type: "PER_DAY", amount: 5, grace: 0, cap: 130);   // far more than 130 would be owed: the cap binds
            var p1 = await Rec(db, w.School, s, Pay(tui, 300));
            Assert.Equal(130m, p1.LateFee!.Amount);
            Assert.Null((await Rec(db, w.School, s, Pay(tui, 300))).LateFee);                                // cap reached
            await db.Payments().VoidPaymentAsync(w.School, p1.PaymentId, TaxDb.UserA, new VoidPaymentRequest { VoidReason = "mistake" }, default);
            Assert.Equal(130m, (await Rec(db, w.School, s, Pay(tui, 300))).LateFee!.Amount);                 // freed again
        }

        [DbFact]
        public async Task Two_simultaneous_payments_on_the_same_late_due_charge_the_late_fee_once_and_receipt_numbers_stay_unique()
        {
            using var db = new TaxDb();
            var (w, s, tui, _) = await World(db, "ZBL");
            var both = await Task.WhenAll(Task.Run(() => db.Payments().RecordPaymentAsync(w.School, s, TaxDb.UserA, Pay(tui, 500), default)), Task.Run(() => db.Payments().RecordPaymentAsync(w.School, s, TaxDb.UserA, Pay(tui, 500), default)));
            Assert.All(both, x => Assert.True(x.Status, x.Message));
            Assert.Equal(50m, db.Scalar<decimal>($"select coalesce(sum(late_fee_amount),0) from tb_fee_payments where school_id='{w.School}' and status='Active'"));   // once in total
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_fee_payment_late_fees where fee_due_id={tui}"));
            Assert.Equal(1, both.Count(x => x.Data!.LateFee != null));
            Assert.Equal(2, db.Scalar<int>($"select count(distinct receipt_number) from tb_fee_payments where school_id='{w.School}'"));
            Assert.Equal(1000m, db.Scalar<decimal>($"select amount_paid from tb_student_fee_dues where fee_due_id={tui}"));
        }

        [DbFact]
        public async Task A_rule_changed_after_the_payment_never_alters_its_receipt()
        {
            using var db = new TaxDb();
            var (w, s, tui, _) = await World(db, "ZBM");
            var r = await Rec(db, w.School, s, Pay(tui, 1000));
            string Json() => JsonSerializer.Serialize(db.Payments().GetReceiptAsync(w.School, r.PaymentId, default).Result);
            var before = Json();
            await L.Svc(db).SaveRuleAsync(w.School, TaxDb.UserA, L.Save(type: "PER_DAY", amount: 9, grace: 0, cap: 1, tax: true, types: Array.Empty<long>()), default);
            Assert.Equal(before, Json());
            Assert.Contains("₹50 for each late installment", (await db.Payments().GetReceiptAsync(w.School, r.PaymentId, default)).Data!.LateFee!.RuleText);
        }

        // ------------------------------------------------------------------------------------------------ the readers
        [DbFact]
        public async Task Fee_collected_figures_keep_meaning_fee_only_so_no_existing_figure_moves_when_a_payment_carries_a_late_fee()
        {
            using var db = new TaxDb();
            var (w, s, tui, _) = await World(db, "ZBN", tax: false);
            var tax = db.Scalar<long>($"insert into tb_tax_types (school_id, tax_name, tax_code, rate_percent, is_active) values ('{w.School}', 'GST', 'GST', 18, true) returning tax_type_id");
            var r = await Rec(db, w.School, s, Pay(tui, 1000, tax: tax));                                    // fee 1000, late fee 50, tax 180 (setting off), total 1230
            var from = DateTime.UtcNow.AddDays(-1); var to = DateTime.UtcNow.AddDays(1);
            Assert.Equal(1000m, await new AccountantDashboardRepository(db.Ctx()).GetCollectedBetweenAsync(w.School, from, to, default));              // Accountant: cash FEE collected
            Assert.Equal(1000m, (await new FeeReportRepository(db.Ctx()).GetCollectionByMonthAsync(w.School, w.S, null, null, default)).Sum(m => m.Amount));   // collection by month: allocations
            Assert.Equal(1000m, await new SchoolAdminDashboardRepository(db.Ctx()).CollectedAsync(w.School, w.S, null, null, default));                // School Admin dashboard
            Assert.Equal(180m, (await new FeeReportRepository(db.Ctx()).GetTaxCollectedAsync(w.School, w.S, null, null, default)).Sum(t => t.Amount));     // tax report: the tax on the fee, as before
            var recent = Assert.Single(await new AccountantDashboardRepository(db.Ctx()).GetRecentPaymentsAsync(w.School, 5, default));
            Assert.Equal(1230m, recent.Amount);                                                  // the recent list shows what the payer handed over: fee + late fee + tax
            var h = (await db.Payments().GetPaymentsAsync(w.School, s, default)).Data!.Single();
            Assert.Equal((1230m, 1000m, 180m), (h.Amount, h.FeeAmount, h.TaxAmount));
        }

        [DbFact]
        public async Task The_tax_on_a_late_fee_is_part_of_the_tax_report_only_when_the_school_setting_charges_it()
        {
            using var db = new TaxDb();
            var (w, s, tui, _) = await World(db, "ZBO", tax: true);
            var tax = db.Scalar<long>($"insert into tb_tax_types (school_id, tax_name, tax_code, rate_percent, is_active) values ('{w.School}', 'GST', 'GST', 18, true) returning tax_type_id");
            await Rec(db, w.School, s, Pay(tui, 1000, tax: tax));
            Assert.Equal(189m, (await new FeeReportRepository(db.Ctx()).GetTaxCollectedAsync(w.School, w.S, null, null, default)).Sum(t => t.Amount));    // 180 on the fee + 9 on the late fee
            Assert.Equal(1000m, await new SchoolAdminDashboardRepository(db.Ctx()).CollectedAsync(w.School, w.S, null, null, default));                    // and the fee collected still does not move
        }

        // ------------------------------------------------------------------------------------------------ old receipts, schools, roles
        private static readonly (string School, string Key, string Hash)[] GoldenReceipts =
        {
            ("APS261013001", "RCP-2026-0001", "b70f0fbbe686080c32f15b2d6bdf29d6"),
            ("APS261013001", "RCP-2026-0002", "a58f273bbdb1fdbee04ac9fc0962e8c0"),
            ("APS261013001", "RCP-2026-0003", "4b6a42e221e1bc01f774cb8ad8f3d25b"),
            ("APS261013001", "RCP-2026-0004", "7fee6e3d3e9743cf11a3549bf74f37f2"),
            ("LAS261103001", "RCP-2026-0001", "48901c6d40076c870a15f117fb387984"),
            ("LAS261103001", "RCP-2026-0002", "5c3c2b70638251f225408d236ec66f86"),
            ("LAS261103001", "RCP-2026-0003", "4cf890ba2b9a87fb6ab7dcab0bce1f69"),
            ("LAS261103001", "RCP-2026-0004", "ea72ecf5acca1d2d334af00821dd0936"),
            ("LAS261103001", "RCP-2026-0005", "d2309921721c8e76ca0063af73cf4934"),
            ("LAS261103001", "RCP-2026-0006", "3c29ce3e3b3ed88742513c8fa3c46131"),
            ("LAS261103001", "RCP-2026-0007", "c4caabb055e556073451e1ecb3ec220c"),
            ("LAS261103001", "RCP-2026-0008", "0e42231d028344c7f05ba7530f1718d7"),
            ("LAS261103001", "RCP-2026-0009", "1e8ea8dc9e5c8c0b7cbf651d1a3678ae"),
            ("LAS261103001", "RCP-2026-0010", "acfe0920e7962593c67485f00d8c3b42"),
            ("LAS261103001", "RCP-2026-0011", "5b938895cf611c137293c207b2ec675f"),
            ("LAS261103001", "RCP-2026-0012", "e84be87a6607f6555101bafde96e7e09"),
        };

        private static readonly (string School, string Key, string Hash)[] GoldenHistories =
        {
            ("APS261013001", "APS001ST002", "2cc4b6bb0d41836a754247085987ff17"),
            ("APS261013001", "APS001ST001", "fd7b8e96bf24744502741724008c8354"),
            ("LAS261103001", "LAS001ST005", "03bafaea104a8071a770b21ac8d09547"),
            ("LAS261103001", "LAS001ST002", "7e236ccbaac33a6834ab1c4a4009ea58"),
            ("LAS261103001", "LAS001ST037", "f58cab1e08797e9bbc350c0b85b418eb"),
            ("LAS261103001", "LAS001ST001", "a63e221709998aabb831e0e35faaffd7"),
            ("LAS261103001", "LAS001ST003", "bcaf17d06d62fd121c1d05f6253fde63"),
            ("LAS261103001", "LAS001ST013", "004881827102fd2166dc8e58271a12bc"),
            ("LAS261103001", "LAS001ST014", "9b836ace965190c621235d5c2e81acac"),
            ("LAS261103001", "LAS001ST006", "d45129390101be2d05744d35fa848d4b"),
            ("LAS261103001", "LAS001ST008", "6c03cebe211acd4e938f485351e16702"),
            ("LAS261103001", "LAS001ST010", "84c9ab772b4a6d57c5b298edf9fbcab6"),
        };

        private static string H(object o) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o)))).ToLowerInvariant();

        [DbFact]
        public async Task The_receipts_and_histories_of_the_existing_payments_are_byte_for_byte_what_they_were_before_the_late_fee()
        {
            using var db = new TaxDb();
            var svc = db.Payments();
            var checkedReceipts = 0;
            foreach (var (school, key, hash) in GoldenReceipts)
            {
                var id = db.Scalar<long>($"select coalesce((select payment_id from tb_fee_payments where school_id='{school}' and receipt_number='{key}' and created_at < '2026-11-26'), 0)");
                if (id == 0) continue;                                                                       // a template built from another copy may not hold this receipt
                Assert.Equal(hash, H(await svc.GetReceiptAsync(school, id, default)));
                checkedReceipts++;
            }
            Assert.Equal(db.Scalar<int>("select count(*) from tb_fee_payments where created_at < '2026-11-26'"), checkedReceipts);   // every existing payment of the template was compared (16 on the real data)
            Assert.True(checkedReceipts >= 1, "none of the existing receipts was found in the template: this test proved nothing");
            foreach (var (school, student, hash) in GoldenHistories)
            {
                if (db.Scalar<int>($"select count(*) from tb_fee_payments where school_id='{school}' and student_id='{student}' and created_at < '2026-11-26'") == 0) continue;
                Assert.Equal(hash, H(await svc.GetPaymentsAsync(school, student, default)));
            }
        }

        [DbFact]
        public async Task A_student_of_another_school_is_a_404_and_one_schools_rule_never_charges_another_school()
        {
            using var db = new TaxDb();
            var (a, sa, tuiA, _) = await World(db, "ZBP");
            var (b, sb, tuiB, _) = L.Student(db, "ZBQ");                                                     // school B never set a rule
            var fp = Fp(db, b.School);
            var cross = await db.Payments().RecordPaymentAsync(a.School, sb, TaxDb.UserA, Pay(tuiB, 1000), default);
            Assert.Equal(404, cross.StatusCode);
            Assert.Equal(fp, Fp(db, b.School));
            Assert.Null((await Rec(db, b.School, sb, Pay(tuiB, 1000))).LateFee);                              // the same late dues, no rule: no late fee
            Assert.NotNull((await Rec(db, a.School, sa, Pay(tuiA, 1000))).LateFee);
        }

        public static IEnumerable<object[]> RoleCases() => Fake.AllRoles.Concat(new[] { "Driver" }).Select(r => new object[] { r });

        [Theory, MemberData(nameof(RoleCases))]
        public async Task Only_the_School_Admin_and_the_Accountant_record_a_payment_with_or_without_a_late_fee(string role)
        {
            var svc = Fake.Of<IFeePaymentService>();
            var c = Fake.WithUser(new FeePaymentController(svc), Fake.User("S", 5, role));
            var code = Fake.Code(await c.RecordPayment("ST1", Pay(1, 100, apply: false, note: "poor family"), default));
            var allowed = role is "School Admin" or "Accountant";
            Assert.Equal(!allowed, code == 403);
            Assert.Equal(allowed, Fake.ProxyOf(svc).Calls.Contains("RecordPaymentAsync"));
        }
    }
}
