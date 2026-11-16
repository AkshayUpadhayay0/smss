using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using smss_api.Controllers;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// A FULL WAIVER: the concession covers the whole amount, so the cash total is 0. Allowed only when a concession is given (with its reason); a payment of 0 without one, and a negative
    /// amount, stay refused. Scratch PostgreSQL (see TaxTestKit) for the [DbFact]s. Student GHS001ST001: 4060 Library 500, 4061 Exam 800, 4062 Annual 3000, 4069 Tuition 1800.
    /// </summary>
    public class FullWaiverTests
    {
        private const string Student = "GHS001ST001";
        private const long Library = 4060, Tuition = 4069;

        private static long NewConcession(TaxDb db, string school, string name, string code, string type, decimal value, long? feeTypeId = null) =>
            db.Scalar<long>($"insert into tb_fee_concessions (school_id, concession_name, concession_code, concession_type, value, applicable_fee_type_id, isactive) " +
                            $"values ('{school}', '{name}', '{code}', '{type}', {value.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {(feeTypeId?.ToString() ?? "null")}, true) returning concession_id");

        private static Task<ApiResponse<FeeReceiptDto>> Pay(TaxDb db, decimal gross, long? concessionId, string? reason, long due, long? taxTypeId = null, string student = Student) =>
            db.Payments().RecordPaymentAsync(TaxDb.SchoolA, student, TaxDb.UserA, new RecordPaymentRequest
            { Amount = gross, PaymentMode = "Cash", ConcessionId = concessionId, ConcessionReason = reason, TaxTypeId = taxTypeId, Allocations = new() { new PaymentAllocationRequest { FeeDueId = due, Amount = gross } } }, default);

        [DbFact]
        public async Task Percentage100_IsAFullWaiver_TotalZero_NormalReceiptNumber_DueCleared()
        {
            using var db = new TaxDb();
            var c = NewConcession(db, TaxDb.SchoolA, "Full waiver", "WAIVE", "PERCENTAGE", 100);
            var r = await Pay(db, 1800, c, "Fee waived by the principal", Tuition);
            Assert.Equal(201, r.StatusCode);
            var rc = r.Data!;
            Assert.Equal((1800m, 1800m, 0m, 0m, 0m, 0m), (rc.GrossAmount, rc.ConcessionAmount, rc.NetFeeAmount, rc.TaxAmount, rc.TotalAmount, rc.Amount));
            Assert.Matches(@"^RCP-\d{4}-\d{4}$", rc.ReceiptNumber);
            Assert.Equal("Fee waived by the principal", rc.ConcessionReason);
            Assert.Equal(1800m, rc.Lines.Single().ConcessionAmount);
            // the invariant and the due
            Assert.Equal(1800m, db.Scalar<decimal>($"select sum(allocated_amount) from tb_fee_payment_allocations where payment_id={rc.PaymentId}"));
            Assert.Equal(1800m, db.Scalar<decimal>($"select fee_amount + concession_amount from tb_fee_payments where payment_id={rc.PaymentId}"));
            Assert.Equal(0m, db.Scalar<decimal>($"select amount from tb_fee_payments where payment_id={rc.PaymentId}"));
            var d = db.Dues(Student).First(x => x.Id == Tuition);
            Assert.Equal(d.Due, d.Paid);
            Assert.Equal("Paid", FeeSchedule.Status(d.Due, d.Paid, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 16)));
            // the next receipt number follows it, as for any payment
            var next = (await Pay(db, 100, null, null, Library)).Data!;
            Assert.Equal(int.Parse(rc.ReceiptNumber[^4..]) + 1, int.Parse(next.ReceiptNumber[^4..]));
        }

        [DbFact]
        public async Task AFixedConcessionEqualToTheWholeBase_OrLargerAndCapped_IsAFullWaiver()
        {
            using var db = new TaxDb();
            var exact = NewConcession(db, TaxDb.SchoolA, "Rs 500", "F500", "FIXED", 500);
            var big = NewConcession(db, TaxDb.SchoolA, "Rs 5000", "F5000", "FIXED", 5000);
            var a = (await Pay(db, 500, exact, "scholarship", Library)).Data!;
            Assert.Equal((500m, 0m, 0m), (a.ConcessionAmount, a.NetFeeAmount, a.TotalAmount));
            var b = (await Pay(db, 800, big, "scholarship", 4061)).Data!;           // 5000 is capped at the 800 allocated
            Assert.Equal((800m, 0m, 0m), (b.ConcessionAmount, b.NetFeeAmount, b.TotalAmount));
        }

        [DbFact]
        public async Task AWaiverWithATaxChosen_HasZeroTax_ButStillRecordsWhichTaxWasChosen()
        {
            using var db = new TaxDb();
            var c = NewConcession(db, TaxDb.SchoolA, "Full waiver", "WAIVE", "PERCENTAGE", 100);
            var gst = await db.NewTax(TaxDb.SchoolA, "GST 18%", "GST18", 18);
            var rc = (await Pay(db, 1800, c, "waived", Tuition, gst.TaxTypeId)).Data!;
            Assert.Equal((0m, 0m, "GST 18%"), (rc.TaxAmount, rc.TotalAmount, rc.TaxName));
            Assert.Equal(0m, db.Scalar<decimal>($"select tax_amount from tb_fee_payments where payment_id={rc.PaymentId}"));
        }

        [DbFact]
        public async Task TheReasonIsStillRequired_AndZeroOrNegativeWithoutAConcessionIsStillRefused()
        {
            using var db = new TaxDb();
            var c = NewConcession(db, TaxDb.SchoolA, "Full waiver", "WAIVE", "PERCENTAGE", 100);
            var before = db.Scalar<long>("select count(*) from tb_fee_payments");
            Assert.Equal(400, (await Pay(db, 1800, c, "  ", Tuition)).StatusCode);
            Assert.Equal(400, (await Pay(db, 0, null, null, Tuition)).StatusCode);
            Assert.Equal(400, (await Pay(db, -5, null, null, Tuition)).StatusCode);
            Assert.Equal(400, (await Pay(db, 0, c, "waived", Tuition)).StatusCode);   // there is nothing to apply the concession to
            Assert.Equal(before, db.Scalar<long>("select count(*) from tb_fee_payments"));
            // and the database itself says the same: amount 0 without a concession can't be stored, a negative amount can't either
            Assert.ThrowsAny<PostgresException>(() => db.Exec("update tb_fee_payments set amount = 0, fee_amount = 0 where payment_id = (select min(payment_id) from tb_fee_payments)"));
            Assert.ThrowsAny<PostgresException>(() => db.Exec("update tb_fee_payments set amount = -1, fee_amount = -1 where payment_id = (select min(payment_id) from tb_fee_payments)"));
        }

        [DbFact]
        public async Task VoidingAWaiver_RestoresTheDue_AndTheVoidedReceiptKeepsItsLines()
        {
            using var db = new TaxDb();
            var c = NewConcession(db, TaxDb.SchoolA, "Full waiver", "WAIVE", "PERCENTAGE", 100);
            var before = db.Dues(Student);
            var rc = (await Pay(db, 1800, c, "waived", Tuition)).Data!;
            var v = await db.Payments().VoidPaymentAsync(TaxDb.SchoolA, rc.PaymentId, TaxDb.UserA, new VoidPaymentRequest { VoidReason = "entered for the wrong student" }, default);
            Assert.Equal(200, v.StatusCode);
            Assert.Equal((1800m, 0m, "Voided"), (v.Data!.ConcessionAmount, v.Data.TotalAmount, v.Data.Status));
            Assert.Equal(before.Select(d => d.Paid), db.Dues(Student).Select(d => d.Paid));                 // the waived amount comes back as outstanding
            var receipt = (await db.Payments().GetReceiptAsync(TaxDb.SchoolA, rc.PaymentId, default)).Data!;
            Assert.Equal(("Voided", 1800m, 0m), (receipt.Status, receipt.ConcessionAmount, receipt.TotalAmount));
            Assert.Equal(404, (await db.Payments().VoidPaymentAsync(TaxDb.SchoolA, rc.PaymentId, TaxDb.UserA, new VoidPaymentRequest { VoidReason = "again" }, default)).StatusCode);
        }

        [DbFact]
        public async Task Dashboard_CollectedStaysZeroForAWaiver_ConcessionGivenIsRight_AndTheIdentityHolds()
        {
            using var db = new TaxDb();
            var c = NewConcession(db, TaxDb.SchoolA, "Full waiver", "WAIVE", "PERCENTAGE", 100);
            await Pay(db, 1800, c, "waived", Tuition);
            var d1 = (await db.Reports().GetDashboardAsync(TaxDb.SchoolA, TaxDb.YearA, null, null, 7, true, default)).Data!;
            Assert.Equal((0m, 1800m), (d1.TotalCollected, d1.ConcessionGiven));
            Assert.Equal(d1.TotalDue, d1.TotalCollected + d1.ConcessionGiven + d1.TotalOutstanding);
            Assert.Equal(0m, d1.CollectionByMonth.Sum(m => m.Amount));
            Assert.Equal(1800m, d1.ConcessionByType.Single().ConcessionAmount);

            Assert.Equal(201, (await db.Pay("GHS001ST002", 200)).StatusCode);   // a plain 200 for another student (oldest installment first)
            var d2 = (await db.Reports().GetDashboardAsync(TaxDb.SchoolA, TaxDb.YearA, null, null, 7, true, default)).Data!;
            Assert.Equal((200m, 1800m), (d2.TotalCollected, d2.ConcessionGiven));
            Assert.Equal(d2.TotalDue, d2.TotalCollected + d2.ConcessionGiven + d2.TotalOutstanding);
            Assert.Equal(db.Scalar<decimal>($"select coalesce(sum(a.concession_amount),0) from tb_fee_payment_allocations a join tb_fee_payments p on p.payment_id=a.payment_id where p.school_id='{TaxDb.SchoolA}' and p.status='Active'"), d2.ConcessionGiven);
        }

        [DbFact]
        public async Task ConcessionsGivenReport_ListsTheWaiverWithItsReason()
        {
            using var db = new TaxDb();
            var c = NewConcession(db, TaxDb.SchoolA, "Full waiver", "WAIVE", "PERCENTAGE", 100);
            var rc = (await Pay(db, 1800, c, "waived by the principal", Tuition)).Data!;
            var list = (await db.Reports().GetConcessionsGivenAsync(TaxDb.SchoolA, null, null, 1, 25, default)).Data!;
            var row = Assert.Single(list.Items);
            Assert.Equal((rc.PaymentId, 1800m, "waived by the principal", "Active"), (row.PaymentId, row.ConcessionAmount, row.Reason, row.Status));
            Assert.Equal(1800m, list.TotalAmount);
        }

        [DbFact]
        public async Task StudentMyFees_ShowsTheWaiverWithATotalOfZero_NeverTheReason_AndTheInstallmentReadsPaid()
        {
            using var db = new TaxDb();
            var c = NewConcession(db, TaxDb.SchoolA, "Full waiver", "WAIVE", "PERCENTAGE", 100);
            await Pay(db, 1800, c, "private staff note", Tuition);
            var ctx = db.Ctx();
            var my = new StudentMyFeesService(new StudentRepository(ctx), new StudentFeeBillingService(new StudentFeeBillingRepository(db.Ctx()), new StudentFeeRepository(db.Ctx()), NullLogger<StudentFeeBillingService>.Instance),
                db.Payments(), NullLogger<StudentMyFeesService>.Instance);
            var userId = db.Scalar<long>($"select user_id from tb_users where school_id='{TaxDb.SchoolA}' and org_user_id='{Student}'");
            var fees = (await my.GetMyFeesAsync(TaxDb.SchoolA, userId, default)).Data!;
            var p = Assert.Single(fees.Payments);
            Assert.Equal((1800m, 1800m, 0m, 0m), (p.GrossAmount, p.ConcessionAmount, p.NetFeeAmount, p.Amount));
            Assert.Equal("Full waiver", p.ConcessionName);
            Assert.DoesNotContain("private staff note", System.Text.Json.JsonSerializer.Serialize(fees));
            Assert.Equal("Paid", fees.Dues.Single(x => x.AmountDue == 1800m && x.DueDate == new DateOnly(2026, 11, 10)).Status);
            // the student's own receipt: the waiver, no reason
            var own = (await db.Payments().GetOwnReceiptAsync(TaxDb.SchoolA, userId, db.Scalar<long>("select max(payment_id) from tb_fee_payments"), default)).Data!;
            Assert.Equal((0m, 1800m), (own.TotalAmount, own.ConcessionAmount));
            Assert.Null(own.ConcessionReason);
        }
    }
}
