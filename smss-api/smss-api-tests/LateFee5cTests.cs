using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using smss_api.Controllers;
using smss_api_db_layer.@interface;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;
using B = smss_api_tests.LateFee5bTests;
using L = smss_api_tests.LateFee5aTests;
using P = smss_api_tests.PromotionPreview4b2Tests;

namespace smss_api_tests
{
    /// <summary>
    /// Stage 5c: the late fee in reports and dashboards. Late fee collected and Total received (Accountant), Late fee collected this month (School Admin), the Late fee and Total received columns of the collection
    /// by month, and the Late fee report. Voided payments are everywhere excluded; the existing fee figures keep meaning fee only. Throwaway rows on the scratch database only.
    /// Needs Docs/smss-st-5a1-RUN-ME.sql applied to the template. [DbFact]: needs SMSS_TEST_PG and SMSS_TEST_TEMPLATE.
    /// </summary>
    public class LateFee5cTests
    {
        private static AccountantDashboardService Accountant(TaxDb db)
        {
            var c = db.Ctx();
            return new AccountantDashboardService(db.Reports(), new AccountantDashboardRepository(c), new SchoolDashboardRepository(c), NullLogger<AccountantDashboardService>.Instance, new LateFeeReportRepository(c));
        }

        private static SchoolAdminDashboardService SchoolAdmin(TaxDb db, ILateFeeReportRepository? late = null)
        {
            var c = db.Ctx();
            return new SchoolAdminDashboardService(new SchoolAdminDashboardRepository(c), new SchoolDashboardRepository(c), NullLogger<SchoolAdminDashboardService>.Instance, lateFees: late ?? new LateFeeReportRepository(c));
        }

        private static LateFeeReportService Report(TaxDb db) => new(new LateFeeReportRepository(db.Ctx()), NullLogger<LateFeeReportService>.Instance);

        private sealed class DownLateFeeReport : ILateFeeReportRepository
        {
            public Task<LateFeeReceivedRow> GetReceivedBetweenAsync(string schoolId, DateTime fromUtc, DateTime toExclusiveUtc, CancellationToken ct) => throw new InvalidOperationException("down");
            public Task<decimal> GetLateFeeCollectedAsync(string schoolId, long academicYearId, DateTime? fromUtc, DateTime? toExclusiveUtc, CancellationToken ct) => throw new InvalidOperationException("down");
            public Task<List<LateFeeMonthRow>> GetByMonthAsync(string schoolId, long academicYearId, long? classId, long? sectionId, CancellationToken ct) => throw new InvalidOperationException("down");
            public Task<bool> ClassExistsAsync(string schoolId, long classId, CancellationToken ct) => throw new InvalidOperationException("down");
            public Task<LateFeeReportSummaryRow> GetSummaryAsync(string schoolId, DateTime fromUtc, DateTime toExclusiveUtc, long? classId, CancellationToken ct) => throw new InvalidOperationException("down");
            public Task<List<LateFeeChargedRow>> ListChargedAsync(string schoolId, DateTime fromUtc, DateTime toExclusiveUtc, long? classId, int offset, int limit, CancellationToken ct) => throw new InvalidOperationException("down");
            public Task<List<LateFeeWaivedRow>> ListWaivedAsync(string schoolId, DateTime fromUtc, DateTime toExclusiveUtc, long? classId, int offset, int limit, CancellationToken ct) => throw new InvalidOperationException("down");
        }

        private static long Tax(TaxDb db, string school) => db.Scalar<long>($"insert into tb_tax_types (school_id, tax_name, tax_code, rate_percent, is_active) values ('{school}', 'GST', 'GST', 18, true) returning tax_type_id");

        /// <summary>A payment (and its late fee row) made on a chosen instant, for the month-boundary tests: the late fee, no tax, fee 100.</summary>
        private static void LatePayment(TaxDb db, string school, string student, long due, decimal late, string receipt, string atUtc, string status = "Active") =>
            db.Exec($@"insert into tb_fee_payments (student_id, school_id, payment_date, amount, payment_mode, receipt_number, fee_amount, late_fee_amount, late_fee_decision, late_fee_decided_by, late_fee_rule_snapshot, status, voided_by, voided_at, void_reason)
                       values ('{student}', '{school}', '{atUtc}', {100 + late}, 'CASH', '{receipt}', 100, {late}, 'APPLIED', {TaxDb.UserA}, '{{}}'::jsonb, '{status}', {(status == "Voided" ? TaxDb.UserA.ToString() : "null")}, {(status == "Voided" ? "now()" : "null")}, {(status == "Voided" ? "'x'" : "null")});
                       insert into tb_fee_payment_late_fees (school_id, payment_id, fee_due_id, amount, days_late, days_charged, base_amount, computed_total, already_collected, rule_type, rule_amount, grace_days)
                       values ('{school}', (select payment_id from tb_fee_payments where school_id='{school}' and receipt_number='{receipt}'), {due}, {late}, 60, 55, 100, {late}, 0, 'FLAT', {late}, 5)");

        // ------------------------------------------------------------------------------------------------ Accountant dashboard
        [DbFact]
        public async Task The_Accountant_dashboard_adds_late_fee_collected_and_total_received_and_the_cash_collected_tiles_stay_fee_only()
        {
            using var db = new TaxDb();
            var (w, s, tui, bus) = await B.World(db, "ZCA", tax: true);
            var tax = Tax(db, w.School);
            var voided = await B.Rec(db, w.School, s, B.Pay(tui, 1000));                  // a payment with a late fee that is then voided: nothing may be counted from it
            await db.Payments().VoidPaymentAsync(w.School, voided.PaymentId, TaxDb.UserA, new VoidPaymentRequest { VoidReason = "mistake" }, default);
            var withLate = await B.Rec(db, w.School, s, B.Pay(tui, 1000, tax: tax));     // fee 1000 + late fee 50 + tax 189 (18% of 1050) = 1239
            var plain = await B.Rec(db, w.School, s, B.Pay(bus, 500));                    // fee only: 500
            Assert.Equal(1239m, withLate.Amount);
            var d = (await Accountant(db).GetSummaryAsync(w.School, null, default)).Data!;
            Assert.DoesNotContain("lateFee", d.PartsUnavailable);
            Assert.Equal(1500m, d.Stats.CollectedToday);                                  // "Cash collected today": fee only, exactly as before
            Assert.Equal(1500m, d.Stats.CollectedThisMonth);
            Assert.Equal((50m, 50m), (d.Stats.LateFeeToday, d.Stats.LateFeeThisMonth));   // the late fee on its own, without its tax
            Assert.Equal((1739m, 1739m), (d.Stats.TotalReceivedToday, d.Stats.TotalReceivedThisMonth));   // the receipts: 1239 + 500
            // the drawer reconciles: total received = fee + late fee + tax
            Assert.Equal(d.Stats.TotalReceivedToday, d.Stats.CollectedToday + d.Stats.LateFeeToday + db.Scalar<decimal>($"select coalesce(sum(tax_amount),0) from tb_fee_payments where school_id='{w.School}' and status='Active'"));
        }

        [DbFact]
        public async Task A_school_with_the_rule_off_shows_real_zeros_and_a_failing_read_marks_the_tiles_unavailable_never_zero()
        {
            using var db = new TaxDb();
            var (w, s, tui, _) = L.Student(db, "ZCB");                                    // no rule at all
            await B.Rec(db, w.School, s, B.Pay(tui, 1000));
            var d = (await Accountant(db).GetSummaryAsync(w.School, null, default)).Data!;
            Assert.Equal((0m, 0m, 1000m), (d.Stats.LateFeeToday, d.Stats.LateFeeThisMonth, d.Stats.TotalReceivedToday));
            var c = db.Ctx();
            var down = new AccountantDashboardService(db.Reports(), new AccountantDashboardRepository(c), new SchoolDashboardRepository(c), NullLogger<AccountantDashboardService>.Instance, new DownLateFeeReport());
            var broken = (await down.GetSummaryAsync(w.School, null, default)).Data!;
            Assert.Contains("lateFee", broken.PartsUnavailable);
            Assert.Null(broken.Stats.LateFeeToday); Assert.Null(broken.Stats.TotalReceivedThisMonth);   // the client says Unavailable
            Assert.Equal(1000m, broken.Stats.CollectedToday);                              // the rest of the dashboard is unaffected
        }

        [DbFact]
        public async Task A_payment_just_before_and_just_after_midnight_India_time_belongs_to_the_right_month()
        {
            using var db = new TaxDb();
            var (w, s, tui, bus) = await B.World(db, "ZCC");
            LatePayment(db, w.School, s, tui, 30, "ZCC-OCT", "2026-10-31 18:29:00+00");   // 23:59 IST on 31 October
            LatePayment(db, w.School, s, bus, 70, "ZCC-NOV", "2026-10-31 18:30:00+00");   // 00:00 IST on 1 November
            var repo = new LateFeeReportRepository(db.Ctx());
            var oct = await repo.GetReceivedBetweenAsync(w.School, SchoolClock.StartOfDayUtc(new DateOnly(2026, 10, 1)), SchoolClock.StartOfDayUtc(new DateOnly(2026, 11, 1)), default);
            var nov = await repo.GetReceivedBetweenAsync(w.School, SchoolClock.StartOfDayUtc(new DateOnly(2026, 11, 1)), SchoolClock.StartOfDayUtc(new DateOnly(2026, 12, 1)), default);
            Assert.Equal((30m, 130m), (oct.LateFeeAmount, oct.TotalAmount));
            Assert.Equal((70m, 170m), (nov.LateFeeAmount, nov.TotalAmount));
            var months = await repo.GetByMonthAsync(w.School, w.S, null, null, default);
            Assert.Equal(new[] { (2026, 10, 30m), (2026, 11, 70m) }, months.OrderBy(m => m.Year).ThenBy(m => m.Month).Select(m => (m.Year, m.Month, m.LateFee)).ToArray());
            var r = (await Report(db).GetAsync(w.School, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), null, "charged", 1, 25, default)).Data!;
            Assert.Equal(("ZCC-NOV", 1, 70m), (Assert.Single(r.Charged).ReceiptNumber, r.ChargedCount, r.ChargedAmount));   // the 1 Nov 00:00 payment is in November only
        }

        // ------------------------------------------------------------------------------------------------ School Admin dashboard
        [DbFact]
        public async Task The_School_Admin_dashboard_has_a_late_fee_tile_for_the_month_and_the_fee_received_tile_does_not_move()
        {
            using var db = new TaxDb();
            var (a, sa, tuiA, _) = await B.World(db, "ZCD");                               // rule on
            var (b, sb, tuiB, _) = L.Student(db, "ZCE");                                    // the same dues, no rule
            await B.Rec(db, a.School, sa, B.Pay(tuiA, 1000));
            await B.Rec(db, b.School, sb, B.Pay(tuiB, 1000));
            var withRule = (await SchoolAdmin(db).GetAsync(a.School, default, null, a.S)).Data!;
            var without = (await SchoolAdmin(db).GetAsync(b.School, default, null, b.S)).Data!;
            Assert.Equal(50m, withRule.LateFeeCollectedThisMonth);
            Assert.Equal(0m, without.LateFeeCollectedThisMonth);                           // no rule: a real zero, no error
            Assert.Equal((without.FeeCollectedThisMonth, without.IncomeThisMonth, without.IncomeThisSession), (withRule.FeeCollectedThisMonth, withRule.IncomeThisMonth, withRule.IncomeThisSession));   // nothing existing moved
            Assert.Equal(1000m, withRule.FeeCollectedThisMonth);
            var down = (await SchoolAdmin(db, new DownLateFeeReport()).GetAsync(a.School, default, null, a.S)).Data!;
            Assert.Null(down.LateFeeCollectedThisMonth);                                   // Unavailable, never 0
            Assert.Equal(1000m, down.FeeCollectedThisMonth);
        }

        // ------------------------------------------------------------------------------------------------ collection by month
        [DbFact]
        public async Task The_collection_by_month_has_late_fee_and_total_received_columns_that_leave_the_fee_column_and_the_totals_alone()
        {
            using var db = new TaxDb();
            var (w, s, tui, bus) = await B.World(db, "ZCF", tax: true);
            var tax = Tax(db, w.School);
            var gone = await B.Rec(db, w.School, s, B.Pay(tui, 1000));                       // voided: it is in no column
            await db.Payments().VoidPaymentAsync(w.School, gone.PaymentId, TaxDb.UserA, new VoidPaymentRequest { VoidReason = "mistake" }, default);
            await B.Rec(db, w.School, s, B.Pay(tui, 1000, tax: tax));                        // 1000 + 50 + 189
            await B.Rec(db, w.School, s, B.Pay(bus, 500));
            var d = (await db.Reports().GetDashboardAsync(w.School, w.S, null, null, 7, true, default)).Data!;
            var m = d.CollectionByMonth.Single(x => x.Amount > 0);
            Assert.Equal((1500m, 50m, 1739m), (m.Amount, m.LateFee, m.TotalReceived));      // fee 1500 + late fee 50 + tax 189
            Assert.Equal(1500m, d.TotalCollected);                                           // existing totals: fee only
            Assert.Equal(189m, d.TaxCollected);                                              // the tax on the late fee stays in the tax report
            Assert.All(d.CollectionByMonth.Where(x => x.Amount == 0), x => Assert.Equal((0m, 0m), (x.LateFee, x.TotalReceived)));
            // a class filter that excludes the student: zeros
            var other = db.Scalar<long>($"select class_id from tb_classes where school_id='{w.School}' and class_id <> {w.C1} limit 1");
            var empty = (await db.Reports().GetDashboardAsync(w.School, w.S, other, null, 7, true, default)).Data!;
            Assert.All(empty.CollectionByMonth, x => Assert.Equal((0m, 0m, 0m), (x.Amount, x.LateFee, x.TotalReceived)));
        }

        [DbFact]
        public async Task A_payment_over_two_years_counts_its_late_fee_once_in_each_years_own_view_and_never_twice()
        {
            using var db = new TaxDb();
            var (w, s, tui, _) = await B.World(db, "ZCG");
            // a second year's Tuition due, also flagged and also overdue
            db.Exec($@"insert into tb_student_fee_items (student_id, school_id, academic_year_id, fee_type_id, frequency_id, base_amount, net_amount)
                       values ('{s}', '{w.School}', {w.T}, (select fee_type_id from tb_fee_types where school_id='{w.School}' and fee_type_code='TUI'), (select min(frequency_id) from lut_fee_frequency), 800, 800);
                       insert into tb_student_fee_dues (student_fee_item_id, student_id, school_id, due_date, amount_due, amount_paid)
                       select student_fee_item_id, '{s}', '{w.School}', '2026-09-02', 800, 0 from tb_student_fee_items where student_id='{s}' and academic_year_id={w.T}");
            var dueT = db.Scalar<long>($"select d.fee_due_id from tb_student_fee_dues d join tb_student_fee_items i using(student_fee_item_id) where i.student_id='{s}' and i.academic_year_id={w.T}");
            var r = await db.Payments().RecordPaymentAsync(w.School, s, TaxDb.UserA, new RecordPaymentRequest { Amount = 1800, PaymentMode = "Cash", Allocations = new() { new() { FeeDueId = tui, Amount = 1000 }, new() { FeeDueId = dueT, Amount = 800 } } }, default);
            Assert.True(r.Status, r.Message);
            Assert.Equal(100m, r.Data!.LateFee!.Amount);                                    // 50 on each year's due
            var repo = new LateFeeReportRepository(db.Ctx());
            var y1 = await repo.GetLateFeeCollectedAsync(w.School, w.S, null, null, default);
            var y2 = await repo.GetLateFeeCollectedAsync(w.School, w.T, null, null, default);
            Assert.Equal((50m, 50m, 100m), (y1, y2, y1 + y2));                               // each year sees its own part; together they are the payment's late fee, once
            Assert.Equal(100m, (await repo.GetReceivedBetweenAsync(w.School, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), default)).LateFeeAmount);
        }

        // ------------------------------------------------------------------------------------------------ the Late fee report
        private static async Task<(P.W W, string Student, long Tui, long Bus)> TwoFlagged(TaxDb db, string id)
        {
            var (w, s, tui, bus) = L.Student(db, id);
            await L.Svc(db).SaveRuleAsync(w.School, TaxDb.UserA, L.Save(type: "FLAT", amount: 50, grace: 5, cap: null, types: new[] { L.Type(db, w.School, "TUI"), L.Type(db, w.School, "BUS") }), default);
            return (w, s, tui, bus);
        }

        [DbFact]
        public async Task The_report_lists_each_late_fee_per_payment_and_due_with_the_days_and_the_waived_ones_apart_and_leaves_voided_payments_out()
        {
            using var db = new TaxDb();
            var (w, s, tui, bus) = await TwoFlagged(db, "ZCH");
            var both = await db.Payments().RecordPaymentAsync(w.School, s, TaxDb.UserA, new RecordPaymentRequest { Amount = 1500, PaymentMode = "Cash", Allocations = new() { new() { FeeDueId = tui, Amount = 1000 }, new() { FeeDueId = bus, Amount = 500 } } }, default);
            Assert.True(both.Status, both.Message);
            var report = await Report(db).GetAsync(w.School, null, null, null, "charged", 1, 25, default);
            Assert.True(report.Status, report.Message);
            var r = report.Data!;
            Assert.Equal((2, 100m, 0, 0m), (r.ChargedCount, r.ChargedAmount, r.WaivedCount, r.WaivedAmount));
            Assert.Equal(new[] { "Tuition", "Bus" }.OrderBy(x => x), r.Charged.Select(c => c.FeeType).OrderBy(x => x));
            var line = r.Charged.Single(c => c.FeeType == "Tuition");
            Assert.Equal((both.Data!.ReceiptNumber, s, new DateOnly(2026, 9, 1), 50m), (line.ReceiptNumber, line.StudentId, line.DueDate, line.Amount));
            Assert.Equal((SchoolClock.Today().DayNumber - line.DueDate.DayNumber, SchoolClock.Today().DayNumber - line.DueDate.DayNumber - 5, 1000m), (line.DaysLate, line.DaysCharged, line.BaseAmount));
            Assert.Equal("Class 1", line.ClassName);
            // voiding the payment removes it from the report and the totals
            await db.Payments().VoidPaymentAsync(w.School, both.Data.PaymentId, TaxDb.UserA, new VoidPaymentRequest { VoidReason = "wrong" }, default);
            var after = (await Report(db).GetAsync(w.School, null, null, null, "charged", 1, 25, default)).Data!;
            Assert.Equal((0, 0m, 0), (after.ChargedCount, after.ChargedAmount, after.Charged.Count));
        }

        [DbFact]
        public async Task Waived_late_fees_are_counted_and_listed_apart_with_what_they_would_have_been_who_waived_and_why()
        {
            using var db = new TaxDb();
            var (w, s, tui, _) = await TwoFlagged(db, "ZCI");
            var waived = await B.Rec(db, w.School, s, B.Pay(tui, 1000, apply: false, note: "Poor family"));
            var r = (await Report(db).GetAsync(w.School, null, null, null, "waived", 1, 25, default)).Data!;
            Assert.Equal((0, 0m, 1, 50m), (r.ChargedCount, r.ChargedAmount, r.WaivedCount, r.WaivedAmount));
            var item = Assert.Single(r.Waived);
            Assert.Equal((waived.ReceiptNumber, s, 50m, "Poor family", "Class 1"), (item.ReceiptNumber, item.StudentId, item.WouldHaveBeen, item.Note, item.ClassName));
            Assert.False(string.IsNullOrWhiteSpace(item.WaivedBy));
            Assert.Empty(r.Charged);                                                        // the other list is left empty for this view
            await db.Payments().VoidPaymentAsync(w.School, waived.PaymentId, TaxDb.UserA, new VoidPaymentRequest { VoidReason = "x" }, default);
            Assert.Equal((0, 0m), ((await Report(db).GetAsync(w.School, null, null, null, "waived", 1, 25, default)).Data!.WaivedCount, (await Report(db).GetAsync(w.School, null, null, null, "waived", 1, 25, default)).Data!.WaivedAmount));
        }

        [DbFact]
        public async Task The_report_pages_filters_by_class_and_range_validates_and_is_empty_with_the_rule_off()
        {
            using var db = new TaxDb();
            var (w, s, tui, bus) = await TwoFlagged(db, "ZCJ");
            await db.Payments().RecordPaymentAsync(w.School, s, TaxDb.UserA, new RecordPaymentRequest { Amount = 1500, PaymentMode = "Cash", Allocations = new() { new() { FeeDueId = tui, Amount = 1000 }, new() { FeeDueId = bus, Amount = 500 } } }, default);
            var p1 = (await Report(db).GetAsync(w.School, null, null, null, "charged", 1, 1, default)).Data!;
            var p2 = (await Report(db).GetAsync(w.School, null, null, null, "charged", 2, 1, default)).Data!;
            Assert.Equal((1, 2, 1, 2), (p1.Charged.Count, p1.TotalItems, p2.Charged.Count, p2.TotalItems));
            Assert.NotEqual(p1.Charged[0].FeeType, p2.Charged[0].FeeType);
            Assert.Equal(2, (await Report(db).GetAsync(w.School, null, null, w.C1, "charged", 1, 25, default)).Data!.ChargedCount);
            Assert.Equal(0, (await Report(db).GetAsync(w.School, null, null, w.C2, "charged", 1, 25, default)).Data!.ChargedCount);                       // another class: nothing
            var nextYear = new DateOnly(SchoolClock.Today().Year + 1, 1, 1);
            Assert.Equal(0, (await Report(db).GetAsync(w.School, nextYear, nextYear.AddDays(30), null, "charged", 1, 25, default)).Data!.ChargedCount);   // another range
            Assert.Equal(400, (await Report(db).GetAsync(w.School, new DateOnly(2026, 11, 30), new DateOnly(2026, 11, 1), null, "charged", 1, 25, default)).StatusCode);
            var (off, so, tuiO, _) = L.Student(db, "ZCK");                                   // no rule, never a late fee
            await B.Rec(db, off.School, so, B.Pay(tuiO, 1000));
            var none = await Report(db).GetAsync(off.School, null, null, null, "charged", 1, 25, default);
            Assert.True(none.Status);
            Assert.Equal((0, 0m, 0, 0m), (none.Data!.ChargedCount, none.Data.ChargedAmount, none.Data.WaivedCount, none.Data.WaivedAmount));
            Assert.Empty(none.Data.Charged);
            Assert.Equal(400, (await Report(db).GetAsync(null, null, null, null, "charged", 1, 25, default)).StatusCode);
        }

        [DbFact]
        public async Task The_report_never_shows_another_schools_payments_and_another_schools_class_is_a_404()
        {
            using var db = new TaxDb();
            var (a, sa, tuiA, _) = await TwoFlagged(db, "ZCL");
            var (b, sb, tuiB, _) = await TwoFlagged(db, "ZCM");
            await B.Rec(db, a.School, sa, B.Pay(tuiA, 1000));
            await B.Rec(db, b.School, sb, B.Pay(tuiB, 1000));
            var forA = (await Report(db).GetAsync(a.School, null, null, null, "charged", 1, 25, default)).Data!;
            Assert.Equal(1, forA.ChargedCount);
            Assert.All(forA.Charged, c => Assert.Equal(sa, c.StudentId));
            Assert.Equal(404, (await Report(db).GetAsync(a.School, null, null, b.C1, "charged", 1, 25, default)).StatusCode);   // B's class id
            var accountantB = (await Accountant(db).GetSummaryAsync(b.School, null, default)).Data!;
            Assert.Equal(50m, accountantB.Stats.LateFeeToday);                                // each school sees only its own late fee
        }

        public static IEnumerable<object[]> RoleCases() => Fake.AllRoles.Concat(new[] { "Driver" }).Select(r => new object[] { r });

        [Theory, MemberData(nameof(RoleCases))]
        public async Task The_School_Admin_Accountant_and_Principal_read_the_late_fee_report_and_nobody_else(string role)
        {
            var svc = Fake.Of<ILateFeeReportService>();
            var c = Fake.WithUser(new LateFeeController(Fake.Of<ILateFeeService>(), svc), Fake.User("S", 5, role));
            var code = Fake.Code(await c.Report(null, null, null, null, 1, 25, default));
            var allowed = role is "School Admin" or "Accountant" or "Principal";
            Assert.Equal(!allowed, code == 403);
            Assert.Equal(allowed, Fake.ProxyOf(svc).Calls.Contains("GetAsync"));
        }
    }
}
