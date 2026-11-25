using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using smss_api.Controllers;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;
using P = smss_api_tests.PromotionPreview4b2Tests;

namespace smss_api_tests
{
    /// <summary>
    /// Stage 5a: late fee settings (the school rule, the fee type flags) and the read-only preview of what a payment would be charged. Nothing is charged yet. The pure arithmetic tests need no database;
    /// the rest need Docs/smss-st-5a1-RUN-ME.sql applied to the template (SMSS_TEST_TEMPLATE). Payments and late-fee rows are throwaway rows on the scratch database only.
    /// </summary>
    public class LateFee5aTests
    {
        private static readonly DateOnly Today = new(2026, 11, 25);
        private static LateFeeService Svc(TaxDb db) => new(new LateFeeRepository(db.Ctx()), NullLogger<LateFeeService>.Instance);
        private static LateFeeRuleValues Rule(string type, decimal? amount = null, decimal? percent = null, int grace = 0, decimal? cap = null) => new(type, amount, percent, grace, cap);

        // ------------------------------------------------------------------------------------------------ the arithmetic (no database)
        [Fact]
        public void A_due_is_late_only_for_the_days_beyond_the_grace_days_counted_in_calendar_days()
        {
            var due = new DateOnly(2026, 11, 10);
            Assert.Equal(0, LateFeeMath.DaysBeyondGrace(due, new DateOnly(2026, 11, 10), 0));    // due today: not late
            Assert.Equal(1, LateFeeMath.DaysBeyondGrace(due, new DateOnly(2026, 11, 11), 0));
            Assert.Equal(0, LateFeeMath.DaysBeyondGrace(due, new DateOnly(2026, 11, 15), 5));    // exactly the grace: still free
            Assert.Equal(1, LateFeeMath.DaysBeyondGrace(due, new DateOnly(2026, 11, 16), 5));
            Assert.Null(LateFeeMath.Charge(Rule("FLAT", 50, grace: 5), due, new DateOnly(2026, 11, 15), 1000, 0));
            Assert.NotNull(LateFeeMath.Charge(Rule("FLAT", 50, grace: 5), due, new DateOnly(2026, 11, 16), 1000, 0));
            Assert.Null(LateFeeMath.Charge(Rule("FLAT", 50), due, new DateOnly(2026, 11, 1), 1000, 0));   // not due yet
        }

        [Fact]
        public void Flat_is_one_amount_per_late_due_capped_and_never_charged_twice_for_the_same_due()
        {
            var due = new DateOnly(2026, 9, 1);
            var first = LateFeeMath.Charge(Rule("FLAT", 50, grace: 5, cap: 100), due, Today, 400, 0)!;
            Assert.Equal((85, 80, 400m, 50m, 0m, 50m), (first.DaysLate, first.DaysCharged, first.BaseAmount, first.ComputedTotal, first.AlreadyCollected, first.Amount));
            Assert.Null(LateFeeMath.Charge(Rule("FLAT", 50, grace: 5, cap: 100), due, Today, 600, 50));            // the second partial payment: the flat fee was already collected
            Assert.Equal(30m, LateFeeMath.Charge(Rule("FLAT", 50), due, Today, 600, 20)!.Amount);                    // part of it was collected: the rest is charged
            Assert.Equal(40m, LateFeeMath.Charge(Rule("FLAT", 150, cap: 100), due, Today, 600, 60)!.Amount);         // the cap (100) minus what was collected (60)
            Assert.Null(LateFeeMath.Charge(Rule("FLAT", 150, cap: 100), due, Today, 600, 100));                      // cap reached
        }

        [Fact]
        public void Per_day_charges_only_the_days_beyond_grace_and_the_cap_applies_per_due()
        {
            var due = new DateOnly(2026, 11, 1);                                     // 24 days late on 25 Nov
            var c = LateFeeMath.Charge(Rule("PER_DAY", 2, grace: 4), due, Today, 500, 0)!;
            Assert.Equal((24, 20, 40m, 40m), (c.DaysLate, c.DaysCharged, c.ComputedTotal, c.Amount));                 // 20 days beyond grace x 2
            Assert.Equal(30m, LateFeeMath.Charge(Rule("PER_DAY", 2, cap: 30), due, Today, 500, 0)!.Amount);          // 24 x 2 = 48, capped at 30
            Assert.Equal(10m, LateFeeMath.Charge(Rule("PER_DAY", 2, cap: 30), due, Today, 500, 20)!.Amount);         // 30 - 20 already
            Assert.Equal(8m, LateFeeMath.Charge(Rule("PER_DAY", 2), due, Today, 500, 40)!.Amount);                   // total today 48, 40 collected on earlier days
        }

        [Fact]
        public void Percent_is_of_the_amount_paid_against_the_due_in_this_payment_with_the_cap_per_due_still_binding()
        {
            var due = new DateOnly(2026, 11, 1);
            Assert.Equal(40m, LateFeeMath.Charge(Rule("PERCENT", percent: 10), due, Today, 400, 0)!.Amount);
            Assert.Equal(20m, LateFeeMath.Charge(Rule("PERCENT", percent: 10), due, Today, 200, 40)!.Amount);        // each partial payment pays its own percentage (the earlier one does not reduce it)
            Assert.Equal(20m, LateFeeMath.Charge(Rule("PERCENT", percent: 10, cap: 100), due, Today, 400, 80)!.Amount);   // cap 100 - 80 collected
            Assert.Null(LateFeeMath.Charge(Rule("PERCENT", percent: 10, cap: 100), due, Today, 400, 100));
            Assert.Equal(33.33m, LateFeeMath.Charge(Rule("PERCENT", percent: 10), due, Today, 333.33m, 0)!.Amount);   // rounded to 2 places
            Assert.Equal(0.05m, LateFeeMath.Charge(Rule("PERCENT", percent: 5), due, Today, 1m, 0)!.Amount);         // half away from zero
            Assert.Null(LateFeeMath.Charge(Rule("PERCENT", percent: 10), due, Today, 0, 0));                         // nothing applied to the due
        }

        // ------------------------------------------------------------------------------------------------ the settings
        private static PromotionPreview4b2Tests.W World(TaxDb db, string id)
        {
            var w = P.Build(db, id);
            db.Exec($"insert into tb_fee_types (school_id, fee_type_name, fee_type_code, is_optional, isactive) values ('{id}', 'Bus', 'BUS', true, true)");   // Build makes Tuition; late fee tests need a second fee type
            return w;
        }
        private static SaveLateFeeRuleRequest Save(bool enabled = true, string? type = "FLAT", decimal? amount = 50, decimal? percent = null, int grace = 5, decimal? cap = 100, bool tax = false, long[]? types = null) =>
            new() { Enabled = enabled, RuleType = type, Amount = amount, Percent = percent, GraceDays = grace, CapPerDue = cap, TaxApplies = tax, FeeTypeIds = (types ?? Array.Empty<long>()).ToList() };
        private static long Type(TaxDb db, string school, string code) => db.Scalar<long>($"select fee_type_id from tb_fee_types where school_id='{school}' and fee_type_code='{code}'");

        [DbFact]
        public async Task A_school_that_never_set_a_rule_has_it_off_and_every_fee_type_flag_off()
        {
            using var db = new TaxDb();
            var w = World(db, "ZLA");
            var r = (await Svc(db).GetRuleAsync(w.School, default)).Data!;
            Assert.False(r.Enabled); Assert.Null(r.RuleType); Assert.False(r.TaxApplies); Assert.Equal(0, r.GraceDays);
            Assert.Equal(2, r.FeeTypes.Count); Assert.All(r.FeeTypes, f => Assert.False(f.AppliesLateFee));
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_school_late_fee_rules where school_id='{w.School}'"));     // reading never creates a row
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_fee_payments where late_fee_amount <> 0 or late_fee_decision is not null"));   // no existing payment has a late fee
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_fee_types where applies_late_fee"));
        }

        [DbFact]
        public async Task The_rule_and_the_flags_are_saved_together_and_only_the_figure_of_the_chosen_type_is_kept()
        {
            using var db = new TaxDb();
            var w = World(db, "ZLB");
            var saved = await Svc(db).SaveRuleAsync(w.School, TaxDb.UserA, Save(type: "FLAT", amount: 50, percent: 12, grace: 5, cap: 100, tax: true, types: new[] { Type(db, w.School, "TUI") }), default);
            Assert.True(saved.Status, saved.Message);
            var r = saved.Data!;
            Assert.Equal((true, "FLAT", 50m, null, 5, 100m, true), (r.Enabled, r.RuleType, r.Amount, r.Percent, r.GraceDays, r.CapPerDue, r.TaxApplies));
            Assert.Equal(new[] { true, false }, r.FeeTypes.OrderBy(f => f.Name == "Tuition" ? 0 : 1).Select(f => f.AppliesLateFee).ToArray());
            Assert.Equal(TaxDb.UserA, db.Scalar<long>($"select updated_by from tb_school_late_fee_rules where school_id='{w.School}'"));
            // a second save replaces everything: another type, another flag set
            var again = (await Svc(db).SaveRuleAsync(w.School, TaxDb.UserA, Save(type: "PERCENT", amount: null, percent: 7.5m, grace: 0, cap: null, types: new[] { Type(db, w.School, "BUS") }), default)).Data!;
            Assert.Equal(("PERCENT", null, 7.5m, null), (again.RuleType, again.Amount, again.Percent, again.CapPerDue));
            Assert.Equal(new[] { "Bus" }, again.FeeTypes.Where(f => f.AppliesLateFee).Select(f => f.Name).ToArray());
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_school_late_fee_rules where school_id='{w.School}'"));
            // switching it off keeps the rest but charges nothing
            Assert.False((await Svc(db).SaveRuleAsync(w.School, TaxDb.UserA, Save(enabled: false, type: "PERCENT", amount: null, percent: 7.5m), default)).Data!.Enabled);
        }

        [DbFact]
        public async Task A_rule_that_cannot_work_is_refused_and_nothing_changes()
        {
            using var db = new TaxDb();
            var w = World(db, "ZLC");
            var svc = Svc(db);
            await svc.SaveRuleAsync(w.School, TaxDb.UserA, Save(types: new[] { Type(db, w.School, "TUI") }), default);
            var before = db.Scalar<string>($"select md5(r::text) from tb_school_late_fee_rules r where school_id='{w.School}'") + db.Scalar<string>($"select string_agg(applies_late_fee::text, ',' order by fee_type_id) from tb_fee_types where school_id='{w.School}'");
            var bad = new[]
            {
                Save(type: null), Save(type: "FLAT", amount: null), Save(type: "PER_DAY", amount: 0), Save(type: "PERCENT", amount: null, percent: null), Save(type: "PERCENT", amount: null, percent: 0), Save(type: "PERCENT", amount: null, percent: 101),
                Save(type: "WEEKLY"), Save(grace: -1), Save(grace: 366), Save(cap: 0), Save(amount: 0),
            };
            foreach (var b in bad) Assert.Equal(400, (await svc.SaveRuleAsync(w.School, TaxDb.UserA, b, default)).StatusCode);
            Assert.Equal(400, (await svc.SaveRuleAsync(null, TaxDb.UserA, Save(), default)).StatusCode);
            Assert.Equal(before, db.Scalar<string>($"select md5(r::text) from tb_school_late_fee_rules r where school_id='{w.School}'") + db.Scalar<string>($"select string_agg(applies_late_fee::text, ',' order by fee_type_id) from tb_fee_types where school_id='{w.School}'"));
        }

        [DbFact]
        public async Task Another_schools_fee_type_is_a_404_and_is_never_flagged()
        {
            using var db = new TaxDb();
            var a = World(db, "ZLD"); var b = World(db, "ZLE");
            var other = Type(db, b.School, "TUI");
            var x = await Svc(db).SaveRuleAsync(a.School, TaxDb.UserA, Save(types: new[] { other }), default);
            Assert.Equal(404, x.StatusCode);
            Assert.False(db.Scalar<bool>($"select applies_late_fee from tb_fee_types where fee_type_id={other}"));
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_school_late_fee_rules where school_id='{a.School}'"));
            await Svc(db).SaveRuleAsync(a.School, TaxDb.UserA, Save(types: new[] { Type(db, a.School, "TUI") }), default);
            Assert.False(db.Scalar<bool>($"select applies_late_fee from tb_fee_types where fee_type_id={other}"));       // saving school A never touches school B
            Assert.Empty((await Svc(db).GetRuleAsync(b.School, default)).Data!.FeeTypes.Where(f => f.AppliesLateFee));
        }

        public static IEnumerable<object[]> RoleCases() => Fake.AllRoles.Concat(new[] { "Driver" }).Select(r => new object[] { r });

        [Theory, MemberData(nameof(RoleCases))]
        public async Task The_School_Admin_Accountant_and_Principal_read_the_rule_only_the_School_Admin_changes_it_and_the_School_Admin_and_Accountant_preview(string role)
        {
            var svc = Fake.Of<ILateFeeService>();
            var c = Fake.WithUser(new LateFeeController(svc), Fake.User("S", 5, role));
            var read = Fake.Code(await c.GetRule(default));
            var write = Fake.Code(await c.SaveRule(Save(), default));
            var preview = Fake.Code(await c.Preview("ST1", new LateFeePreviewRequest { Amount = 100 }, default));
            Assert.Equal(role is not ("School Admin" or "Accountant" or "Principal"), read == 403);
            Assert.Equal(role != "School Admin", write == 403);
            Assert.Equal(role is not ("School Admin" or "Accountant"), preview == 403);
            Assert.Equal(role == "School Admin", Fake.ProxyOf(svc).Calls.Contains("SaveRuleAsync"));
        }

        // ------------------------------------------------------------------------------------------------ the preview
        /// <summary>A student with a Tuition due of 1000 (due 1 Sep, flagged) and a Bus due of 500 (due 1 Oct, not flagged), both unpaid.</summary>
        private static (PromotionPreview4b2Tests.W W, string Student, long Tuition, long Bus) Student(TaxDb db, string id)
        {
            var w = World(db, id);
            var s = id + "S1";
            P.Student(db, id, s);
            P.Enrol(db, id, s, w.S, w.C1A, "1");
            P.Due(db, w, s, "2026-09-01", 1000, 0);
            db.Exec($@"insert into tb_student_fee_items (student_id, school_id, academic_year_id, fee_type_id, frequency_id, base_amount, net_amount)
                       values ('{s}', '{id}', {w.S}, (select fee_type_id from tb_fee_types where school_id='{id}' and fee_type_code='BUS'), (select min(frequency_id) from lut_fee_frequency), 500, 500);
                       insert into tb_student_fee_dues (student_fee_item_id, student_id, school_id, due_date, amount_due, amount_paid)
                       select student_fee_item_id, '{s}', '{id}', '2026-10-01', 500, 0 from tb_student_fee_items where student_id='{s}' and fee_type_id=(select fee_type_id from tb_fee_types where school_id='{id}' and fee_type_code='BUS')");
            long Due(string code) => db.Scalar<long>($"select d.fee_due_id from tb_student_fee_dues d join tb_student_fee_items i using(student_fee_item_id) join tb_fee_types t on t.fee_type_id=i.fee_type_id where i.student_id='{s}' and t.fee_type_code='{code}'");
            return (w, s, Due("TUI"), Due("BUS"));
        }

        private static async Task<LateFeePreviewDto> Preview(TaxDb db, string school, string student, LateFeePreviewRequest r)
        {
            var x = await Svc(db).PreviewAsync(school, student, r, default, Today);
            Assert.True(x.Status, x.Message);
            return x.Data!;
        }

        /// <summary>An earlier payment that collected a late fee on a due (the row the payment step will write in 5b), voided or not.</summary>
        private static void EarlierLateFee(TaxDb db, string school, string student, long due, decimal fee, string receipt, bool voided = false) =>
            db.Exec($@"insert into tb_fee_payments (student_id, school_id, amount, payment_mode, receipt_number, fee_amount, late_fee_amount, late_fee_decision, late_fee_decided_by, late_fee_rule_snapshot, status, voided_by, voided_at, void_reason)
                       values ('{student}', '{school}', {100 + fee}, 'CASH', '{receipt}', 100, {fee}, 'APPLIED', {TaxDb.UserA}, '{{}}'::jsonb, '{(voided ? "Voided" : "Active")}', {(voided ? TaxDb.UserA.ToString() : "null")}, {(voided ? "now()" : "null")}, {(voided ? "'mistake'" : "null")});
                       insert into tb_fee_payment_late_fees (school_id, payment_id, fee_due_id, amount, days_late, days_charged, base_amount, computed_total, already_collected, rule_type, rule_amount, grace_days)
                       values ('{school}', (select payment_id from tb_fee_payments where school_id='{school}' and receipt_number='{receipt}'), {due}, {fee}, 85, 80, 100, {fee}, 0, 'FLAT', {fee}, 5)");

        [DbFact]
        public async Task The_preview_charges_only_flagged_fee_types_that_are_late_beyond_the_grace_days()
        {
            using var db = new TaxDb();
            var (w, s, tui, bus) = Student(db, "ZLF");
            var all = new LateFeePreviewRequest { Allocations = new() { new() { FeeDueId = tui, Amount = 1000 }, new() { FeeDueId = bus, Amount = 500 } } };
            var off = await Preview(db, w.School, s, all);                                                                   // no rule yet
            Assert.False(off.RuleEnabled); Assert.Empty(off.Lines); Assert.Equal(0m, off.Total); Assert.Equal(Today, off.Today);
            await Svc(db).SaveRuleAsync(w.School, TaxDb.UserA, Save(type: "FLAT", amount: 50, grace: 5, cap: null, types: new[] { Type(db, w.School, "TUI") }), default);
            var p = await Preview(db, w.School, s, all);
            var line = Assert.Single(p.Lines);                                                                                // Bus is late too but its fee type is not flagged
            Assert.Equal(("Tuition", 85, 80, 1000m, 50m, 0m, 50m), (line.FeeType, line.DaysLate, line.DaysCharged, line.BaseAmount, line.ComputedTotal, line.AlreadyCollected, line.Amount));
            Assert.Equal(50m, p.Total);
            await Svc(db).SaveRuleAsync(w.School, TaxDb.UserA, Save(type: "FLAT", amount: 50, grace: 90, cap: null, types: new[] { Type(db, w.School, "TUI") }), default);
            Assert.Empty((await Preview(db, w.School, s, all)).Lines);                                                       // 85 days late is inside a 90-day grace
            await Svc(db).SaveRuleAsync(w.School, TaxDb.UserA, Save(enabled: false, type: "FLAT", amount: 50, grace: 5, cap: null, types: new[] { Type(db, w.School, "TUI") }), default);
            Assert.False((await Preview(db, w.School, s, all)).RuleEnabled);                                                 // switched off: nothing, whatever is flagged
        }

        [DbFact]
        public async Task A_due_paid_in_two_partial_payments_is_not_charged_a_flat_late_fee_twice_and_a_voided_payment_frees_it()
        {
            using var db = new TaxDb();
            var (w, s, tui, _) = Student(db, "ZLG");
            await Svc(db).SaveRuleAsync(w.School, TaxDb.UserA, Save(type: "FLAT", amount: 50, grace: 5, cap: 80, types: new[] { Type(db, w.School, "TUI") }), default);
            var part = new LateFeePreviewRequest { Allocations = new() { new() { FeeDueId = tui, Amount = 400 } } };
            Assert.Equal(50m, (await Preview(db, w.School, s, part)).Total);                                                  // the first partial payment pays it
            EarlierLateFee(db, w.School, s, tui, 50, "ZLG-1");
            var second = await Preview(db, w.School, s, part);
            Assert.Empty(second.Lines);                                                                                       // already collected: nothing more
            EarlierLateFee(db, w.School, s, tui, 20, "ZLG-2", voided: true);                                                  // a voided payment counts for nothing
            Assert.Empty((await Preview(db, w.School, s, part)).Lines);
            db.Exec($"update tb_fee_payments set status='Voided', voided_by={TaxDb.UserA}, voided_at=now(), void_reason='wrong amount' where receipt_number='ZLG-1'");
            Assert.Equal(50m, (await Preview(db, w.School, s, part)).Total);                                                  // voiding the first frees its amount again
            await Svc(db).SaveRuleAsync(w.School, TaxDb.UserA, Save(type: "FLAT", amount: 200, grace: 5, cap: 80, types: new[] { Type(db, w.School, "TUI") }), default);
            db.Exec($"update tb_fee_payments set status='Active', voided_by=null, voided_at=null, void_reason=null where receipt_number='ZLG-1'");
            Assert.Equal(30m, (await Preview(db, w.School, s, part)).Total);                                                  // the cap per due (80) minus what is collected (50)
        }

        [DbFact]
        public async Task Per_day_and_percent_rules_and_the_oldest_first_amount_work_through_the_service()
        {
            using var db = new TaxDb();
            var (w, s, tui, bus) = Student(db, "ZLH");
            await Svc(db).SaveRuleAsync(w.School, TaxDb.UserA, Save(type: "PER_DAY", amount: 2, grace: 5, cap: 100, types: new[] { Type(db, w.School, "TUI"), Type(db, w.School, "BUS") }), default);
            var perDay = await Preview(db, w.School, s, new LateFeePreviewRequest { Amount = 1500 });                         // oldest first: Tuition 1000 (2 x 80 = 160, capped 100), then Bus 500 (55 late, 50 charged: 100)
            Assert.Equal(new[] { 100m, 100m }, perDay.Lines.Select(l => l.Amount).ToArray());
            Assert.Equal(new[] { tui, bus }, perDay.Lines.Select(l => l.FeeDueId).ToArray());
            await Svc(db).SaveRuleAsync(w.School, TaxDb.UserA, Save(type: "PERCENT", amount: null, percent: 10, grace: 0, cap: null, types: new[] { Type(db, w.School, "TUI"), Type(db, w.School, "BUS") }), default);
            var pct = await Preview(db, w.School, s, new LateFeePreviewRequest { Amount = 1200 });                            // Tuition 1000 + Bus 200: 10% of what is applied to each
            Assert.Equal(new[] { 100m, 20m }, pct.Lines.Select(l => l.Amount).ToArray());
            Assert.Equal(new[] { 1000m, 200m }, pct.Lines.Select(l => l.BaseAmount).ToArray());
        }

        [DbFact]
        public async Task The_preview_shows_the_tax_on_the_late_fee_only_when_the_school_setting_says_so_and_never_saves_anything()
        {
            using var db = new TaxDb();
            var (w, s, tui, _) = Student(db, "ZLI");
            var tax = db.Scalar<long>($"insert into tb_tax_types (school_id, tax_name, tax_code, rate_percent, is_active) values ('{w.School}', 'GST', 'GST', 18, true) returning tax_type_id");
            var req = new LateFeePreviewRequest { Allocations = new() { new() { FeeDueId = tui, Amount = 1000 } }, TaxTypeId = tax };
            await Svc(db).SaveRuleAsync(w.School, TaxDb.UserA, Save(type: "FLAT", amount: 50, grace: 5, cap: null, tax: false, types: new[] { Type(db, w.School, "TUI") }), default);
            var noTax = await Preview(db, w.School, s, req);
            Assert.Equal((50m, null, 0m), (noTax.Total, noTax.TaxName, noTax.TaxAmount));
            await Svc(db).SaveRuleAsync(w.School, TaxDb.UserA, Save(type: "FLAT", amount: 50, grace: 5, cap: null, tax: true, types: new[] { Type(db, w.School, "TUI") }), default);
            var fp = db.Scalar<string>($"select md5(coalesce(string_agg(x, '|' order by x), '')) from (select p::text x from tb_fee_payments p union all select l::text from tb_fee_payment_late_fees l union all select d::text from tb_student_fee_dues d where school_id='{w.School}') q");
            var withTax = await Preview(db, w.School, s, req);
            Assert.Equal(("GST", 9m), (withTax.TaxName, withTax.TaxAmount));                                                  // 18% of 50
            Assert.Equal(fp, db.Scalar<string>($"select md5(coalesce(string_agg(x, '|' order by x), '')) from (select p::text x from tb_fee_payments p union all select l::text from tb_fee_payment_late_fees l union all select d::text from tb_student_fee_dues d where school_id='{w.School}') q"));
            Assert.Equal(404, (await Svc(db).PreviewAsync(w.School, s, new LateFeePreviewRequest { Allocations = req.Allocations, TaxTypeId = 999999 }, default, Today)).StatusCode);
        }

        [DbFact]
        public async Task The_preview_refuses_what_a_payment_would_refuse_and_never_reaches_another_school()
        {
            using var db = new TaxDb();
            var (w, s, tui, _) = Student(db, "ZLJ");
            var (w2, s2, tui2, _) = Student(db, "ZLK");
            await Svc(db).SaveRuleAsync(w.School, TaxDb.UserA, Save(types: new[] { Type(db, w.School, "TUI") }), default);
            Assert.Equal(400, (await Svc(db).PreviewAsync(w.School, s, new LateFeePreviewRequest(), default, Today)).StatusCode);                                            // nothing to pay
            Assert.Equal(400, (await Svc(db).PreviewAsync(w.School, s, new LateFeePreviewRequest { Amount = 999999 }, default, Today)).StatusCode);                          // more than outstanding
            Assert.Equal(400, (await Svc(db).PreviewAsync(w.School, s, new LateFeePreviewRequest { Allocations = new() { new() { FeeDueId = tui, Amount = 5000 } } }, default, Today)).StatusCode);
            Assert.Equal(404, (await Svc(db).PreviewAsync(w.School, s2, new LateFeePreviewRequest { Amount = 100 }, default, Today)).StatusCode);                           // another school's student
            Assert.Equal(400, (await Svc(db).PreviewAsync(w.School, s, new LateFeePreviewRequest { Allocations = new() { new() { FeeDueId = tui2, Amount = 100 } } }, default, Today)).StatusCode);   // another school's due
            Assert.Equal(400, (await Svc(db).PreviewAsync(null, s, new LateFeePreviewRequest { Amount = 100 }, default, Today)).StatusCode);
        }

        [DbFact]
        public async Task An_unapplied_late_fee_never_counts_toward_the_promotion_dues_block()
        {
            using var db = new TaxDb();
            var w = P.Populate(db, "ZLL");
            var promotion = new PromotionService(new PromotionRepository(db.Ctx()), new StudentFeeBillingRepository(db.Ctx()), new FeeRepairRepository(db.Ctx()), new AdmissionRepository(db.Ctx()),
                new StudentFeeBillingService(new StudentFeeBillingRepository(db.Ctx()), new StudentFeeRepository(db.Ctx()), NullLogger<StudentFeeBillingService>.Instance), NullLogger<PromotionService>.Instance);
            var before = (await promotion.PreviewAsync(w.School, P.Req(w, date: new DateOnly(2026, 11, 25)), default)).Data!.Rows.Single(r => r.StudentId == w.School + "P4");
            await Svc(db).SaveRuleAsync(w.School, TaxDb.UserA, Save(type: "FLAT", amount: 999, grace: 0, cap: null, types: new[] { Type(db, w.School, "TUI") }), default);   // a heavy late fee on the very fee type that is overdue
            var after = (await promotion.PreviewAsync(w.School, P.Req(w, date: new DateOnly(2026, 11, 25)), default)).Data!.Rows.Single(r => r.StudentId == w.School + "P4");
            Assert.Equal((500m, PromotionRowStatus.Blocked), (before.Outstanding, before.Status));
            Assert.Equal((before.Outstanding, before.Status, before.NotYetDue), (after.Outstanding, after.Status, after.NotYetDue));   // the block is the dues only
        }

        // ------------------------------------------------------------------------------------------------ the schema, as the next step relies on it
        [DbFact]
        public void The_schema_keeps_the_total_the_late_fee_decision_and_the_per_due_rows_consistent()
        {
            using var db = new TaxDb();
            var (w, s, tui, _) = Student(db, "ZLM");
            string Try(string sql) { try { db.Exec(sql); return "ok"; } catch (Exception e) { return e.GetBaseException().Message.Split('\n')[0]; } }
            string Pay(string receipt, string cols, string vals) => $"insert into tb_fee_payments (student_id, school_id, payment_mode, receipt_number, {cols}) values ('{s}', '{w.School}', 'CASH', '{receipt}', {vals})";
            Assert.Equal("ok", Try(Pay("ZLM-0", "amount, fee_amount", "100, 100")));                                                                   // an ordinary payment still works
            Assert.Contains("chk_tb_fee_payments_total", Try(Pay("ZLM-1", "amount, fee_amount, late_fee_amount, late_fee_decision, late_fee_decided_by, late_fee_rule_snapshot", $"100, 100, 20, 'APPLIED', {TaxDb.UserA}, '{{}}'")));   // total must include the late fee
            Assert.Equal("ok", Try(Pay("ZLM-2", "amount, fee_amount, late_fee_amount, late_fee_decision, late_fee_decided_by, late_fee_rule_snapshot", $"120, 100, 20, 'APPLIED', {TaxDb.UserA}, '{{}}'")));
            Assert.Contains("chk_tb_fee_payments_late_shape", Try(Pay("ZLM-3", "amount, fee_amount, late_fee_amount", "120, 100, 20")));                  // a late fee needs a decision, who and the rule snapshot
            Assert.Contains("chk_tb_fee_payments_late_shape", Try(Pay("ZLM-4", "amount, fee_amount, late_fee_decision, late_fee_decided_by, late_fee_note", $"100, 100, 'WAIVED', {TaxDb.UserA}, 'no'")));   // a waiver needs 5 characters
            Assert.Equal("ok", Try(Pay("ZLM-5", "amount, fee_amount, late_fee_decision, late_fee_decided_by, late_fee_note", $"100, 100, 'WAIVED', {TaxDb.UserA}, 'poor family'")));
            Assert.Contains("chk_tb_fee_payments_late_decision", Try(Pay("ZLM-6", "amount, fee_amount, late_fee_decision", "100, 100, 'MAYBE'")));
            var p = db.Scalar<long>("select payment_id from tb_fee_payments where receipt_number='ZLM-2'");
            string Row(long pay, long due, string cols = "amount, days_late, days_charged, base_amount, computed_total, already_collected, rule_type, grace_days", string vals = "20, 10, 5, 100, 20, 0, 'FLAT', 5") =>
                $"insert into tb_fee_payment_late_fees (school_id, payment_id, fee_due_id, {cols}) values ('{w.School}', {pay}, {due}, {vals})";
            Assert.Equal("ok", Try(Row(p, tui)));
            Assert.Contains("uq_tb_fee_payment_late_fees_payment_due", Try(Row(p, tui)));                                                              // one row per (payment, due)
            Assert.Contains("chk_tb_fee_payment_late_fees_days", Try(Row(p, tui + 1, vals: "20, 3, 5, 100, 20, 0, 'FLAT', 5")));                       // days charged cannot exceed days late (a different due so the key is not the first failure)
            var (_, s2, tui2, _) = Student(db, "ZLN");
            Assert.Contains("fk_tb_fee_payment_late_fees_due", Try(Row(p, tui2)));                                                                     // another school's due can never be pointed at
            Assert.Contains("chk_tb_school_late_fee_rules_value", Try($"insert into tb_school_late_fee_rules (school_id, is_enabled, rule_type) values ('{w.School}', true, 'FLAT')"));
            Assert.Equal("ok", Try($"insert into tb_school_late_fee_rules (school_id, is_enabled) values ('{w.School}', false)"));
        }
    }
}
