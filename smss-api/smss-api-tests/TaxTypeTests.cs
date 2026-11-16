using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Controllers;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>Tax types (Fee Setup) and tax on fee payments. The first half needs no database; the [DbFact] half runs against a scratch PostgreSQL (see TaxTestKit).</summary>
    public class TaxTypeTests
    {
        private const string School = "SCH1";
        private static readonly string[] ReadRoles = { "School Admin", "Accountant", "Principal" };
        private static readonly string[] NoRead = { "Teacher", "Student", "Guardian", "Admission Staff", "Super Admin", "Driver" };

        // ============================================================== authorization matrix (no database)
        private static IEnumerable<MethodInfo> Endpoints() =>
            typeof(TaxTypeController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        private static async Task<int> Call(string name, bool includeInactive, params string[] roles)
        {
            var fake = Fake.Of<ITaxTypeService>();
            var c = Fake.WithUser(new TaxTypeController(fake), Fake.User(School, 7, roles));
            var ok = new TaxTypeRequest { TaxName = "GST", TaxCode = "GST", RatePercent = 5 };
            IActionResult r = name switch
            {
                "GetAll" => await c.GetAll(includeInactive, default),
                "Create" => await c.Create(ok, default),
                "Update" => await c.Update(1, ok, default),
                "Activate" => await c.Activate(1, default),
                "Deactivate" => await c.Deactivate(1, default),
                _ => throw new ArgumentException(name)
            };
            return Fake.Code(r);
        }

        [Fact]
        public async Task Reads_AreForSchoolAdminAccountantAndPrincipal_EveryoneElseGets403()
        {
            foreach (var role in ReadRoles) Assert.NotEqual(403, await Call("GetAll", false, role));
            foreach (var role in NoRead) Assert.Equal(403, await Call("GetAll", false, role));
            Assert.Equal(403, await Call("GetAll", false));   // a login with no role at all
        }

        [Theory]
        [InlineData("Create")] [InlineData("Update")] [InlineData("Activate")] [InlineData("Deactivate")]
        public async Task Writes_AreSchoolAdminOnly(string action)
        {
            Assert.NotEqual(403, await Call(action, false, "School Admin"));
            foreach (var role in new[] { "Accountant", "Principal", "Teacher", "Student", "Guardian", "Admission Staff", "Super Admin", "Driver" })
                Assert.True(403 == await Call(action, false, role), $"{action} must refuse {role}");
            Assert.Equal(403, await Call(action, false));
        }

        [Fact]
        public void Controller_RequiresALogin_HasNoAnonymousOrHardDeleteOrPatch()
        {
            Assert.NotEmpty(typeof(TaxTypeController).GetCustomAttributes<AuthorizeAttribute>());   // anonymous = 401
            Assert.Empty(typeof(TaxTypeController).GetCustomAttributes<AllowAnonymousAttribute>());
            foreach (var m in Endpoints())
            {
                Assert.Empty(m.GetCustomAttributes<AllowAnonymousAttribute>());
                Assert.DoesNotContain(m.GetCustomAttributes(), a => a is HttpDeleteAttribute or HttpPatchAttribute);   // no hard delete; PATCH is blocked here
            }
        }

        [Fact]
        public async Task IncludeInactive_IsHonouredForSchoolAdminOnly()
        {
            bool? seen = null;
            var fake = Fake.Of<ITaxTypeService>(new() { ["GetAllAsync"] = (Func<object?[], object?>)(a => { seen = (bool)a[1]!; return Fake.Ok(new List<TaxTypeDto>()); }) });
            foreach (var (role, expected) in new[] { ("School Admin", true), ("Accountant", false), ("Principal", false) })
            {
                seen = null;
                var c = Fake.WithUser(new TaxTypeController(fake), Fake.User(School, 7, role));
                await c.GetAll(true, default);
                Assert.Equal(expected, seen);
            }
        }

        // ============================================================== rounding and validation (no database)
        [Theory]
        [InlineData("1000", "18", "180.00")]
        [InlineData("333.33", "5", "16.67")]     // 16.6665 rounds half up
        [InlineData("0.01", "5", "0.00")]        // 0.0005
        [InlineData("1000", "0", "0.00")]
        [InlineData("100", "12.5", "12.50")]
        [InlineData("0.10", "5", "0.01")]        // 0.005: exactly half, rounds up
        [InlineData("0.30", "5", "0.02")]        // 0.015: exactly half, rounds up (binary floating point would get this wrong)
        [InlineData("99.99", "100", "99.99")]
        public void TaxOn_RoundsHalfUp_Exactly(string fee, string rate, string expected) =>
            Assert.Equal(decimal.Parse(expected), TaxMath.TaxOn(decimal.Parse(fee), decimal.Parse(rate)));

        [Theory]
        [InlineData("-0.01")] [InlineData("100.01")] [InlineData("5.123")] [InlineData("1000")]
        public void Validate_RejectsBadRates(string rate)
        {
            var msg = TaxTypeService.Validate(new TaxTypeRequest { TaxName = "GST", TaxCode = "GST", RatePercent = decimal.Parse(rate) }, out _, out _, out _);
            Assert.NotNull(msg);
        }

        [Theory]
        [InlineData("0")] [InlineData("100")] [InlineData("5")] [InlineData("12.5")] [InlineData("0.01")]
        public void Validate_AcceptsRatesFrom0To100WithTwoDecimals(string rate)
        {
            Assert.Null(TaxTypeService.Validate(new TaxTypeRequest { TaxName = " GST ", TaxCode = "gst-5", RatePercent = decimal.Parse(rate) }, out var name, out var code, out var r));
            Assert.Equal("GST", name); Assert.Equal("GST-5", code); Assert.Equal(decimal.Parse(rate), r);
        }

        [Fact]
        public void Validate_RejectsBlankNameCodeAndBadCodeCharacters()
        {
            Assert.NotNull(TaxTypeService.Validate(new TaxTypeRequest { TaxName = "  ", TaxCode = "X", RatePercent = 1 }, out _, out _, out _));
            Assert.NotNull(TaxTypeService.Validate(new TaxTypeRequest { TaxName = "X", TaxCode = " ", RatePercent = 1 }, out _, out _, out _));
            Assert.NotNull(TaxTypeService.Validate(new TaxTypeRequest { TaxName = "X", TaxCode = "A B", RatePercent = 1 }, out _, out _, out _));
            Assert.NotNull(TaxTypeService.Validate(new TaxTypeRequest { TaxName = "X", TaxCode = "X", RatePercent = null }, out _, out _, out _));
        }

        [Fact]
        public void RecordPaymentRequest_HasNoTaxAmount_AClientSentOneIsNeverRead()
        {
            Assert.Null(typeof(RecordPaymentRequest).GetProperty("TaxAmount"));
            Assert.Null(typeof(RecordPaymentRequest).GetProperty("TotalAmount"));
            Assert.Null(typeof(RecordPaymentRequest).GetProperty("FeeAmount"));
            var req = JsonSerializer.Deserialize<RecordPaymentRequest>("{\"amount\":100,\"paymentMode\":\"Cash\",\"taxAmount\":9999,\"totalAmount\":1,\"taxRatePercent\":99}",
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(100m, req.Amount);
            Assert.Null(req.TaxTypeId);
        }

        // ============================================================== database tests
        [DbFact]
        public void Migration_OldPaymentsKeepTheirMoney_FeeEqualsAmountAndNoTax()
        {
            using var db = new TaxDb();
            Assert.Equal(6, db.Scalar<int>("select count(*) from tb_fee_payments"));
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_fee_payments where fee_amount <> amount or tax_amount <> 0 or tax_type_id is not null or tax_name_snapshot is not null"));
            Assert.Equal(0, db.Scalar<int>(@"select count(*) from (select p.payment_id from tb_fee_payments p join tb_fee_payment_allocations a using (payment_id)
                                             group by p.payment_id, p.fee_amount having sum(a.allocated_amount) <> p.fee_amount) x"));
        }

        [DbFact]
        public async Task TaxTypes_DuplicateNameOrCode_IsRefusedCaseInsensitively_PerSchool()
        {
            using var db = new TaxDb();
            await db.NewTax(TaxDb.SchoolA, "GST", "GST5", 5);
            var byCode = await db.Taxes().CreateAsync(TaxDb.SchoolA, new TaxTypeRequest { TaxName = "Other", TaxCode = "gst5", RatePercent = 5 }, default);
            var byName = await db.Taxes().CreateAsync(TaxDb.SchoolA, new TaxTypeRequest { TaxName = "  gst ", TaxCode = "OTHER", RatePercent = 5 }, default);
            Assert.Equal(409, byCode.StatusCode); Assert.Equal(409, byName.StatusCode);
            // the same name and code are fine in another school
            Assert.True((await db.Taxes().CreateAsync(TaxDb.SchoolB, new TaxTypeRequest { TaxName = "GST", TaxCode = "GST5", RatePercent = 5 }, default)).Status);
            // renaming a type to itself is fine; to another type's code is not
            var second = await db.NewTax(TaxDb.SchoolA, "Cess", "CESS", 1);
            Assert.True((await db.Taxes().UpdateAsync(TaxDb.SchoolA, second.TaxTypeId, new TaxTypeRequest { TaxName = "Cess", TaxCode = "cess", RatePercent = 2 }, default)).Status);
            Assert.Equal(409, (await db.Taxes().UpdateAsync(TaxDb.SchoolA, second.TaxTypeId, new TaxTypeRequest { TaxName = "Cess", TaxCode = "Gst5", RatePercent = 2 }, default)).StatusCode);
        }

        [DbFact]
        public async Task TaxTypes_RateBounds_AreEnforcedByTheServiceAndTheTable()
        {
            using var db = new TaxDb();
            foreach (var bad in new[] { -1m, 100.01m, 5.555m })
                Assert.Equal(400, (await db.Taxes().CreateAsync(TaxDb.SchoolA, new TaxTypeRequest { TaxName = "X" + bad, TaxCode = "X", RatePercent = bad }, default)).StatusCode);
            Assert.True((await db.Taxes().CreateAsync(TaxDb.SchoolA, new TaxTypeRequest { TaxName = "Zero", TaxCode = "ZERO", RatePercent = 0 }, default)).Status);
            Assert.True((await db.Taxes().CreateAsync(TaxDb.SchoolA, new TaxTypeRequest { TaxName = "Full", TaxCode = "FULL", RatePercent = 100 }, default)).Status);
            // the table refuses it even when the service is bypassed
            Assert.ThrowsAny<Exception>(() => db.Exec($"insert into tb_tax_types (school_id, tax_name, tax_code, rate_percent) values ('{TaxDb.SchoolA}', 'Bad', 'BAD', 100.01)"));
        }

        [DbFact]
        public async Task OtherSchoolsTaxType_Is404_ForUpdateActivateDeactivateAndPayment_AndNeverListed()
        {
            using var db = new TaxDb();
            var b = await db.NewTax(TaxDb.SchoolB, "GST", "GST", 5);
            var req = new TaxTypeRequest { TaxName = "Hijack", TaxCode = "H", RatePercent = 1 };
            Assert.Equal(404, (await db.Taxes().UpdateAsync(TaxDb.SchoolA, b.TaxTypeId, req, default)).StatusCode);
            Assert.Equal(404, (await db.Taxes().SetActiveAsync(TaxDb.SchoolA, b.TaxTypeId, false, default)).StatusCode);
            Assert.Equal(404, (await db.Taxes().SetActiveAsync(TaxDb.SchoolA, b.TaxTypeId, true, default)).StatusCode);
            Assert.Empty((await db.Taxes().GetAllAsync(TaxDb.SchoolA, true, default)).Data!);
            var pay = await db.Pay("GHS001ST001", 100, b.TaxTypeId);
            Assert.Equal(404, pay.StatusCode);
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_fee_payments where school_id = '" + TaxDb.SchoolA + "'"));
            Assert.True((await db.Taxes().GetByIdOk(TaxDb.SchoolB, b.TaxTypeId)));   // untouched in its own school
        }

        [DbFact]
        public async Task ListIsActiveOnlyByDefault_AndDeactivateActivateRoundTrips()
        {
            using var db = new TaxDb();
            var t = await db.NewTax(TaxDb.SchoolA, "GST", "GST", 5);
            await db.NewTax(TaxDb.SchoolA, "Cess", "CESS", 1);
            Assert.True((await db.Taxes().SetActiveAsync(TaxDb.SchoolA, t.TaxTypeId, false, default)).Status);
            Assert.Single((await db.Taxes().GetAllAsync(TaxDb.SchoolA, false, default)).Data!);
            Assert.Equal(2, (await db.Taxes().GetAllAsync(TaxDb.SchoolA, true, default)).Data!.Count);
            Assert.True((await db.Taxes().SetActiveAsync(TaxDb.SchoolA, t.TaxTypeId, true, default)).Data!.IsActive);
        }

        [DbFact]
        public async Task PaymentWithoutTax_BehavesAndNumbersExactlyAsBefore()
        {
            using var db = new TaxDb();
            var r = await db.Pay("GHS001ST001", 1000);
            Assert.Equal(201, r.StatusCode);
            var d = r.Data!;
            Assert.Equal(1000m, d.Amount); Assert.Equal(1000m, d.FeeAmount); Assert.Equal(1000m, d.TotalAmount); Assert.Equal(0m, d.TaxAmount);
            Assert.Null(d.TaxName); Assert.Null(d.TaxCode); Assert.Null(d.TaxRatePercent);
            Assert.Equal(1000m, d.Lines.Sum(l => l.AllocatedAmount));
            Assert.Equal(1000m, db.Scalar<decimal>("select coalesce(sum(allocated_amount),0) from tb_fee_payment_allocations a join tb_fee_payments p using (payment_id) where p.payment_id = " + d.PaymentId));
            Assert.Equal(1000m, db.Dues("GHS001ST001").Sum(x => x.Paid));
            Assert.Equal("RCP-2026-0001", d.ReceiptNumber);
        }

        [DbFact]
        public async Task PaymentWithTax_StoresFeeTaxTotalAndSnapshots_AllocationsSumToTheFeeNeverTheTotal()
        {
            using var db = new TaxDb();
            var gst = await db.NewTax(TaxDb.SchoolA, "GST", "GST18", 18);
            var r = await db.Pay("GHS001ST001", 1000, gst.TaxTypeId);
            Assert.Equal(201, r.StatusCode);
            var d = r.Data!;
            Assert.Equal(1000m, d.FeeAmount); Assert.Equal(180.00m, d.TaxAmount); Assert.Equal(1180.00m, d.TotalAmount); Assert.Equal(1180.00m, d.Amount);
            Assert.Equal("GST", d.TaxName); Assert.Equal("GST18", d.TaxCode); Assert.Equal(18m, d.TaxRatePercent);
            Assert.Equal(1000m, d.Lines.Sum(l => l.AllocatedAmount));
            Assert.Equal(1000m, db.Scalar<decimal>($"select sum(allocated_amount) from tb_fee_payment_allocations where payment_id = {d.PaymentId}"));
            Assert.Equal(1000m, db.Dues("GHS001ST001").Sum(x => x.Paid));   // the dues only moved by the fee
            Assert.Equal(1180.00m, db.Scalar<decimal>($"select amount from tb_fee_payments where payment_id = {d.PaymentId}"));
        }

        [DbFact]
        public async Task Rounding_ThroughTheService()
        {
            using var db = new TaxDb();
            var five = await db.NewTax(TaxDb.SchoolA, "Five", "F5", 5);
            var zero = await db.NewTax(TaxDb.SchoolA, "Zero", "Z0", 0);
            var a = (await db.Pay("GHS001ST001", 333.33m, five.TaxTypeId)).Data!;
            Assert.Equal(16.67m, a.TaxAmount); Assert.Equal(350.00m, a.TotalAmount);
            var b = (await db.Pay("GHS001ST002", 0.01m, five.TaxTypeId)).Data!;
            Assert.Equal(0.00m, b.TaxAmount); Assert.Equal(0.01m, b.TotalAmount);
            var c = (await db.Pay("GHS001ST003", 1000m, zero.TaxTypeId)).Data!;
            Assert.Equal(0.00m, c.TaxAmount); Assert.Equal(1000m, c.TotalAmount); Assert.Equal("Zero", c.TaxName);   // a 0% type is still recorded on the receipt
        }

        [DbFact]
        public async Task AutoAllocation_IsOldestDueFirst_OnTheFeeAmount()
        {
            using var db = new TaxDb();
            var gst = await db.NewTax(TaxDb.SchoolA, "GST", "GST", 10);
            var before = db.Dues("GHS001ST001");
            var r = (await db.Pay("GHS001ST001", 1000, gst.TaxTypeId)).Data!;   // dues are 500, 800, ... : 500 to the first, 500 to the second
            var after = db.Dues("GHS001ST001");
            Assert.Equal(500m, after[0].Paid - before[0].Paid);
            Assert.Equal(500m, after[1].Paid - before[1].Paid);
            Assert.Equal(0m, after[2].Paid - before[2].Paid);
            Assert.Equal(2, r.Lines.Count);
            Assert.Equal(100m, r.TaxAmount);
        }

        [DbFact]
        public async Task ExplicitAllocations_MustEqualTheFee_NotTheTotal()
        {
            using var db = new TaxDb();
            var gst = await db.NewTax(TaxDb.SchoolA, "GST", "GST", 10);
            var dues = db.Dues("GHS001ST001");
            // adding up to the TOTAL (fee 500 + tax 50 = 550) is refused
            var asTotal = await db.Pay("GHS001ST001", 500, gst.TaxTypeId, new() { new() { FeeDueId = dues[2].Id, Amount = 550 } });
            Assert.Equal(400, asTotal.StatusCode);
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_fee_payments where school_id = '" + TaxDb.SchoolA + "'"));
            // adding up to the FEE is accepted
            var ok = await db.Pay("GHS001ST001", 500, gst.TaxTypeId, new() { new() { FeeDueId = dues[2].Id, Amount = 300 }, new() { FeeDueId = dues[3].Id, Amount = 200 } });
            Assert.Equal(201, ok.StatusCode);
            Assert.Equal(550m, ok.Data!.TotalAmount);
            Assert.Equal(500m, db.Scalar<decimal>($"select sum(allocated_amount) from tb_fee_payment_allocations where payment_id = {ok.Data.PaymentId}"));
        }

        [DbFact]
        public async Task Overpay_IsRefused_TheFeeCannotPushADuePastItsAmount_WithOrWithoutTax()
        {
            using var db = new TaxDb();
            var gst = await db.NewTax(TaxDb.SchoolA, "GST", "GST", 10);
            var dues = db.Dues("GHS001ST001");
            var outstanding = dues.Sum(d => d.Due - d.Paid);
            Assert.Equal(400, (await db.Pay("GHS001ST001", outstanding + 0.01m)).StatusCode);
            Assert.Equal(400, (await db.Pay("GHS001ST001", outstanding + 0.01m, gst.TaxTypeId)).StatusCode);
            Assert.Equal(400, (await db.Pay("GHS001ST001", 600, gst.TaxTypeId, new() { new() { FeeDueId = dues[0].Id, Amount = 600 } })).StatusCode);   // due 0 is 500
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_fee_payments where school_id = '" + TaxDb.SchoolA + "'"));
            Assert.Equal(0m, db.Dues("GHS001ST001").Sum(d => d.Paid));
            // paying exactly what is outstanding, with tax, works
            Assert.Equal(201, (await db.Pay("GHS001ST001", outstanding, gst.TaxTypeId)).StatusCode);
        }

        [DbFact]
        public async Task Void_RestoresTheDuesByTheFee_AndTheVoidedReceiptKeepsItsTaxLines()
        {
            using var db = new TaxDb();
            var gst = await db.NewTax(TaxDb.SchoolA, "GST", "GST", 18);
            var before = db.Dues("GHS001ST001");
            var paid = (await db.Pay("GHS001ST001", 1000, gst.TaxTypeId)).Data!;
            var v = await db.Payments().VoidPaymentAsync(TaxDb.SchoolA, paid.PaymentId, TaxDb.UserA, new VoidPaymentRequest { VoidReason = "wrong student" }, default);
            Assert.Equal(200, v.StatusCode);
            Assert.Equal("Voided", v.Data!.Status);
            Assert.Equal(180m, v.Data.TaxAmount); Assert.Equal(1180m, v.Data.TotalAmount); Assert.Equal("GST", v.Data.TaxName);
            Assert.Equal(before.Select(d => d.Paid), db.Dues("GHS001ST001").Select(d => d.Paid));   // dues back to what they were
            var receipt = (await db.Payments().GetReceiptAsync(TaxDb.SchoolA, paid.PaymentId, default)).Data!;
            Assert.Equal("Voided", receipt.Status);
            Assert.Equal(180m, receipt.TaxAmount); Assert.Equal(1000m, receipt.FeeAmount); Assert.Equal(1180m, receipt.TotalAmount); Assert.Equal(18m, receipt.TaxRatePercent);
            var history = (await db.Payments().GetPaymentsAsync(TaxDb.SchoolA, "GHS001ST001", default)).Data!.Single();
            Assert.Equal(180m, history.TaxAmount); Assert.Equal("Voided", history.Status);
            // a voided payment can't be voided twice, and its tax no longer counts as collected
            Assert.Equal(404, (await db.Payments().VoidPaymentAsync(TaxDb.SchoolA, paid.PaymentId, TaxDb.UserA, new VoidPaymentRequest { VoidReason = "again" }, default)).StatusCode);
            var dash = (await db.Reports().GetDashboardAsync(TaxDb.SchoolA, TaxDb.YearA, null, null, 7, true, default)).Data!;
            Assert.Equal(0m, dash.TaxCollected); Assert.Empty(dash.TaxByType);
        }

        [DbFact]
        public async Task EditingTheRate_DoesNotChangeOldReceipts_AndNewPaymentsUseTheNewRate()
        {
            using var db = new TaxDb();
            var gst = await db.NewTax(TaxDb.SchoolA, "GST", "GST", 5);
            var old = (await db.Pay("GHS001ST001", 1000, gst.TaxTypeId)).Data!;
            Assert.Equal(50m, old.TaxAmount);
            var up = await db.Taxes().UpdateAsync(TaxDb.SchoolA, gst.TaxTypeId, new TaxTypeRequest { TaxName = "Goods Tax", TaxCode = "GT", RatePercent = 12, Description = "renamed" }, default);
            Assert.True(up.Status);
            var again = (await db.Payments().GetReceiptAsync(TaxDb.SchoolA, old.PaymentId, default)).Data!;
            Assert.Equal(50m, again.TaxAmount); Assert.Equal(5m, again.TaxRatePercent); Assert.Equal("GST", again.TaxName); Assert.Equal("GST", again.TaxCode); Assert.Equal(1050m, again.TotalAmount);
            var fresh = (await db.Pay("GHS001ST002", 1000, gst.TaxTypeId)).Data!;
            Assert.Equal(120m, fresh.TaxAmount); Assert.Equal(12m, fresh.TaxRatePercent); Assert.Equal("Goods Tax", fresh.TaxName);
        }

        [DbFact]
        public async Task DeactivatedType_IsRefusedForNewPayments_ButStaysOnOldReceipts_AndTheTypeCanBeReactivated()
        {
            using var db = new TaxDb();
            var gst = await db.NewTax(TaxDb.SchoolA, "GST", "GST", 5);
            var old = (await db.Pay("GHS001ST001", 1000, gst.TaxTypeId)).Data!;
            Assert.True((await db.Taxes().SetActiveAsync(TaxDb.SchoolA, gst.TaxTypeId, false, default)).Status);   // allowed although it has payments
            var refused = await db.Pay("GHS001ST002", 1000, gst.TaxTypeId);
            Assert.Equal(400, refused.StatusCode);
            Assert.Contains("deactivated", refused.Message);
            Assert.Equal(1, db.Scalar<int>("select count(*) from tb_fee_payments where school_id = '" + TaxDb.SchoolA + "'"));
            var receipt = (await db.Payments().GetReceiptAsync(TaxDb.SchoolA, old.PaymentId, default)).Data!;
            Assert.Equal("GST", receipt.TaxName); Assert.Equal(50m, receipt.TaxAmount);
            Assert.True((await db.Taxes().SetActiveAsync(TaxDb.SchoolA, gst.TaxTypeId, true, default)).Status);
            Assert.Equal(201, (await db.Pay("GHS001ST002", 1000, gst.TaxTypeId)).StatusCode);
        }

        [DbFact]
        public async Task FeeDashboard_CollectedExcludesTax_TaxCollectedMatchesIndependentSql_PerTypeBreakdown()
        {
            using var db = new TaxDb();
            var gst = await db.NewTax(TaxDb.SchoolA, "GST", "GST", 18);
            var cess = await db.NewTax(TaxDb.SchoolA, "Cess", "CESS", 5);
            await db.Pay("GHS001ST001", 1000, gst.TaxTypeId);
            await db.Pay("GHS001ST002", 333.33m, cess.TaxTypeId);
            await db.Pay("GHS001ST003", 700);                    // no tax
            var voided = (await db.Pay("GHS001ST004", 500, gst.TaxTypeId)).Data!;
            await db.Payments().VoidPaymentAsync(TaxDb.SchoolA, voided.PaymentId, TaxDb.UserA, new VoidPaymentRequest { VoidReason = "x" }, default);
            await db.Pay("GHS001ST005", 100, gst.TaxTypeId, school: TaxDb.SchoolA);

            var dash = (await db.Reports().GetDashboardAsync(TaxDb.SchoolA, TaxDb.YearA, null, null, 7, true, default)).Data!;
            var sqlTax = db.Scalar<decimal>($"select coalesce(sum(tax_amount),0) from tb_fee_payments where school_id='{TaxDb.SchoolA}' and status='Active'");
            var sqlFee = db.Scalar<decimal>($"select coalesce(sum(allocated_amount),0) from tb_fee_payment_allocations a join tb_fee_payments p using (payment_id) where p.school_id='{TaxDb.SchoolA}' and p.status='Active'");
            var sqlPaid = db.Scalar<decimal>($"select coalesce(sum(d.amount_paid),0) from tb_student_fee_dues d join tb_student_fee_items i using (student_fee_item_id) where i.school_id='{TaxDb.SchoolA}' and i.academic_year_id={TaxDb.YearA}");
            Assert.Equal(sqlTax, dash.TaxCollected);
            Assert.Equal(180m + 16.67m + 18m, dash.TaxCollected);
            Assert.Equal(sqlPaid, dash.TotalCollected);                     // fee only
            Assert.Equal(sqlFee, dash.TotalCollected);
            Assert.Equal(1000m + 333.33m + 700m + 100m, dash.TotalCollected);
            Assert.Equal(dash.TaxCollected, dash.TaxByType.Sum(t => t.TaxAmount));
            Assert.Equal(198m, dash.TaxByType.Single(t => t.TaxCode == "GST").TaxAmount);
            Assert.Equal(2, dash.TaxByType.Single(t => t.TaxCode == "GST").PaymentCount);
            Assert.Equal(16.67m, dash.TaxByType.Single(t => t.TaxCode == "CESS").TaxAmount);
            Assert.Equal(dash.CollectionByMonth.Sum(m => m.Amount), dash.TotalCollected);   // the month chart stays fee only too
        }

        [DbFact]
        public async Task ClassReport_StaysFeeOnly()
        {
            using var db = new TaxDb();
            var gst = await db.NewTax(TaxDb.SchoolA, "GST", "GST", 18);
            await db.Pay("GHS001ST001", 1000, gst.TaxTypeId);
            var classId = db.Scalar<long>($"select s.class_id from tb_student_enrollments e join tb_sections s on s.section_id = e.class_section_id where e.student_id='GHS001ST001' and e.academic_year_id={TaxDb.YearA} limit 1");
            var rep = (await db.Reports().GetClassReportAsync(TaxDb.SchoolA, classId, TaxDb.YearA, null, true, default)).Data!;
            Assert.Equal(1000m, rep.Students.Single(s => s.StudentId == "GHS001ST001").TotalCollected);
            Assert.Equal(1000m, rep.TotalCollected);
        }

        [DbFact]
        public async Task Concurrency_SixSimultaneousPaymentsWithTax_GetSixDistinctReceiptNumbers_AndConsistentTotals()
        {
            using var db = new TaxDb();
            var gst = await db.NewTax(TaxDb.SchoolA, "GST", "GST", 18);
            // six different students of one school at the same moment (they all race for the school's next receipt number)
            var students = Enumerable.Range(1, 6).Select(i => $"GHS001ST{i:D3}").ToArray();
            var results = await Task.WhenAll(students.Select(s => Task.Run(() => db.Pay(s, 1000, gst.TaxTypeId))));
            Assert.All(results, r => Assert.True(r.Status, r.Message));
            Assert.Equal(6, results.Select(r => r.Data!.ReceiptNumber).Distinct().Count());
            Assert.Equal(6, db.Scalar<int>($"select count(distinct receipt_number) from tb_fee_payments where school_id='{TaxDb.SchoolA}'"));
            Assert.Equal(6000m, db.Scalar<decimal>($"select sum(fee_amount) from tb_fee_payments where school_id='{TaxDb.SchoolA}'"));
            Assert.Equal(1080m, db.Scalar<decimal>($"select sum(tax_amount) from tb_fee_payments where school_id='{TaxDb.SchoolA}'"));
            Assert.Equal(7080m, db.Scalar<decimal>($"select sum(amount) from tb_fee_payments where school_id='{TaxDb.SchoolA}'"));
            Assert.Equal(6000m, db.Scalar<decimal>($"select sum(allocated_amount) from tb_fee_payment_allocations a join tb_fee_payments p using (payment_id) where p.school_id='{TaxDb.SchoolA}'"));
            Assert.Equal(6000m, db.Scalar<decimal>($"select sum(amount_paid) from tb_student_fee_dues where school_id='{TaxDb.SchoolA}' and student_id = any(array['{string.Join("','", students)}'])"));
        }

        [DbFact]
        public async Task Concurrency_SixSimultaneousPaymentsForOneStudent_NeverOverpayADue()
        {
            using var db = new TaxDb();
            var gst = await db.NewTax(TaxDb.SchoolA, "GST", "GST", 18);
            var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => db.Pay("GHS001ST007", 1000, gst.TaxTypeId))));
            Assert.All(results, r => Assert.True(r.Status, r.Message));
            Assert.Equal(6, results.Select(r => r.Data!.ReceiptNumber).Distinct().Count());
            Assert.Equal(6000m, db.Dues("GHS001ST007").Sum(d => d.Paid));
            Assert.All(db.Dues("GHS001ST007"), d => Assert.True(d.Paid <= d.Due));
            Assert.Equal(6000m, db.Scalar<decimal>($"select sum(allocated_amount) from tb_fee_payment_allocations a join tb_fee_payments p using (payment_id) where p.student_id='GHS001ST007'"));
        }

        [DbFact]
        public async Task DatabaseRefusesInconsistentPayments()
        {
            using var db = new TaxDb();
            var ex1 = Assert.ThrowsAny<Exception>(() => db.Exec($"update tb_fee_payments set tax_amount = 1 where payment_id = (select min(payment_id) from tb_fee_payments)"));   // amount <> fee + tax
            Assert.Contains("chk_tb_fee_payments_total", ex1.ToString());
            Assert.ThrowsAny<Exception>(() => db.Exec("update tb_fee_payments set tax_amount = -5, amount = amount - 5 where payment_id = (select min(payment_id) from tb_fee_payments)"));
            var gst = await db.NewTax(TaxDb.SchoolB, "GST", "GST", 5);
            Assert.ThrowsAny<Exception>(() => db.Exec($"update tb_fee_payments set tax_type_id = {gst.TaxTypeId} where payment_id = (select min(payment_id) from tb_fee_payments)"));   // no snapshot name
        }
    }

    internal static class TaxTestExtensions
    {
        /// <summary>True when the tax type is still readable in that school's own list.</summary>
        public static async Task<bool> GetByIdOk(this TaxTypeService svc, string school, long id) =>
            (await svc.GetAllAsync(school, true, default)).Data!.Any(t => t.TaxTypeId == id);
    }
}
