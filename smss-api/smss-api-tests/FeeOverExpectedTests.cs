using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using smss_api_db_layer.context;
using smss_api_db_layer.repository;
using smss_api_service_layer.helper;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// The audit report counts an item as "over expected" when its dues add up to more than net x installments of the year. In the Nov 14 restore that is 5 items: four demo items that got a concession
    /// AFTER their past months were billed (the old sync then deleted the not-yet-due months), and one fully paid one-time fee that got a concession right after it was paid. This pins down that today's
    /// sync never repeats that: a concession that arrives after billing leaves every due in place, rewrites only the unpaid FUTURE months at the discounted amount, and never touches the past or a payment.
    /// </summary>
    public class FeeOverExpectedTests
    {
        private const long Tuition = 12, StaffWardHalf = 6;   // Staff Ward Concession: 50 % on Tuition Fee, school GHS261103002

        [DbFact]
        public async Task AConcessionThatArrivesAfterBilling_NeverDeletesADue_AndOnlyDiscountsTheUnpaidFutureMonths()
        {
            using var db = new TaxDb();
            var today = SchoolClock.Today();
            var start = new DateOnly(today.Month >= 4 ? today.Year : today.Year - 1, 4, 1);
            db.Exec($"update tb_academic_years set start_date='{start:yyyy-MM-dd}', end_date='{start.AddYears(1).AddDays(-1):yyyy-MM-dd}', is_current=true where academic_year_id={TaxDb.YearA}");
            const string student = "GHS001ST002";
            var item = db.Scalar<long>($"select student_fee_item_id from tb_student_fee_items where student_id='{student}' and fee_type_id={Tuition}");

            List<(long Id, DateOnly Date, decimal Amount)> Dues()
            {
                var l = new List<(long, DateOnly, decimal)>();
                using var c = new NpgsqlConnection(db.Conn); c.Open();
                using var cmd = new NpgsqlCommand($"select fee_due_id, due_date, amount_due from tb_student_fee_dues where student_fee_item_id={item} order by due_date", c);
                using var r = cmd.ExecuteReader();
                while (r.Read()) l.Add((r.GetInt64(0), DateOnly.FromDateTime(r.GetDateTime(1)), r.GetDecimal(2)));
                return l;
            }

            var before = Dues();
            Assert.Equal(12, before.Count);
            Assert.All(before, d => Assert.Equal(1800m, d.Amount));
            Assert.Contains(before, d => d.Date <= today);       // some months are already billed ...
            Assert.Contains(before, d => d.Date > today);        // ... and some are not due yet

            // the concession arrives now, after the past months were billed
            db.Exec($"insert into tb_student_fee_concessions (student_id, concession_id, approved_by, academic_year_id) values ('{student}', {StaffWardHalf}, {TaxDb.UserA}, (select max(academic_year_id) from tb_student_enrollments where student_id='{student}'))");
            using var c2 = db.Ctx();
            var billing = new StudentFeeBillingService(new StudentFeeBillingRepository(c2), new StudentFeeRepository(c2), NullLogger<StudentFeeBillingService>.Instance);
            await billing.SyncAsync(TaxDb.SchoolA, student, TaxDb.YearA, null, default);

            var after = Dues();
            Assert.Equal(before.Count, after.Count);                                                     // nothing deleted (the old code deleted the not-yet-due months here)
            Assert.Equal(before.Select(d => d.Id), after.Select(d => d.Id));                              // the same rows
            foreach (var (b, a) in before.Zip(after))
            {
                if (b.Date <= today) Assert.Equal(1800m, a.Amount);                                       // billed months are never rewritten
                else Assert.Equal(900m, a.Amount);                                                        // unpaid future months carry the 50 % concession
            }
            Assert.Equal(900m, db.Scalar<decimal>($"select net_amount from tb_student_fee_items where student_fee_item_id={item}"));
        }

        [DbFact]
        public async Task AConcessionAddedAfterAFullPayment_LeavesThePaidDueAlone()
        {
            using var db = new TaxDb();
            const string student = "GHS001ST003";
            var admission = db.Scalar<long>($"select student_fee_item_id from tb_student_fee_items where student_id='{student}' and fee_type_id=13");   // Admission Fee, one time
            var due = db.Scalar<long>($"select fee_due_id from tb_student_fee_dues where student_fee_item_id={admission}");
            db.Exec($"update tb_student_fee_dues set amount_paid = amount_due where fee_due_id={due}");                                        // paid in full
            var before = db.Scalar<string>($"select amount_due||'|'||amount_paid||'|'||due_date from tb_student_fee_dues where fee_due_id={due}");
            db.Exec($"insert into tb_fee_concessions (school_id, concession_name, concession_code, concession_type, value, applicable_fee_type_id, isactive) values ('{TaxDb.SchoolA}', 'Late 10%', 'LATE10', 'PERCENTAGE', 10, 13, true)");
            var concession = db.Scalar<long>("select concession_id from tb_fee_concessions where concession_code='LATE10'");
            db.Exec($"insert into tb_student_fee_concessions (student_id, concession_id, approved_by, academic_year_id) values ('{student}', {concession}, {TaxDb.UserA}, (select max(academic_year_id) from tb_student_enrollments where student_id='{student}'))");

            using var c2 = db.Ctx();
            var billing = new StudentFeeBillingService(new StudentFeeBillingRepository(c2), new StudentFeeRepository(c2), NullLogger<StudentFeeBillingService>.Instance);
            await billing.SyncAsync(TaxDb.SchoolA, student, TaxDb.YearA, null, default);

            Assert.Equal(before, db.Scalar<string>($"select amount_due||'|'||amount_paid||'|'||due_date from tb_student_fee_dues where fee_due_id={due}"));   // the paid due is exactly as it was
            Assert.Equal(1, db.Scalar<long>($"select count(*) from tb_student_fee_dues where student_fee_item_id={admission}"));                          // and not billed a second time
        }
    }
}
