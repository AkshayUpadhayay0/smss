using Microsoft.Extensions.Logging.Abstractions;
using smss_api_db_layer.repository;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// "Outstanding from earlier years", step 1 (API): the dues read of a student also carries what is still owed from years that START BEFORE the year shown, per year, with those installments.
    /// Derived on every read; nothing is carried forward, moved or created. [DbFact]: needs SMSS_TEST_PG (no migration).
    /// </summary>
    public class EarlierYearDuesS1Tests
    {
        private static StudentFeeBillingService Svc(TaxDb db)
        {
            var c = db.Ctx();
            return new StudentFeeBillingService(new StudentFeeBillingRepository(c), new StudentFeeRepository(c), NullLogger<StudentFeeBillingService>.Instance, new EarlierYearDuesRepository(c));
        }

        private static string Rows(TaxDb db, string school) => db.Scalar<string>($@"select md5(coalesce(string_agg(d::text, '|' order by d.fee_due_id), '')) from tb_student_fee_dues d where d.school_id='{school}'")
            + "/" + db.Scalar<string>($"select count(*) from tb_student_fee_items where school_id='{school}'");

        [DbFact]
        public async Task A_student_promoted_with_override_sees_the_old_years_unpaid_amount_beside_the_current_year_and_nothing_is_moved()
        {
            using var db = new TaxDb(); var w = EarlierYearsKit.Build(db, "ZEA");
            var before = Rows(db, "ZEA");
            var r = await Svc(db).GetDuesAsync("ZEA", w.P, default);
            Assert.True(r.Status, r.Message);
            Assert.Equal("2026-27", r.Data!.AcademicYearName);
            Assert.Equal(1000, r.Data.Outstanding);                                   // the year's own figures are untouched by the earlier year
            Assert.Equal(300, r.Data.OutstandingEarlierYears);
            var y = Assert.Single(r.Data.EarlierYears);
            Assert.Equal(("2025-26", 300m), (y.AcademicYearName, y.Outstanding));
            var due = Assert.Single(r.Data.EarlierDues);                              // only the UNPAID installment of 2025-26 (the paid one is not listed)
            Assert.Equal((300m, "2025-26"), (due.Outstanding, due.AcademicYearName));
            Assert.DoesNotContain(r.Data.Dues, d => d.AcademicYearName != null);     // the current year's list never mixes earlier rows in
            Assert.Equal(before, Rows(db, "ZEA"));                                    // no due row moved, created or changed
        }

        [DbFact]
        public async Task A_repeated_student_and_a_student_with_nothing_earlier_are_each_right()
        {
            using var db = new TaxDb(); var w = EarlierYearsKit.Build(db, "ZEB");
            var repeated = (await Svc(db).GetDuesAsync("ZEB", w.R, default)).Data!;
            Assert.Equal(500, repeated.OutstandingEarlierYears);                       // the same class again: the old year's dues are still the old year's
            Assert.Equal(1000, repeated.Outstanding);
            var fresh = (await Svc(db).GetDuesAsync("ZEB", w.N, default)).Data!;
            Assert.Empty(fresh.EarlierYears); Assert.Empty(fresh.EarlierDues); Assert.Equal(0, fresh.OutstandingEarlierYears);
        }

        [DbFact]
        public async Task A_left_student_keeps_both_figures_and_paying_the_old_year_makes_it_disappear()
        {
            using var db = new TaxDb(); var w = EarlierYearsKit.Build(db, "ZEC");
            var left = (await Svc(db).GetDuesAsync("ZEC", w.L, default)).Data!;
            Assert.Equal((400m, 200m), (left.Outstanding, left.OutstandingEarlierYears));
            db.Exec($"update tb_student_fee_dues set amount_paid = amount_due where student_id='{w.L}' and due_date = date '2025-08-10'");
            var paid = (await Svc(db).GetDuesAsync("ZEC", w.L, default)).Data!;
            Assert.Empty(paid.EarlierYears); Assert.Equal(0, paid.OutstandingEarlierYears);
        }

        [DbFact]
        public async Task Another_schools_student_id_finds_nothing_and_the_repository_only_reads_the_callers_school()
        {
            using var db = new TaxDb(); var d = EarlierYearsKit.Build(db, "ZED", "D"); var e = EarlierYearsKit.Build(db, "ZEE", "E");
            var svc = Svc(db);
            Assert.Equal(404, (await svc.GetDuesAsync("ZEE", d.P, default)).StatusCode);        // another school's student is not found
            var repo = new EarlierYearDuesRepository(db.Ctx());
            var ref1 = new DateOnly(2026, 4, 1);
            Assert.Empty(await repo.GetOutstandingAsync("ZEE", new[] { d.P, d.R }, new[] { ref1, ref1 }, new DateOnly(2026, 12, 1), default));   // school D's ids asked through school E: nothing
            var rows = await repo.GetOutstandingAsync("ZEE", new[] { e.P, e.R }, new[] { ref1, ref1 }, new DateOnly(2026, 12, 1), default);
            Assert.Equal(new[] { 300m, 500m }, rows.OrderBy(x => x.StudentId == e.P ? 0 : 1).Select(x => x.Outstanding).ToArray());
            Assert.Empty(await repo.GetDuesAsync("ZEE", d.P, ref1, default));
        }

        [DbFact]
        public async Task A_payment_in_auto_mode_pays_the_earlier_year_first_and_an_explicit_choice_can_pay_only_this_year()
        {
            using var db = new TaxDb(); var w = EarlierYearsKit.Build(db, "ZEF");
            var auto = await db.Pay(w.P, 700, school: "ZEF");                             // oldest installment first, across years
            Assert.True(auto.Status, auto.Message);
            Assert.Equal(300, db.Scalar<decimal>($"select amount_paid from tb_student_fee_dues where student_id='{w.P}' and due_date = date '2025-06-10'"));
            Assert.Equal(400, db.Scalar<decimal>($"select amount_paid from tb_student_fee_dues where student_id='{w.P}' and due_date = date '2026-05-10'"));
            var after = (await Svc(db).GetDuesAsync("ZEF", w.P, default)).Data!;
            Assert.Equal((600m, 0m), (after.Outstanding, after.OutstandingEarlierYears));

            // not forced: paying only this year's installment leaves the earlier year untouched
            var thisYear = db.Scalar<long>($"select fee_due_id from tb_student_fee_dues where student_id='{w.R}' and due_date = date '2026-05-10'");
            var explicitPay = await db.Pay(w.R, 100, allocations: new List<smss_api_service_layer.dto.PaymentAllocationRequest> { new() { FeeDueId = thisYear, Amount = 100 } }, school: "ZEF");
            Assert.True(explicitPay.Status, explicitPay.Message);
            var repeated = (await Svc(db).GetDuesAsync("ZEF", w.R, default)).Data!;
            Assert.Equal((900m, 500m), (repeated.Outstanding, repeated.OutstandingEarlierYears));
        }
    }
}
