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
    /// Stage 5a-1: late fee settings (the school rule, the fee type flags) and the schema. Nothing is charged yet. These tests need Docs/smss-st-5a1-RUN-ME.sql applied to the template (SMSS_TEST_TEMPLATE). Payments and late-fee rows are throwaway rows on the scratch database only.
    /// </summary>
    public class LateFee5aTests
    {
        private static readonly DateOnly Today = new(2026, 11, 25);
        private static LateFeeService Svc(TaxDb db) => new(new LateFeeRepository(db.Ctx()), NullLogger<LateFeeService>.Instance);
        private static LateFeeRuleValues Rule(string type, decimal? amount = null, decimal? percent = null, int grace = 0, decimal? cap = null) => new(type, amount, percent, grace, cap);

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
        public async Task The_School_Admin_Accountant_and_Principal_read_the_rule_only_the_School_Admin_changes_it(string role)
        {
            var svc = Fake.Of<ILateFeeService>();
            var c = Fake.WithUser(new LateFeeController(svc), Fake.User("S", 5, role));
            var read = Fake.Code(await c.GetRule(default));
            var write = Fake.Code(await c.SaveRule(Save(), default));
            Assert.Equal(role is not ("School Admin" or "Accountant" or "Principal"), read == 403);
            Assert.Equal(role != "School Admin", write == 403);
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
