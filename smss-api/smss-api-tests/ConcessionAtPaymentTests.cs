using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
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
    /// A concession chosen when collecting a payment. The first half needs no database (arithmetic, authorization matrix, the retired assignment endpoint); the [DbFact] half runs
    /// on a scratch PostgreSQL (see TaxTestKit). Student GHS001ST001 of TaxDb.SchoolA has these installments: 4060 Library(16) 500, 4061 Exam(15) 800, 4062 Annual(14) 3000,
    /// 4063 Admission(13) 4000, 4069 Tuition(12) 1800 (all due 3 Nov / 10 Nov 2026), nothing paid.
    /// </summary>
    public class ConcessionAtPaymentTests
    {
        private const string Student = "GHS001ST001";
        private const long Library = 4060, Exam = 4061, Annual = 4062, Tuition = 4069;
        private const long ExamType = 15;
        private static readonly string[] Managers = { "School Admin", "Accountant" };
        private static readonly string[] Viewers = { "School Admin", "Accountant", "Principal" };
        private static readonly string[] NotManagers = { "Principal", "Teacher", "Student", "Guardian", "Admission Staff", "Super Admin", "Driver" };
        private static readonly string[] NotViewers = { "Teacher", "Student", "Guardian", "Admission Staff", "Super Admin", "Driver" };

        // ============================================================== arithmetic (no database)
        [Theory]
        [InlineData("PERCENTAGE", 10, 1000, 100.00)]
        [InlineData("PERCENTAGE", 10, 333.33, 33.33)]     // 33.333 -> 33.33
        [InlineData("PERCENTAGE", 10, 0.05, 0.01)]        // 0.005 -> 0.01 (half up, like the tax)
        [InlineData("PERCENTAGE", 12.5, 100.04, 12.51)]   // 12.505 -> 12.51
        [InlineData("PERCENTAGE", 100, 250.75, 250.75)]
        [InlineData("PERCENTAGE", 0, 1000, 0)]
        [InlineData("FIXED", 100, 1000, 100.00)]
        [InlineData("FIXED", 100, 60, 60.00)]             // never more than the base
        [InlineData("FIXED", 2000, 1500, 1500.00)]
        public void DiscountOn_RoundsHalfUp_AndNeverExceedsTheBase(string type, double value, double baseAmount, double expected) =>
            Assert.Equal((decimal)expected, ConcessionMath.DiscountOn(type, (decimal)value, (decimal)baseAmount));

        [Fact]
        public void Spread_LargestRemainderInCents_SumsExactlyAndNeverExceedsAnAllocation()
        {
            Assert.Equal(new[] { 33.33m, 33.33m, 33.34m }, ConcessionMath.Spread(100m, new[] { 333.33m, 333.33m, 333.34m }));
            Assert.Equal(new[] { 50m, 50m }, ConcessionMath.Spread(100m, new[] { 500m, 500m }));
            Assert.Equal(new[] { 0.01m, 0m }, ConcessionMath.Spread(0.01m, new[] { 0.05m, 0.05m }));   // a tie goes to the earlier (older) one
            Assert.Equal(new[] { 500m, 250m }, ConcessionMath.Spread(750m, new[] { 500m, 250m }));       // the whole base: every allocation fully discounted
            Assert.Empty(ConcessionMath.Spread(10m, Array.Empty<decimal>()));

            var rng = new Random(20261116);
            for (var run = 0; run < 3000; run++)
            {
                var n = rng.Next(1, 9);
                var allocs = Enumerable.Range(0, n).Select(_ => rng.Next(1, 500_000) / 100m).ToArray();
                var total = allocs.Sum();
                var discount = Math.Round(total * (decimal)rng.NextDouble(), 2);
                var parts = ConcessionMath.Spread(discount, allocs);
                Assert.Equal(discount, parts.Sum());
                for (var i = 0; i < n; i++) { Assert.True(parts[i] >= 0, "negative part"); Assert.True(parts[i] <= allocs[i], $"part {parts[i]} above allocation {allocs[i]}"); }
                Assert.Equal(parts, ConcessionMath.Spread(discount, allocs));   // deterministic
            }
        }

        // ============================================================== request shape: the client sends no amounts
        [Fact]
        public void TheRequest_HasNoDiscountTaxOrTotalField_SoAClientSentAmountIsNeverRead()
        {
            var names = typeof(RecordPaymentRequest).GetProperties().Select(p => p.Name).ToList();
            Assert.Contains("ConcessionId", names);
            Assert.Contains("ConcessionReason", names);
            foreach (var forbidden in new[] { "ConcessionAmount", "DiscountAmount", "Discount", "TaxAmount", "TotalAmount", "FeeAmount", "NetFeeAmount", "GrossAmount" })
                Assert.DoesNotContain(forbidden, names);
            // and a JSON body that tries anyway simply drops those fields
            var req = System.Text.Json.JsonSerializer.Deserialize<RecordPaymentRequest>(
                "{\"amount\":1000,\"paymentMode\":\"Cash\",\"concessionId\":3,\"concessionReason\":\"x\",\"concessionAmount\":999,\"taxAmount\":999,\"totalAmount\":1,\"discount\":999}",
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Assert.Equal(1000m, req.Amount); Assert.Equal(3, req.ConcessionId);
        }

        // ============================================================== authorization matrix (no database)
        private static async Task<int> Choices(params string[] roles)
        {
            var c = Fake.WithUser(new FeePaymentController(Fake.Of<IFeePaymentService>(new() { ["GetConcessionChoicesAsync"] = Fake.Ok(new List<ConcessionChoiceDto>()) })), Fake.User("SCH1", 7, roles));
            return Fake.Code(await c.GetConcessions(default));
        }

        [Fact]
        public async Task ConcessionChoices_AreForSchoolAdminAndAccountant_EveryoneElse403()
        {
            foreach (var r in Managers) Assert.Equal(200, await Choices(r));
            foreach (var r in NotManagers) Assert.True(403 == await Choices(r), $"{r} must be refused");
            Assert.Equal(403, await Choices());
        }

        private static async Task<int> Given(params string[] roles)
        {
            var c = Fake.WithUser(new FeeReportsController(Fake.Of<IFeeReportService>(new() { ["GetConcessionsGivenAsync"] = Fake.Ok(new ConcessionsGivenDto()) })), Fake.User("SCH1", 7, roles));
            return Fake.Code(await c.GetConcessionsGiven(null, null, 1, 25, default));
        }

        [Fact]
        public async Task ConcessionsGivenReport_IsSchoolAdminAccountantPrincipal_EveryoneElse403()
        {
            foreach (var r in Viewers) Assert.Equal(200, await Given(r));
            foreach (var r in NotViewers) Assert.True(403 == await Given(r), $"{r} must be refused");
            Assert.Equal(403, await Given());
        }

        [Fact]
        public async Task RecordingStaysSchoolAdminAndAccountant_AndAnyConcessionRequestFromAnotherRoleIs403()
        {
            var svc = Fake.Of<IFeePaymentService>();
            foreach (var r in NotManagers)
            {
                var c = Fake.WithUser(new FeePaymentController(svc), Fake.User("SCH1", 7, r));
                var res = await c.RecordPayment(Student, new RecordPaymentRequest { Amount = 100, PaymentMode = "Cash", ConcessionId = 1, ConcessionReason = "x" }, default);
                Assert.True(403 == Fake.Code(res), $"{r} must be refused");
            }
            Assert.Empty(Fake.ProxyOf(svc).Calls);
        }

        [Fact]
        public async Task ConcessionMasters_StayWritableBySchoolAdminOnly()
        {
            var svc = Fake.Of<IFeeConcessionService>();
            foreach (var r in new[] { "Accountant", "Principal", "Teacher", "Student", "Guardian", "Admission Staff", "Super Admin" })
            {
                var c = Fake.WithUser(new FeeConcessionController(svc), Fake.User("SCH1", 7, r));
                Assert.True(403 == Fake.Code(await c.Create(new CreateFeeConcessionRequest())), $"create: {r}");
                Assert.True(403 == Fake.Code(await c.Update(1, new UpdateFeeConcessionRequest())), $"update: {r}");
                Assert.True(403 == Fake.Code(await c.ToggleStatus(1)), $"toggle: {r}");
            }
            Assert.Empty(Fake.ProxyOf(svc).Calls);
        }

        [Fact]
        public void NewEndpoints_RequireALogin_NoAnonymous()
        {
            foreach (var t in new[] { typeof(FeePaymentController), typeof(FeeReportsController), typeof(StudentFeeController) })
            {
                Assert.NotEmpty(t.GetCustomAttributes<AuthorizeAttribute>());
                Assert.Empty(t.GetCustomAttributes<AllowAnonymousAttribute>());
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) Assert.Empty(m.GetCustomAttributes<AllowAnonymousAttribute>());
            }
        }

        // ============================================================== assigning a concession to a student is retired (no database)
        [Fact]
        public async Task AssignEndpoint_Answers409ForSchoolAdmin_403ForEveryoneElse_AndRemoveStaysSchoolAdminOnly()
        {
            var real = new StudentFeeService(Fake.Of<smss_api_db_layer.@interface.IStudentFeeRepository>(), Fake.Of<IStudentFeeBillingService>(), NullLogger<StudentFeeService>.Instance);
            var admin = Fake.WithUser(new StudentFeeController(real, Fake.Of<IStudentFeeBillingService>()), Fake.User("SCH1", 7, "School Admin"));
            var res = await admin.ApplyConcession(Student, new ApplyConcessionRequest { ConcessionId = 1 }, default);
            Assert.Equal(409, Fake.Code(res));
            Assert.Contains("Concessions are now chosen when collecting a payment.", System.Text.Json.JsonSerializer.Serialize(((ObjectResult)res).Value));

            foreach (var r in new[] { "Accountant", "Principal", "Teacher", "Student", "Guardian", "Admission Staff", "Super Admin" })
            {
                var c = Fake.WithUser(new StudentFeeController(real, Fake.Of<IStudentFeeBillingService>()), Fake.User("SCH1", 7, r));
                Assert.True(403 == Fake.Code(await c.ApplyConcession(Student, new ApplyConcessionRequest { ConcessionId = 1 }, default)), $"assign: {r}");
                Assert.True(403 == Fake.Code(await c.RemoveConcession(Student, 1, default)), $"remove: {r}");
            }
        }

        // ============================================================== student My Fees never shows the reason (no database)
        [Fact]
        public async Task MyFees_ShowsGrossConcessionNetTaxTotal_ButNeverTheReason()
        {
            Assert.DoesNotContain(typeof(MyFeePaymentDto).GetProperties(), p => p.Name.Contains("Reason") || p.Name.Contains("AppliedBy"));
            var students = Fake.Of<smss_api_db_layer.@interface.IStudentRepository>(new()
            {
                ["GetUserLoginIdAsync"] = Student, ["GetStudentAsync"] = new smss_api_db_layer.entity.TbStudent { StudentId = Student },
            });
            var billing = Fake.Of<IStudentFeeBillingService>(new()
            {
                ["GetItemsAsync"] = Fake.Ok(new StudentFeeItemsDto { StudentId = Student }), ["GetDuesAsync"] = Fake.Ok(new StudentFeeDuesDto { StudentId = Student }),
            });
            var pays = Fake.Of<IFeePaymentService>(new()
            {
                ["GetPaymentsAsync"] = Fake.Ok(new List<PaymentSummaryDto>
                {
                    new() { PaymentId = 1, ReceiptNumber = "RCP-1", PaymentDate = DateTime.UtcNow, PaymentMode = "Cash", Status = "Active", Amount = 1062, TotalAmount = 1062, FeeAmount = 900, NetFeeAmount = 900, GrossAmount = 1000,
                            ConcessionName = "Sibling", ConcessionCode = "SIB", ConcessionType = "PERCENTAGE", ConcessionValue = 10, ConcessionAmount = 100, ConcessionReason = "SECRET staff note", ConcessionAppliedByName = "Priya",
                            TaxName = "GST 18%", TaxRatePercent = 18, TaxAmount = 162 }
                })
            });
            var dto = (await new StudentMyFeesService(students, billing, pays, NullLogger<StudentMyFeesService>.Instance).GetMyFeesAsync("SCH1", 7, default)).Data!;
            var p = Assert.Single(dto.Payments);
            Assert.Equal((1000m, 100m, 900m, 162m, 1062m), (p.GrossAmount, p.ConcessionAmount, p.NetFeeAmount, p.TaxAmount, p.TotalAmount == 0 ? p.Amount : p.TotalAmount));
            Assert.Equal("Sibling", p.ConcessionName);
            Assert.DoesNotContain("SECRET", System.Text.Json.JsonSerializer.Serialize(dto));
            Assert.DoesNotContain("Priya", System.Text.Json.JsonSerializer.Serialize(dto));
        }

        // ============================================================== helpers (scratch PostgreSQL)
        private static long NewConcession(TaxDb db, string school, string name, string code, string type, decimal value, long? feeTypeId = null, bool active = true) =>
            db.Scalar<long>($"insert into tb_fee_concessions (school_id, concession_name, concession_code, concession_type, value, applicable_fee_type_id, isactive) " +
                            $"values ('{school}', '{name}', '{code}', '{type}', {value.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {(feeTypeId?.ToString() ?? "null")}, {(active ? "true" : "false")}) returning concession_id");

        private static List<PaymentAllocationRequest> Allocs(params (long due, decimal amount)[] a) => a.Select(x => new PaymentAllocationRequest { FeeDueId = x.due, Amount = x.amount }).ToList();

        private static Task<ApiResponse<FeeReceiptDto>> Pay(TaxDb db, decimal gross, long? concessionId, string? reason = "approved by principal", long? taxTypeId = null,
            List<PaymentAllocationRequest>? allocations = null, string student = Student, string school = TaxDb.SchoolA, long user = TaxDb.UserA) =>
            db.Payments().RecordPaymentAsync(school, student, user, new RecordPaymentRequest
            { Amount = gross, PaymentMode = "Cash", TaxTypeId = taxTypeId, ConcessionId = concessionId, ConcessionReason = reason, Allocations = allocations }, default);

        private static long PaymentCount(TaxDb db) => db.Scalar<long>("select count(*) from tb_fee_payments");

        // ============================================================== the worked example
        [DbFact]
        public async Task Example_Fee1000_Concession10Percent_Gst18_Is900_162_1062()
        {
            using var db = new TaxDb();
            var c = NewConcession(db, TaxDb.SchoolA, "Sibling 10%", "SIB10", "PERCENTAGE", 10);
            var gst = await db.NewTax(TaxDb.SchoolA, "GST 18%", "GST18", 18);

            var r = await Pay(db, 1000, c, "second child in the school", gst.TaxTypeId);
            Assert.Equal(201, r.StatusCode);
            var rc = r.Data!;
            Assert.Equal((1000m, 100m, 900m, 162m, 1062m), (rc.GrossAmount, rc.ConcessionAmount, rc.NetFeeAmount, rc.TaxAmount, rc.TotalAmount));
            Assert.Equal((900m, 1062m), (rc.FeeAmount, rc.Amount));
            Assert.Equal(("Sibling 10%", "SIB10", "PERCENTAGE", 10m), (rc.ConcessionName, rc.ConcessionCode, rc.ConcessionType, rc.ConcessionValue));
            Assert.Equal("second child in the school", rc.ConcessionReason);
            Assert.False(string.IsNullOrEmpty(rc.ConcessionAppliedByName));
            Assert.Equal(new[] { 50m, 50m }, rc.Lines.Select(l => l.ConcessionAmount));     // auto-allocation: Library 500 + Exam 500, the discount spread 50 / 50
            Assert.Equal(new[] { 500m, 500m }, rc.Lines.Select(l => l.AllocatedAmount));

            // the invariant, in the database itself
            Assert.Equal(1000m, db.Scalar<decimal>($"select sum(allocated_amount) from tb_fee_payment_allocations where payment_id={rc.PaymentId}"));
            Assert.Equal(1000m, db.Scalar<decimal>($"select fee_amount + concession_amount from tb_fee_payments where payment_id={rc.PaymentId}"));
            Assert.Equal(100m, db.Scalar<decimal>($"select sum(concession_amount) from tb_fee_payment_allocations where payment_id={rc.PaymentId}"));
            Assert.Equal(TaxDb.UserA, db.Scalar<long>($"select concession_applied_by from tb_fee_payments where payment_id={rc.PaymentId}"));   // who applied it: the token user
        }

        [DbFact]
        public async Task WithoutAConcession_ThePaymentIsExactlyWhatItWasBefore()
        {
            using var db = new TaxDb();
            var plain = (await Pay(db, 1000, null, null)).Data!;
            Assert.Equal((1000m, 0m, 1000m, 0m, 1000m), (plain.GrossAmount, plain.ConcessionAmount, plain.NetFeeAmount, plain.TaxAmount, plain.TotalAmount));
            Assert.Null(plain.ConcessionName); Assert.Null(plain.ConcessionReason);
            Assert.All(plain.Lines, l => Assert.Equal(0m, l.ConcessionAmount));
            // a reason sent without a concession is ignored
            var ignored = (await Pay(db, 100, null, "orphan reason")).Data!;
            Assert.Null(ignored.ConcessionReason);
            Assert.Equal(0, db.Scalar<long>("select count(*) from tb_fee_payments where concession_reason is not null"));
        }

        // ============================================================== calculation
        [DbFact]
        public async Task Fixed_IsAppliedOncePerPayment_AndPercentageUsesTheMatchingBaseOnly()
        {
            using var db = new TaxDb();
            var fixedC = NewConcession(db, TaxDb.SchoolA, "Rs 100 off", "F100", "FIXED", 100);
            var r = (await Pay(db, 1000, fixedC)).Data!;
            Assert.Equal((100m, 900m, 900m), (r.ConcessionAmount, r.NetFeeAmount, r.TotalAmount));   // once, however many installments
            Assert.Equal(2, r.Lines.Count);

            // a concession for the Exam fee: only the Exam allocation is the base (700 of the 1000); the Library allocation is untouched
            // (covered with real installments in ApplicableFeeType_OnlyTheMatchingAllocationsAreDiscounted...)
        }

        [DbFact]
        public async Task ApplicableFeeType_OnlyTheMatchingAllocationsAreDiscounted_AndNoMatchIs400()
        {
            using var db = new TaxDb();
            var exam = NewConcession(db, TaxDb.SchoolA, "Exam 10%", "EX10", "PERCENTAGE", 10, ExamType);
            var before = PaymentCount(db);

            var none = await Pay(db, 500, exam, allocations: Allocs((Library, 500)));
            Assert.Equal(400, none.StatusCode);
            Assert.Contains("doesn't apply", none.Message);
            Assert.Equal(before, PaymentCount(db));   // nothing recorded, no installment moved
            Assert.All(db.Dues(Student), d => Assert.Equal(0m, d.Paid));

            var ok = (await Pay(db, 1000, exam, allocations: Allocs((Library, 300), (Exam, 700)))).Data!;
            Assert.Equal(70m, ok.ConcessionAmount);                                   // 10% of the 700 on the Exam installment only
            var byDue = ok.Lines.ToDictionary(l => l.FeeDueId, l => l.ConcessionAmount);
            Assert.Equal(0m, byDue[Library]); Assert.Equal(70m, byDue[Exam]);
            Assert.Equal(930m, ok.NetFeeAmount);
        }

        [DbFact]
        public async Task TheDiscountIsSpreadSoThePartsSumExactly_AndNeverExceedAnAllocation()
        {
            using var db = new TaxDb();
            var c = NewConcession(db, TaxDb.SchoolA, "Ten", "T10", "PERCENTAGE", 10);
            var r = (await Pay(db, 1000, c, allocations: Allocs((Library, 333.33m), (Exam, 333.33m), (Annual, 333.34m)))).Data!;
            Assert.Equal(100m, r.ConcessionAmount);
            Assert.Equal(new[] { 33.33m, 33.33m, 33.34m }, r.Lines.Select(l => l.ConcessionAmount));
            Assert.Equal(100m, db.Scalar<decimal>($"select sum(concession_amount) from tb_fee_payment_allocations where payment_id={r.PaymentId}"));
            Assert.Equal(0, db.Scalar<long>("select count(*) from tb_fee_payment_allocations where concession_amount > allocated_amount"));
        }

        [DbFact]
        public async Task RoundingEdges_AreHalfUp_AndTaxIsOnTheNetFee()
        {
            using var db = new TaxDb();
            var c = NewConcession(db, TaxDb.SchoolA, "Ten", "T10", "PERCENTAGE", 10);
            var gst = await db.NewTax(TaxDb.SchoolA, "GST 18%", "GST18", 18);

            var a = (await Pay(db, 333.33m, c, taxTypeId: gst.TaxTypeId, allocations: Allocs((Annual, 333.33m)))).Data!;   // 33.333 -> 33.33; net 300.00; tax 54.00
            Assert.Equal((33.33m, 300.00m, 54.00m, 354.00m), (a.ConcessionAmount, a.NetFeeAmount, a.TaxAmount, a.TotalAmount));

            var b = (await Pay(db, 0.05m, c, taxTypeId: gst.TaxTypeId, allocations: Allocs((Tuition, 0.05m)))).Data!;      // 0.005 -> 0.01; net 0.04; tax 0.0072 -> 0.01
            Assert.Equal((0.01m, 0.04m, 0.01m, 0.05m), (b.ConcessionAmount, b.NetFeeAmount, b.TaxAmount, b.TotalAmount));

            var tiny = await Pay(db, 0.01m, c, allocations: Allocs((Tuition, 0.01m)));                                       // 0.001 -> 0.00: no discount at all
            Assert.Equal(400, tiny.StatusCode);
        }

        [DbFact]
        public async Task ADiscountThatLeavesNothingToCollect_IsRefused()
        {
            using var db = new TaxDb();
            var full = NewConcession(db, TaxDb.SchoolA, "Full", "FULL", "PERCENTAGE", 100);
            var big = NewConcession(db, TaxDb.SchoolA, "Big", "BIG", "FIXED", 5000);
            var before = PaymentCount(db);
            Assert.Equal(400, (await Pay(db, 500, full, allocations: Allocs((Library, 500)))).StatusCode);
            Assert.Equal(400, (await Pay(db, 200, big, allocations: Allocs((Library, 200)))).StatusCode);   // a fixed amount is capped at the base (200) and that is everything
            Assert.Equal(before, PaymentCount(db));
            var partly = (await Pay(db, 500, big, allocations: Allocs((Library, 500), (Exam, 0.01m)))).StatusCode;   // capped at the base, which here is the whole payment
            Assert.Equal(400, partly);
        }

        // ============================================================== reason, state of the concession, double discount
        [DbFact]
        public async Task ReasonIsRequired_BlankMissingOrTooLongIs400_AndNothingIsRecorded()
        {
            using var db = new TaxDb();
            var c = NewConcession(db, TaxDb.SchoolA, "Ten", "T10", "PERCENTAGE", 10);
            var before = PaymentCount(db);
            foreach (var reason in new string?[] { null, "", "   " })
            {
                var r = await Pay(db, 100, c, reason);
                Assert.Equal(400, r.StatusCode);
                Assert.Contains("reason is required", r.Message);
            }
            Assert.Equal(400, (await Pay(db, 100, c, new string('x', 501))).StatusCode);
            Assert.Equal(201, (await Pay(db, 100, c, new string('x', 500))).StatusCode);   // 500 is allowed
            Assert.Equal(before + 1, PaymentCount(db));
        }

        [DbFact]
        public async Task DeactivatedConcessionIs400_OtherSchoolsConcessionIs404_UnknownIs404()
        {
            using var db = new TaxDb();
            var off = NewConcession(db, TaxDb.SchoolA, "Off", "OFF", "PERCENTAGE", 10, active: false);
            var theirs = NewConcession(db, TaxDb.SchoolB, "Theirs", "THEIRS", "PERCENTAGE", 10);
            var dead = await Pay(db, 100, off);
            Assert.Equal(400, dead.StatusCode); Assert.Contains("deactivated", dead.Message);
            Assert.Equal(404, (await Pay(db, 100, theirs)).StatusCode);
            Assert.Equal(404, (await Pay(db, 100, 987654321)).StatusCode);
            Assert.Equal(0, db.Scalar<long>("select count(*) from tb_fee_payments where concession_id is not null"));
        }

        [DbFact]
        public async Task DoubleDiscountGuard_AConcessionAlreadyAssignedToTheStudentIs409_AnotherOneStillWorks()
        {
            using var db = new TaxDb();
            var assigned = NewConcession(db, TaxDb.SchoolA, "Assigned", "ASG", "PERCENTAGE", 10);
            var other = NewConcession(db, TaxDb.SchoolA, "Other", "OTH", "PERCENTAGE", 10);
            db.Exec($"insert into tb_student_fee_concessions (student_id, concession_id, approved_by) values ('{Student}', {assigned}, {TaxDb.UserA})");
            var r = await Pay(db, 100, assigned);
            Assert.Equal(409, r.StatusCode); Assert.Contains("already has the concession", r.Message);
            Assert.Equal(201, (await Pay(db, 100, other)).StatusCode);
            Assert.Equal(201, (await Pay(db, 100, assigned, student: "GHS001ST002")).StatusCode);   // another student is unaffected
        }

        // ============================================================== dues, existing assignments
        [DbFact]
        public async Task ADiscountedInstallment_IsFullyCleared_AndReadsPaid_AndAmountDueIsUntouched()
        {
            using var db = new TaxDb();
            var c = NewConcession(db, TaxDb.SchoolA, "Ten", "T10", "PERCENTAGE", 10);
            var before = db.Dues(Student);
            var r = (await Pay(db, 500, c, allocations: Allocs((Library, 500)))).Data!;   // cash 450 + discount 50 clears the 500 installment
            Assert.Equal(450m, r.NetFeeAmount);
            var after = db.Dues(Student);
            Assert.Equal(500m, after.First(d => d.Id == Library).Paid);
            Assert.Equal(before.Select(d => d.Due), after.Select(d => d.Due));                      // amount_due never moves
            Assert.Equal("Paid", FeeSchedule.Status(500m, after.First(d => d.Id == Library).Paid, new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 16)));
            // and it can't be paid again
            Assert.Equal(400, (await Pay(db, 100, null, null, allocations: Allocs((Library, 100)))).StatusCode);
        }

        [DbFact]
        public async Task ExistingAssignedConcessions_KeepReducingDuesAsBefore_AndTheMigrationLeftOldPaymentsAlone()
        {
            using var db = new TaxDb();
            Assert.Equal(11, db.Scalar<long>("select count(*) from tb_student_fee_concessions"));    // the assignments in the data are all still there
            Assert.Equal(6, db.Scalar<long>("select count(*) from tb_fee_payments"));
            Assert.Equal(38400m, db.Scalar<decimal>("select sum(amount) from tb_fee_payments"));
            Assert.Equal(0m, db.Scalar<decimal>("select coalesce(sum(concession_amount),0) from tb_fee_payments"));
            Assert.Equal(0m, db.Scalar<decimal>("select coalesce(sum(concession_amount),0) from tb_fee_payment_allocations"));
            Assert.Equal(0, db.Scalar<long>("select count(*) from tb_fee_payments where concession_id is not null"));
            // the discount arithmetic of an assigned concession is the old code, untouched: 10% off a 2500 line and a fixed 300 off the whole structure
            var lines = new List<(long FeeTypeId, decimal Amount)> { (1, 2500m), (2, 1500m) };
            var cons = new List<smss_api_db_layer.@interface.AppliedConcessionRow>
            {
                new() { ConcessionType = "PERCENTAGE", Value = 10, ApplicableFeeTypeId = 1 },
                new() { ConcessionType = "FIXED", Value = 300, ApplicableFeeTypeId = null },
            };
            var d = StudentFeeService.Discounts(lines, cons);
            Assert.Equal(250m + 300m * 2500m / 4000m, d[0], 2);
            // a payment with a concession leaves every amount_due as it was
            var dues = db.Dues(Student);
            var c = NewConcession(db, TaxDb.SchoolA, "Ten", "T10", "PERCENTAGE", 10);
            await Pay(db, 800, c);
            Assert.Equal(dues.Select(x => x.Due), db.Dues(Student).Select(x => x.Due));
        }

        [DbFact]
        public async Task NewAssignmentAttempts_Return409_AndOldOnesCanStillBeRemovedOnlyBySchoolAdmin()
        {
            using var db = new TaxDb();
            var c = NewConcession(db, TaxDb.SchoolA, "Ten", "T10", "PERCENTAGE", 10);
            var fees = new StudentFeeService(new StudentFeeRepository(db.Ctx()), new StudentFeeBillingService(new StudentFeeBillingRepository(db.Ctx()), new StudentFeeRepository(db.Ctx()), NullLogger<StudentFeeBillingService>.Instance), NullLogger<StudentFeeService>.Instance);
            var r = await fees.ApplyConcessionAsync(TaxDb.SchoolA, Student, TaxDb.UserA, new ApplyConcessionRequest { ConcessionId = c }, default);
            Assert.Equal(409, r.StatusCode);
            Assert.Equal("Concessions are now chosen when collecting a payment.", r.Message);
            Assert.Equal(0, db.Scalar<long>($"select count(*) from tb_student_fee_concessions where student_id='{Student}'"));   // nothing was assigned
            var read = await fees.GetFeesAsync(TaxDb.SchoolA, Student, default);   // the read still works, with nothing to offer
            Assert.Equal(200, read.StatusCode);
            Assert.Empty(read.Data!.AvailableConcessions);
        }

        // ============================================================== void, editing a concession, receipts
        [DbFact]
        public async Task Void_RestoresTheDuesInFull_AndTheVoidedReceiptKeepsItsConcessionAndTaxLines()
        {
            using var db = new TaxDb();
            var c = NewConcession(db, TaxDb.SchoolA, "Sibling 10%", "SIB10", "PERCENTAGE", 10);
            var gst = await db.NewTax(TaxDb.SchoolA, "GST 18%", "GST18", 18);
            var before = db.Dues(Student);
            var paid = (await Pay(db, 1000, c, "second child", gst.TaxTypeId)).Data!;
            var v = await db.Payments().VoidPaymentAsync(TaxDb.SchoolA, paid.PaymentId, TaxDb.UserA, new VoidPaymentRequest { VoidReason = "wrong student" }, default);
            Assert.Equal(200, v.StatusCode);
            Assert.Equal("Voided", v.Data!.Status);
            Assert.Equal((100m, 162m, 1062m, "Sibling 10%"), (v.Data.ConcessionAmount, v.Data.TaxAmount, v.Data.TotalAmount, v.Data.ConcessionName));
            Assert.Equal(before.Select(d => d.Paid), db.Dues(Student).Select(d => d.Paid));   // the discount came back with the cash: every installment is where it was

            var receipt = (await db.Payments().GetReceiptAsync(TaxDb.SchoolA, paid.PaymentId, default)).Data!;
            Assert.Equal("Voided", receipt.Status);
            Assert.Equal((1000m, 100m, 900m, 162m, 1062m), (receipt.GrossAmount, receipt.ConcessionAmount, receipt.NetFeeAmount, receipt.TaxAmount, receipt.TotalAmount));
            Assert.Equal("second child", receipt.ConcessionReason);
            var history = (await db.Payments().GetPaymentsAsync(TaxDb.SchoolA, Student, default)).Data!.Single();
            Assert.Equal((100m, "Voided", "second child"), (history.ConcessionAmount, history.Status, history.ConcessionReason));
            Assert.False(string.IsNullOrEmpty(history.ConcessionAppliedByName));
        }

        [DbFact]
        public async Task EditingOrDeactivatingAConcessionLater_NeverChangesAnOldReceipt()
        {
            using var db = new TaxDb();
            var c = NewConcession(db, TaxDb.SchoolA, "Sibling 10%", "SIB10", "PERCENTAGE", 10);
            var old = (await Pay(db, 1000, c)).Data!;
            db.Exec($"update tb_fee_concessions set concession_name='Renamed', concession_code='REN', value=50, concession_type='FIXED', isactive=false where concession_id={c}");
            var again = (await db.Payments().GetReceiptAsync(TaxDb.SchoolA, old.PaymentId, default)).Data!;
            Assert.Equal(("Sibling 10%", "SIB10", "PERCENTAGE", 10m, 100m, 900m), (again.ConcessionName, again.ConcessionCode, again.ConcessionType, again.ConcessionValue, again.ConcessionAmount, again.NetFeeAmount));
            var hist = (await db.Payments().GetPaymentsAsync(TaxDb.SchoolA, Student, default)).Data!.Single();
            Assert.Equal("Sibling 10%", hist.ConcessionName);
            Assert.Equal(400, (await Pay(db, 100, c)).StatusCode);   // deactivated now: refused for a new payment
        }

        [DbFact]
        public async Task TheStudentsOwnReceiptHidesTheReason_StaffSeeIt()
        {
            using var db = new TaxDb();
            var c = NewConcession(db, TaxDb.SchoolA, "Sibling 10%", "SIB10", "PERCENTAGE", 10);
            var paid = (await Pay(db, 1000, c, "private staff note")).Data!;
            var studentUser = db.Scalar<long>($"select user_id from tb_users where school_id='{TaxDb.SchoolA}' and org_user_id='{Student}'");
            var own = (await db.Payments().GetOwnReceiptAsync(TaxDb.SchoolA, studentUser, paid.PaymentId, default)).Data!;
            Assert.Equal(100m, own.ConcessionAmount); Assert.Equal("Sibling 10%", own.ConcessionName);
            Assert.Null(own.ConcessionReason); Assert.Null(own.ConcessionAppliedByName);
            Assert.Equal("private staff note", (await db.Payments().GetReceiptAsync(TaxDb.SchoolA, paid.PaymentId, default)).Data!.ConcessionReason);
        }

        // ============================================================== the choices for the payment form
        [DbFact]
        public async Task ConcessionChoices_AreTheSchoolsActiveOnesOnly()
        {
            using var db = new TaxDb();
            NewConcession(db, TaxDb.SchoolA, "B Active", "BA", "PERCENTAGE", 5);
            NewConcession(db, TaxDb.SchoolA, "A Fixed", "AF", "FIXED", 200, ExamType);
            NewConcession(db, TaxDb.SchoolA, "Off", "OFF", "PERCENTAGE", 5, active: false);
            NewConcession(db, TaxDb.SchoolB, "Other school", "OS", "PERCENTAGE", 5);
            var list = (await db.Payments().GetConcessionChoicesAsync(TaxDb.SchoolA, default)).Data!;
            var names = list.Select(x => x.ConcessionName).ToList();
            Assert.Contains("A Fixed", names); Assert.Contains("B Active", names);            // the school already has its own seeded concessions too
            Assert.DoesNotContain("Off", names); Assert.DoesNotContain("Other school", names);
            Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal).ToList(), names);       // by name
            Assert.Equal("Exam Fee", list.Single(x => x.ConcessionName == "A Fixed").ApplicableFeeTypeName);
            Assert.Equal(400, (await db.Payments().GetConcessionChoicesAsync(null, default)).StatusCode);
        }

        // ============================================================== readers: Fee Dashboard, class report
        [DbFact]
        public async Task FeeDashboard_CollectedIsCash_ConcessionGivenMatchesIndependentSql_AndVoidedIsExcluded()
        {
            using var db = new TaxDb();
            var c = NewConcession(db, TaxDb.SchoolA, "Sibling 10%", "SIB10", "PERCENTAGE", 10);
            var c2 = NewConcession(db, TaxDb.SchoolA, "Staff Rs 50", "STF50", "FIXED", 50);
            var gst = await db.NewTax(TaxDb.SchoolA, "GST 18%", "GST18", 18);
            await Pay(db, 1000, c, taxTypeId: gst.TaxTypeId);                                              // discount 100, cash fee 900, tax 162
            await Pay(db, 800, c2, student: "GHS001ST002");                                                // discount 50, cash fee 750
            var cancelled = (await Pay(db, 500, c, student: "GHS001ST003")).Data!;                        // discount 50 ... then voided
            await db.Payments().VoidPaymentAsync(TaxDb.SchoolA, cancelled.PaymentId, TaxDb.UserA, new VoidPaymentRequest { VoidReason = "typo" }, default);
            await Pay(db, 200, null, null, student: "GHS001ST004");                                        // a plain 200

            var d = (await db.Reports().GetDashboardAsync(TaxDb.SchoolA, TaxDb.YearA, null, null, 7, true, default)).Data!;

            var cashSql = db.Scalar<decimal>($@"select coalesce(sum(a.allocated_amount - a.concession_amount),0) from tb_fee_payment_allocations a join tb_fee_payments p on p.payment_id=a.payment_id
                join tb_student_fee_dues d on d.fee_due_id=a.fee_due_id join tb_student_fee_items i on i.student_fee_item_id=d.student_fee_item_id
                where p.school_id='{TaxDb.SchoolA}' and p.status='Active' and i.academic_year_id={TaxDb.YearA}");
            var givenSql = db.Scalar<decimal>($@"select coalesce(sum(a.concession_amount),0) from tb_fee_payment_allocations a join tb_fee_payments p on p.payment_id=a.payment_id
                join tb_student_fee_dues d on d.fee_due_id=a.fee_due_id join tb_student_fee_items i on i.student_fee_item_id=d.student_fee_item_id
                where p.school_id='{TaxDb.SchoolA}' and p.status='Active' and i.academic_year_id={TaxDb.YearA}");
            Assert.Equal(900m + 750m + 200m, d.TotalCollected);          // cash only: no discount, no tax
            Assert.Equal(150m, d.ConcessionGiven);                        // 100 + 50; the voided 50 is not counted
            Assert.Equal(cashSql, d.TotalCollected);
            Assert.Equal(givenSql, d.ConcessionGiven);
            Assert.Equal(162m, d.TaxCollected);
            Assert.Equal(new[] { "Sibling 10%", "Staff Rs 50" }, d.ConcessionByType.Select(x => x.ConcessionName));
            Assert.Equal(new[] { 100m, 50m }, d.ConcessionByType.Select(x => x.ConcessionAmount));
            Assert.Equal(d.ConcessionGiven, d.ConcessionByType.Sum(x => x.ConcessionAmount));
            // the month table is cash too
            Assert.Equal(d.TotalCollected, d.CollectionByMonth.Sum(m => m.Amount));
            // outstanding is driven by what is PAID (a discounted installment is cleared): due = cash + discount + outstanding
            Assert.Equal(d.TotalDue, d.TotalCollected + d.ConcessionGiven + d.TotalOutstanding);
        }

        [DbFact]
        public async Task ClassReport_ShowsCashCollectedAndAConcessionColumn_OutstandingStaysDrivenByPaid()
        {
            using var db = new TaxDb();
            var c = NewConcession(db, TaxDb.SchoolA, "Ten", "T10", "PERCENTAGE", 10);
            await Pay(db, 500, c, allocations: Allocs((Library, 500)));
            var classId = db.Scalar<long>($"select s.class_id from tb_student_enrollments e join tb_sections s on s.section_id=e.class_section_id where e.student_id='{Student}' and e.academic_year_id={TaxDb.YearA}");
            var report = (await db.Reports().GetClassReportAsync(TaxDb.SchoolA, classId, TaxDb.YearA, null, true, default)).Data!;
            var row = report.Students.Single(s => s.StudentId == Student);
            Assert.Equal((450m, 50m), (row.TotalCollected, row.Concession));
            Assert.Equal(row.TotalDue - 500m, row.Outstanding);                                             // the 500 installment counts as cleared
            Assert.Equal(report.Students.Sum(s => s.Concession), report.TotalConcession);
            Assert.Equal(row.TotalDue, row.TotalCollected + row.Concession + row.Outstanding);
        }

        // ============================================================== the audit list
        [DbFact]
        public async Task ConcessionsGiven_ListsPaymentsWithReasonAndWho_FiltersByDate_Pages_AndNeverShowsAnotherSchool()
        {
            using var db = new TaxDb();
            var c = NewConcession(db, TaxDb.SchoolA, "Ten", "T10", "PERCENTAGE", 10);
            var theirs = NewConcession(db, TaxDb.SchoolB, "Theirs", "TH", "PERCENTAGE", 10);
            var p1 = (await Pay(db, 1000, c, "first")).Data!;
            var p2 = (await Pay(db, 500, c, "second", student: "GHS001ST002")).Data!;
            var p3 = (await Pay(db, 300, c, "third", student: "GHS001ST003")).Data!;
            await db.Payments().VoidPaymentAsync(TaxDb.SchoolA, p3.PaymentId, TaxDb.UserA, new VoidPaymentRequest { VoidReason = "typo" }, default);
            await Pay(db, 100, null, null, student: "GHS001ST004");   // no concession: not listed
            db.Exec($"update tb_fee_payments set payment_date = '2026-10-05T10:00:00Z' where payment_id = {p2.PaymentId}");   // an older one

            var all = (await db.Reports().GetConcessionsGivenAsync(TaxDb.SchoolA, null, null, 1, 25, default)).Data!;
            Assert.Equal(3, all.TotalItems);
            Assert.Equal(new[] { p3.PaymentId, p1.PaymentId, p2.PaymentId }, all.Items.Select(i => i.PaymentId));   // newest first
            Assert.Equal(150m, all.TotalAmount);                                                              // 100 + 50; the voided 30 is not in the total
            var first = all.Items.Single(i => i.PaymentId == p1.PaymentId);
            Assert.Equal(("first", 100m, "Ten", "GHS001ST001"), (first.Reason, first.ConcessionAmount, first.ConcessionName, first.StudentId));
            Assert.False(string.IsNullOrEmpty(first.AppliedByName)); Assert.False(string.IsNullOrEmpty(first.StudentName)); Assert.Equal(p1.ReceiptNumber, first.ReceiptNumber);
            Assert.Equal("Voided", all.Items.Single(i => i.PaymentId == p3.PaymentId).Status);

            var oct = (await db.Reports().GetConcessionsGivenAsync(TaxDb.SchoolA, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 1, 25, default)).Data!;
            Assert.Equal(new[] { p2.PaymentId }, oct.Items.Select(i => i.PaymentId));
            Assert.Equal(50m, oct.TotalAmount);
            Assert.Empty((await db.Reports().GetConcessionsGivenAsync(TaxDb.SchoolA, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), 1, 25, default)).Data!.Items);

            var page2 = (await db.Reports().GetConcessionsGivenAsync(TaxDb.SchoolA, null, null, 2, 2, default)).Data!;
            Assert.Equal((3, 2, 2), (page2.TotalItems, page2.Page, page2.PageSize));
            Assert.Equal(new[] { p2.PaymentId }, page2.Items.Select(i => i.PaymentId));
            Assert.Equal(400, (await db.Reports().GetConcessionsGivenAsync(TaxDb.SchoolA, new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 1), 1, 25, default)).StatusCode);
            Assert.Equal(100, (await db.Reports().GetConcessionsGivenAsync(TaxDb.SchoolA, null, null, 1, 5000, default)).Data!.PageSize);   // capped

            // school B has none of school A's rows
            await Pay(db, 100, theirs, "theirs", school: TaxDb.SchoolB, user: TaxDb.UserB, student: db.Scalar<string>($"select student_id from tb_students where school_id='{TaxDb.SchoolB}' order by student_id limit 1"));
            var b = (await db.Reports().GetConcessionsGivenAsync(TaxDb.SchoolB, null, null, 1, 25, default)).Data!;
            Assert.All(b.Items, i => Assert.Equal("Theirs", i.ConcessionName));
            Assert.DoesNotContain(b.Items, i => i.PaymentId == p1.PaymentId);
        }

        // ============================================================== concurrency
        [DbFact]
        public async Task SixSimultaneousPaymentsWithConcessionAndTax_GetDistinctReceiptNumbers_AndConsistentTotals()
        {
            using var db = new TaxDb();
            var c = NewConcession(db, TaxDb.SchoolA, "Ten", "T10", "PERCENTAGE", 10);
            var gst = await db.NewTax(TaxDb.SchoolA, "GST 18%", "GST18", 18);
            var students = Enumerable.Range(1, 6).Select(i => $"GHS001ST00{i}").ToList();
            var results = await Task.WhenAll(students.Select(s => Task.Run(() => Pay(db, 100, c, "bulk", gst.TaxTypeId, null, s))));
            Assert.All(results, r => Assert.Equal(201, r.StatusCode));
            Assert.Equal(6, results.Select(r => r.Data!.ReceiptNumber).Distinct().Count());
            Assert.All(results, r => Assert.Equal((100m, 10m, 90m, 16.20m, 106.20m), (r.Data!.GrossAmount, r.Data.ConcessionAmount, r.Data.NetFeeAmount, r.Data.TaxAmount, r.Data.TotalAmount)));
            Assert.Equal(6, db.Scalar<long>("select count(distinct receipt_number) from tb_fee_payments where concession_id is not null"));
            Assert.Equal(0, db.Scalar<long>(@"select count(*) from (select p.payment_id from tb_fee_payments p join tb_fee_payment_allocations a on a.payment_id=p.payment_id
                where p.concession_id is not null group by p.payment_id, p.fee_amount, p.concession_amount having sum(a.allocated_amount) <> p.fee_amount + p.concession_amount) x"));
            Assert.Equal(60m, db.Scalar<decimal>("select sum(concession_amount) from tb_fee_payments where concession_id is not null"));
            Assert.Equal(6 * 106.20m, db.Scalar<decimal>("select sum(amount) from tb_fee_payments where concession_id is not null"));
        }
    }
}
