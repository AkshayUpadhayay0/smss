using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using smss_api.Controllers;
using smss_api_db_layer.context;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Fee lines with an Effective From date, billing cycles and installment plans, and the explicit back-dating. The first half needs no database (the schedule arithmetic and the authorization
    /// matrix); the [DbFact] half runs on a scratch PostgreSQL (see TaxTestKit). School GHS261103002, structure 17 = the Nursery structure (12 students, ST001 enrolled 3 Nov 2026, the rest in April).
    /// </summary>
    public class FeeEffectiveFromTests
    {
        private const long Nursery = 17;
        private const long Monthly = 2, Quarterly = 3, HalfYearly = 4, Annual = 5, OneTime = 1;
        private static readonly DateOnly YearStart = new(2026, 4, 1);

        // ============================================================== the arithmetic (no database)
        private static string D(FeeSchedule.Scheduled s) => $"{s.Date:yyyy-MM-dd}:{s.Amount}";

        [Fact]
        public void BillingCycle_TheConfiguredAmountIsPerCycle_NotSplitAcrossTheYear()
        {
            var half = FeeSchedule.FullSchedule(YearStart, FeeSchedule.HalfYearly, 10, FeeSchedule.PlanNone, 600m);
            Assert.Equal(new[] { "2026-04-10:600", "2026-10-10:600" }, half.Select(D));                       // two dues of 600, not 100 a month
            var quarter = FeeSchedule.FullSchedule(YearStart, FeeSchedule.Quarterly, 12, FeeSchedule.PlanNone, 600m);
            Assert.Equal(new[] { "2026-04-12:600", "2026-07-12:600", "2026-10-12:600", "2027-01-12:600" }, quarter.Select(D));
            var monthly = FeeSchedule.FullSchedule(YearStart, FeeSchedule.Monthly, 10, FeeSchedule.PlanNone, 600m);
            Assert.Equal(12, monthly.Count); Assert.All(monthly, s => Assert.Equal(600m, s.Amount));
            Assert.Equal(new DateOnly(2027, 3, 10), monthly[^1].Date);
            foreach (var code in new[] { FeeSchedule.Annual, FeeSchedule.OneTime })
            {
                var one = Assert.Single(FeeSchedule.FullSchedule(YearStart, code, 15, FeeSchedule.PlanNone, 600m));    // Annual and One Time: unchanged, one due
                Assert.Equal(600m, one.Amount);
            }
        }

        [Fact]
        public void InstallmentPlans_SplitEachCycleEqually_AndTheYearTotalNeverChanges()
        {
            decimal Total(IEnumerable<FeeSchedule.Scheduled> s) => s.Sum(x => x.Amount);
            var none = FeeSchedule.FullSchedule(YearStart, FeeSchedule.HalfYearly, 10, FeeSchedule.PlanNone, 600m);
            var monthly = FeeSchedule.FullSchedule(YearStart, FeeSchedule.HalfYearly, 10, FeeSchedule.PlanMonthly, 600m);
            var quarterly = FeeSchedule.FullSchedule(YearStart, FeeSchedule.HalfYearly, 10, FeeSchedule.PlanQuarterly, 600m);
            Assert.Equal(12, monthly.Count); Assert.All(monthly, s => Assert.Equal(100m, s.Amount));      // 6 a cycle
            Assert.Equal(new[] { "2026-04-10:300", "2026-07-10:300", "2026-10-10:300", "2027-01-10:300" }, quarterly.Select(D));
            Assert.Equal(Total(none), Total(monthly)); Assert.Equal(Total(none), Total(quarterly)); Assert.Equal(1200m, Total(none));

            var qMonthly = FeeSchedule.FullSchedule(YearStart, FeeSchedule.Quarterly, 12, FeeSchedule.PlanMonthly, 600m);
            Assert.Equal(12, qMonthly.Count); Assert.All(qMonthly, s => Assert.Equal(200m, s.Amount));
            Assert.Equal(new[] { 0, 0, 0, 1, 1, 1 }, qMonthly.Take(6).Select(s => s.Cycle));
        }

        [Fact]
        public void InstallmentPlans_UnevenAmounts_RoundDownToThePaisaWithTheRemainderOnTheLastOfTheCycle()
        {
            // 100 over 3 months (a Quarterly line on a Monthly plan): 33.33, 33.33, 33.34 in every cycle
            var s = FeeSchedule.FullSchedule(YearStart, FeeSchedule.Quarterly, 5, FeeSchedule.PlanMonthly, 100m);
            for (var c = 0; c < 4; c++) Assert.Equal(new[] { 33.33m, 33.33m, 33.34m }, s.Where(x => x.Cycle == c).Select(x => x.Amount));
            Assert.Equal(400m, s.Sum(x => x.Amount));
            // 1000 over 7: 142.85 x 6 and 142.90
            Assert.Equal(new[] { 142.85m, 142.85m, 142.85m, 142.85m, 142.85m, 142.85m, 142.90m }, FeeSchedule.Split(1000m, 7));
            Assert.Equal(1000m, FeeSchedule.Split(1000m, 7).Sum());
            // a Half-Yearly 1000 on a Monthly plan: 166.66 x 5 and 166.70 per cycle
            var h = FeeSchedule.FullSchedule(YearStart, FeeSchedule.HalfYearly, 5, FeeSchedule.PlanMonthly, 1000m);
            Assert.Equal(new[] { 166.66m, 166.66m, 166.66m, 166.66m, 166.66m, 166.70m }, h.Where(x => x.Cycle == 0).Select(x => x.Amount));
            Assert.Equal(2000m, h.Sum(x => x.Amount));
            Assert.Equal(0.01m, FeeSchedule.Split(0.01m, 3).Sum());   // a tiny amount still adds up (the paisa lands on the last installment)
        }

        [Fact]
        public void AllowedPlans_MatchTheDecision()
        {
            Assert.Equal(new[] { "NONE", "QUARTERLY", "MONTHLY" }, FeeSchedule.AllowedPlans(FeeSchedule.HalfYearly));
            Assert.Equal(new[] { "NONE", "MONTHLY" }, FeeSchedule.AllowedPlans(FeeSchedule.Quarterly));
            foreach (var code in new[] { FeeSchedule.Monthly, FeeSchedule.Annual, FeeSchedule.OneTime }) Assert.Equal(new[] { "NONE" }, FeeSchedule.AllowedPlans(code));
        }

        [Fact]
        public void Owed_StartsAtTheFirstDueOnOrAfterTheStart_SingleChargesAreDatedFromIt_AndNothingLeftMeansOneInstallmentAtTheStart()
        {
            var monthly = FeeSchedule.FullSchedule(YearStart, FeeSchedule.Monthly, 10, FeeSchedule.PlanNone, 600m);
            var from = FeeSchedule.Owed(monthly, new DateOnly(2026, 9, 15), false);
            Assert.Equal(new DateOnly(2026, 10, 10), from[0].Date); Assert.Equal(6, from.Count);              // a September start owes October onwards
            Assert.Equal(new DateOnly(2026, 9, 10), FeeSchedule.Owed(monthly, new DateOnly(2026, 9, 10), false)[0].Date);   // ON the due date counts

            var half = FeeSchedule.FullSchedule(YearStart, FeeSchedule.HalfYearly, 10, FeeSchedule.PlanNone, 600m);
            var late = Assert.Single(FeeSchedule.Owed(half, new DateOnly(2026, 11, 20), false));              // both dates passed: one installment at the start
            Assert.Equal((new DateOnly(2026, 11, 20), 600m), (late.Date, late.Amount));

            var annual = FeeSchedule.FullSchedule(YearStart, FeeSchedule.Annual, 15, FeeSchedule.PlanNone, 500m);
            Assert.Equal(new DateOnly(2026, 11, 20), Assert.Single(FeeSchedule.Owed(annual, new DateOnly(2026, 11, 20), true)).Date);   // dated from the start
            Assert.Equal(new DateOnly(2026, 4, 15), Assert.Single(FeeSchedule.Owed(annual, new DateOnly(2026, 4, 1), true)).Date);
        }

        // ============================================================== authorization matrix (no database)
        private static readonly string[] Others = { "Accountant", "Principal", "Teacher", "Student", "Guardian", "Admission Staff", "Super Admin", "Driver" };

        private static async Task<int> Backdating(string call, params string[] roles)
        {
            var svc = Fake.Of<IFeeBackdatingService>(new()
            {
                ["PreviewAsync"] = Fake.Ok(new BackdatingPreviewDto()), ["ApplyAsync"] = Fake.Ok(new BackdatingResultDto())
            });
            var c = Fake.WithUser(new FeeStructureController(Fake.Of<IFeeStructureService>(), svc), Fake.User("SCH1", 7, roles));
            IActionResult r = call == "preview"
                ? await c.PreviewBackdating(1, 2, new BackdatingPreviewRequest { FromDate = new DateOnly(2026, 4, 1) }, default)
                : await c.ApplyBackdating(1, 2, new ApplyBackdatingRequest { FromDate = new DateOnly(2026, 4, 1), Reason = "x", Confirm = true }, default);
            var code = Fake.Code(r);
            if (code == 403) Assert.Empty(Fake.ProxyOf(svc).Calls);
            return code;
        }

        [Theory]
        [InlineData("preview")]
        [InlineData("apply")]
        public async Task BackdatingEndpoints_AreSchoolAdminOnly_EveryoneElse403(string call)
        {
            Assert.Equal(200, await Backdating(call, "School Admin"));
            foreach (var r in Others) Assert.True(403 == await Backdating(call, r), $"{r} must be refused");
            Assert.Equal(403, await Backdating(call));
        }

        [Fact]
        public async Task StructureSaveAndItemEdit_StayFeeConfigurationEndpoints_SchoolAdminOnly()
        {
            foreach (var r in Others)
            {
                var c = Fake.WithUser(new FeeStructureController(Fake.Of<IFeeStructureService>(), Fake.Of<IFeeBackdatingService>()), Fake.User("SCH1", 7, r));
                Assert.True(403 == Fake.Code(await c.Update(1, new UpdateFeeStructureRequest { StructureName = "x" })), $"structure save: {r}");
                var sf = Fake.WithUser(new StudentFeeController(Fake.Of<IStudentFeeService>(), Fake.Of<IStudentFeeBillingService>()), Fake.User("SCH1", 7, r));
                Assert.True(403 == Fake.Code(await sf.UpdateFeeItem("GHS001ST001", 1, new UpdateFeeItemRequest { InstallmentPlan = "MONTHLY" }, default)), $"item edit: {r}");
            }
        }

        [Fact]
        public void FeeStructureController_RequiresALogin_AnonymousIs401()
        {
            Assert.NotEmpty(typeof(FeeStructureController).GetCustomAttributes<AuthorizeAttribute>());
            Assert.Empty(typeof(FeeStructureController).GetCustomAttributes<AllowAnonymousAttribute>());
            foreach (var m in typeof(FeeStructureController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) Assert.Empty(m.GetCustomAttributes<AllowAnonymousAttribute>());
        }

        [Fact]
        public void TheRequestsHaveNoFrequencyOverride_AStudentHasNoFrequencyOfTheirOwn()
        {
            Assert.DoesNotContain(typeof(FeeItemSelection).GetProperties(), p => p.Name == "FrequencyId");
            Assert.DoesNotContain(typeof(UpdateFeeItemRequest).GetProperties(), p => p.Name == "FrequencyId");
            Assert.DoesNotContain(typeof(StudentFeeItemDto).GetProperties(), p => p.Name == "AllowedFrequencies");
        }

        // ============================================================== helpers (scratch PostgreSQL)
        private sealed class SpyLogger<T> : ILogger<T>
        {
            public List<string> Errors { get; } = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            { if (logLevel >= LogLevel.Error) Errors.Add(formatter(state, exception) + (exception == null ? "" : " :: " + exception)); }
        }

        private sealed class Rig : IDisposable
        {
            public readonly TaxDb Db;
            public readonly dbContext C;
            public readonly SpyLogger<StudentFeeBillingService> Spy = new();
            public readonly SpyLogger<FeeStructureService> StructureSpy = new();
            public readonly StudentFeeBillingService Billing;
            public readonly FeeStructureService Structures;
            public readonly FeeBackdatingService Backdating;
            public readonly DateOnly Today = SchoolClock.Today();

            public Rig(TaxDb db)
            {
                Db = db;
                C = db.Ctx();
                Billing = new StudentFeeBillingService(new StudentFeeBillingRepository(C), new StudentFeeRepository(C), Spy);
                Structures = new FeeStructureService(new FeeStructureRepository(C), Billing, StructureSpy);
                Backdating = new FeeBackdatingService(new StudentFeeBillingRepository(C), new FeeStructureRepository(C), NullLogger<FeeBackdatingService>.Instance);
            }
            public void Dispose() => C.Dispose();
        }

        /// <summary>Makes sure the test year (id 41) covers today and is the current one, so the tests do not depend on the date they run on.</summary>
        private static Rig Open(TaxDb db)
        {
            var today = SchoolClock.Today();
            var start = new DateOnly(today.Month >= 4 ? today.Year : today.Year - 1, 4, 1);
            db.Exec($"update tb_academic_years set start_date='{start:yyyy-MM-dd}', end_date='{start.AddYears(1).AddDays(-1):yyyy-MM-dd}', is_current=true where academic_year_id={TaxDb.YearA}");
            return new Rig(db);
        }

        private static long NewFeeType(TaxDb db, string name, bool optional = false) =>
            db.Scalar<long>($"insert into tb_fee_types (school_id, fee_type_name, fee_type_code, isactive, is_optional) values ('{TaxDb.SchoolA}', '{name}', '{name.ToUpperInvariant().Replace(' ', '_')}', true, {(optional ? "true" : "false")}) returning fee_type_id");

        private static FeeStructureLineRequest Line(long feeTypeId, long frequencyId, decimal amount, int dueDay = 10, DateOnly? effective = null) =>
            new() { FeeTypeId = feeTypeId, FrequencyId = frequencyId, Amount = amount, DueDay = dueDay, EffectiveFrom = effective };

        /// <summary>The structure's current lines as a save request (existing lines keep their effective date), plus the changes.</summary>
        private static async Task<ApiResponse<object>> Save(Rig r, Action<List<FeeStructureLineRequest>> change, bool confirm = false, long structure = Nursery)
        {
            var current = (FeeStructureResponse)(await r.Structures.GetByIdAsync(TaxDb.SchoolA, structure)).Data!;
            var lines = current.Details.Select(d => Line(d.FeeTypeId, d.FrequencyId, d.Amount, d.DueDay ?? 10, d.EffectiveFrom)).ToList();
            change(lines);
            var result = await r.Structures.UpdateAsync(TaxDb.SchoolA, structure, new UpdateFeeStructureRequest { StructureName = current.StructureName, Details = lines, ConfirmDueRemoval = confirm });
            if (result.StatusCode == 500) throw new InvalidOperationException("structure save failed: " + string.Join(" | ", r.StructureSpy.Errors));
            return result;
        }

        private sealed record DueRow(long Id, DateOnly Date, decimal Amount, decimal Paid, string Source, long? Log);

        private static List<DueRow> DuesOf(TaxDb db, string student, long feeTypeId)
        {
            var list = new List<DueRow>();
            using var c = new NpgsqlConnection(db.Conn); c.Open();
            using var cmd = new NpgsqlCommand($@"select d.fee_due_id, d.due_date, d.amount_due, d.amount_paid, d.source, d.backdating_log_id from tb_student_fee_dues d
                join tb_student_fee_items i on i.student_fee_item_id=d.student_fee_item_id where i.student_id='{student}' and i.fee_type_id={feeTypeId} order by d.due_date, d.fee_due_id", c);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(new DueRow(r.GetInt64(0), DateOnly.FromDateTime(r.GetDateTime(1)), r.GetDecimal(2), r.GetDecimal(3), r.GetString(4), r.IsDBNull(5) ? null : r.GetInt64(5)));
            return list;
        }

        private static string[] NurseryStudents(TaxDb db)
        {
            using var c = new NpgsqlConnection(db.Conn); c.Open();
            using var cmd = new NpgsqlCommand($"select student_id from tb_student_fee_structure where fee_structure_id={Nursery} order by student_id", c);
            using var r = cmd.ExecuteReader();
            var l = new List<string>(); while (r.Read()) l.Add(r.GetString(0)); return l.ToArray();
        }

        private static string Snapshot(TaxDb db, string? where = null)
        {
            using var c = new NpgsqlConnection(db.Conn); c.Open();
            using var cmd = new NpgsqlCommand($"select string_agg(fee_due_id||'|'||student_fee_item_id||'|'||due_date||'|'||amount_due||'|'||amount_paid||'|'||source, ';' order by fee_due_id) from tb_student_fee_dues {where}", c);
            return (string?)cmd.ExecuteScalar() ?? "";
        }

        // ============================================================== Problem 1: the September example
        [DbFact]
        public async Task SeptemberExample_AFeeAddedNowGivesNoDuesBeforeToday_ForStudentsAlreadyAssigned()
        {
            using var db = new TaxDb(); using var r = Open(db);
            var sports = NewFeeType(db, "Sports Fee");
            var res = await Save(r, l => l.Add(Line(sports, HalfYearly, 600, 10)));     // no effective date: defaults to today because the structure already has students
            Assert.Equal(200, res.StatusCode);
            var line = ((FeeStructureResponse)(await r.Structures.GetByIdAsync(TaxDb.SchoolA, Nursery)).Data!).Details.Single(d => d.FeeTypeId == sports);
            Assert.Equal(r.Today, line.EffectiveFrom);
            foreach (var s in NurseryStudents(db))
            {
                var dues = DuesOf(db, s, sports);
                Assert.NotEmpty(dues);
                Assert.All(dues, d => { Assert.True(d.Date >= r.Today, $"{s}: a due dated {d.Date} before today {r.Today}"); Assert.Equal("AUTO", d.Source); });
            }
            // an Apr/Oct Half-Yearly schedule with both dates behind us: ONE installment of the normal 600, dated today
            var one = Assert.Single(DuesOf(db, "GHS001ST002", sports));
            Assert.Equal((r.Today, 600m), (one.Date, one.Amount));
            // the same with an effective date at the start of the year: still nothing before today (only the explicit back-dating can create those)
            var swim = NewFeeType(db, "Swim Fee");
            Assert.Equal(200, (await Save(r, l => l.Add(Line(swim, FeeSchedule_Monthly(), 600, 10, r.Today.AddMonths(-7) < YearStart ? YearStart : new DateOnly(r.Today.Year >= 2027 ? 2026 : r.Today.Year, 4, 1))))).StatusCode);
            Assert.All(NurseryStudents(db), s => Assert.All(DuesOf(db, s, swim), d => Assert.True(d.Date >= r.Today)));
        }
        private static long FeeSchedule_Monthly() => Monthly;

        [DbFact]
        public async Task MidYearAdmission_DuesStartAtTheFirstDueOnOrAfterTheLaterOfEffectiveDateAdmissionAndToday()
        {
            using var db = new TaxDb(); using var r = Open(db);
            var student = "GHS001ST005";
            db.Exec($"delete from tb_student_fee_dues where student_id='{student}'; delete from tb_student_fee_items where student_id='{student}'");
            db.Exec($"update tb_student_enrollments set enrollment_date='{r.Today.AddMonths(-2):yyyy-MM-dd}' where student_id='{student}' and academic_year_id={TaxDb.YearA}");   // admitted two months ago
            await r.Billing.SyncAsync(TaxDb.SchoolA, student, TaxDb.YearA, null, default);
            var tuition = db.Scalar<long>($"select fee_type_id from tb_fee_types where school_id='{TaxDb.SchoolA}' and fee_type_name='Tuition Fee'");
            var dues = DuesOf(db, student, tuition);
            Assert.NotEmpty(dues);
            Assert.All(dues, d => Assert.True(d.Date >= r.Today));                                   // nothing for the two months already gone
            Assert.All(dues, d => Assert.Equal(1800m, d.Amount));                                    // the same amount as everyone, just fewer of them
            Assert.Equal(dues.Select(d => d.Date).Distinct().Count(), dues.Count);
        }

        [DbFact]
        public async Task ExistingSchedules_AreUntouchedBySyncingTheWholeStructure()
        {
            using var db = new TaxDb(); using var r = Open(db);
            var before = Snapshot(db);
            await r.Billing.SyncStructureAsync(TaxDb.SchoolA, Nursery, default);
            Assert.Equal(before, Snapshot(db));          // every due: same id, date, amount, paid, source
        }

        [DbFact]
        public async Task EffectiveFrom_MustBeInsideTheYear_AfterTheYearEndIsRefused_OmittedKeepsTheDate()
        {
            using var db = new TaxDb(); using var r = Open(db);
            var year = db.Scalar<string>($"select end_date::text from tb_academic_years where academic_year_id={TaxDb.YearA}");
            var end = DateOnly.Parse(year);
            var fee = NewFeeType(db, "Late Fee");
            var after = await Save(r, l => l.Add(Line(fee, Monthly, 100, 10, end.AddDays(1))));
            Assert.Equal(400, after.StatusCode); Assert.Contains("after the academic year ends", after.Message);
            var before = await Save(r, l => l.Add(Line(fee, Monthly, 100, 10, new DateOnly(2000, 1, 1))));
            Assert.Equal(400, before.StatusCode); Assert.Contains("before the academic year starts", before.Message);
            Assert.Equal(200, (await Save(r, l => l.Add(Line(fee, Monthly, 100, 10, end)))).StatusCode);   // the last day of the year is fine
            // editing another line leaves this one's date alone (omitted = keep)
            var kept = ((FeeStructureResponse)(await r.Structures.GetByIdAsync(TaxDb.SchoolA, Nursery)).Data!).Details.Single(d => d.FeeTypeId == fee).EffectiveFrom;
            Assert.Equal(end, kept);
            Assert.Equal(200, (await Save(r, l => { var x = l.First(); l[0] = new FeeStructureLineRequest { FeeTypeId = x.FeeTypeId, FrequencyId = x.FrequencyId, Amount = x.Amount, DueDay = x.DueDay }; })).StatusCode);
        }

        // ============================================================== editing rules
        [DbFact]
        public async Task ChangingALinesFrequency_IsRefusedOnceStudentsHaveDues_AndNothingMoves()
        {
            using var db = new TaxDb(); using var r = Open(db);
            var sports = NewFeeType(db, "Sports Fee");
            Assert.Equal(200, (await Save(r, l => l.Add(Line(sports, Monthly, 600, 10)))).StatusCode);
            var before = Snapshot(db);
            var res = await Save(r, l => { var i = l.FindIndex(x => x.FeeTypeId == sports); l[i] = Line(sports, HalfYearly, 600, 10); });
            Assert.Equal(409, res.StatusCode);
            Assert.Contains("can't be changed", res.Message); Assert.Contains("Add a new line", res.Message);
            Assert.Equal(before, Snapshot(db));
            Assert.Equal(Monthly, db.Scalar<long>($"select frequency_id from tb_fee_structure_details where fee_structure_id={Nursery} and fee_type_id={sports}"));
            // a line nobody has dues for (an optional fee nobody opted into) can still change its frequency
            var opt = NewFeeType(db, "Trip Fee", optional: true);
            Assert.Equal(200, (await Save(r, l => l.Add(Line(opt, Monthly, 300, 10)))).StatusCode);
            Assert.Equal(0, db.Scalar<long>($"select count(*) from tb_student_fee_dues d join tb_student_fee_items i on i.student_fee_item_id=d.student_fee_item_id where i.fee_type_id={opt}"));
            Assert.Equal(200, (await Save(r, l => { var i = l.FindIndex(x => x.FeeTypeId == opt); l[i] = Line(opt, HalfYearly, 300, 10); })).StatusCode);
        }

        [DbFact]
        public async Task MovingEffectiveFromLater_NeedsAConfirmation_RemovesOnlyUnpaidDuesBefore_AndIsRefusedOncePaid()
        {
            using var db = new TaxDb(); using var r = Open(db);
            var fee = NewFeeType(db, "Lab Fee");
            Assert.Equal(200, (await Save(r, l => l.Add(Line(fee, Monthly, 500, 10, r.Today)))).StatusCode);   // dues from the first due date on or after today
            var s = "GHS001ST002";
            var all = DuesOf(db, s, fee);
            var later = all[2].Date.AddDays(-1);                      // the third due and later survive
            var asked = await Save(r, l => { var i = l.FindIndex(x => x.FeeTypeId == fee); l[i] = Line(fee, Monthly, 500, 10, later); });
            Assert.Equal(409, asked.StatusCode);
            var data = System.Text.Json.JsonSerializer.Serialize(asked.Data);
            Assert.Contains("\"confirmationRequired\":true", data);
            Assert.Equal(all.Count, DuesOf(db, s, fee).Count);        // nothing was removed yet
            var ok = await Save(r, l => { var i = l.FindIndex(x => x.FeeTypeId == fee); l[i] = Line(fee, Monthly, 500, 10, later); }, confirm: true);
            Assert.Equal(200, ok.StatusCode);
            Assert.Equal(all.Skip(2).Select(d => d.Date), DuesOf(db, s, fee).Select(d => d.Date));       // the unpaid dues before the new date are gone, the rest stay

            // once a due of the line has a payment the date can't move later any more
            var paid = DuesOf(db, s, fee)[0];
            Assert.Equal(201, (await db.Pay(s, 100, allocations: new() { new PaymentAllocationRequest { FeeDueId = paid.Id, Amount = 100 } })).StatusCode);
            var refused = await Save(r, l => { var i = l.FindIndex(x => x.FeeTypeId == fee); l[i] = Line(fee, Monthly, 500, 10, DuesOf(db, s, fee)[1].Date); }, confirm: true);
            Assert.Equal(409, refused.StatusCode); Assert.Contains("payments", refused.Message);
            // moving it EARLIER never creates dues by itself
            var count = DuesOf(db, s, fee).Count;
            Assert.Equal(200, (await Save(r, l => { var i = l.FindIndex(x => x.FeeTypeId == fee); l[i] = Line(fee, Monthly, 500, 10, YearStart > r.Today ? r.Today : new DateOnly(r.Today.Month >= 4 ? r.Today.Year : r.Today.Year - 1, 4, 1)); })).StatusCode);
            Assert.Equal(count, DuesOf(db, s, fee).Count);
        }

        [DbFact]
        public async Task APartPaidFee_IsNeverTouched_ByAmountEditsPlanChangesOrSync()
        {
            using var db = new TaxDb(); using var r = Open(db);
            var fee = NewFeeType(db, "Lab Fee");
            Assert.Equal(200, (await Save(r, l => l.Add(Line(fee, HalfYearly, 600, 10, r.Today)))).StatusCode);
            var s = "GHS001ST002";
            var due = Assert.Single(DuesOf(db, s, fee));
            Assert.Equal(201, (await db.Pay(s, 250, allocations: new() { new PaymentAllocationRequest { FeeDueId = due.Id, Amount = 250 } })).StatusCode);
            var snapshot = Snapshot(db, $"where fee_due_id={due.Id}");

            Assert.Equal(200, (await Save(r, l => { var i = l.FindIndex(x => x.FeeTypeId == fee); l[i] = Line(fee, HalfYearly, 900, 10, r.Today); })).StatusCode);      // amount edit
            Assert.Equal(snapshot, Snapshot(db, $"where fee_due_id={due.Id}"));
            var item = db.Scalar<long>($"select student_fee_item_id from tb_student_fee_items where student_id='{s}' and fee_type_id={fee}");
            Assert.Equal(200, (await r.Billing.UpdateItemAsync(TaxDb.SchoolA, s, item, new UpdateFeeItemRequest { InstallmentPlan = "MONTHLY" }, default)).StatusCode);        // plan change
            Assert.Equal(snapshot, Snapshot(db, $"where fee_due_id={due.Id}"));
            await r.Billing.SyncAsync(TaxDb.SchoolA, s, TaxDb.YearA, null, default);
            await r.Billing.SyncStructureAsync(TaxDb.SchoolA, Nursery, default);
            Assert.Equal(snapshot, Snapshot(db, $"where fee_due_id={due.Id}"));
            Assert.Equal(250m, db.Scalar<decimal>($"select amount_paid from tb_student_fee_dues where fee_due_id={due.Id}"));
        }

        // ============================================================== installment plans on a student's item
        [DbFact]
        public async Task ItemEdit_AnInstallmentPlanIsAllowedOnlyWhereTheLineAllowsIt_AndTheYearTotalNeverChanges()
        {
            using var db = new TaxDb(); using var r = Open(db);
            var half = NewFeeType(db, "Lab Fee");
            var monthlyFee = NewFeeType(db, "Bus Fee");
            Assert.Equal(200, (await Save(r, l => { l.Add(Line(half, HalfYearly, 600, 10, YearStart > r.Today ? r.Today : new DateOnly(r.Today.Month >= 4 ? r.Today.Year : r.Today.Year - 1, 4, 1))); l.Add(Line(monthlyFee, Monthly, 100, 10, r.Today)); })).StatusCode);
            var s = "GHS001ST001";
            long Item(long f) => db.Scalar<long>($"select student_fee_item_id from tb_student_fee_items where student_id='{s}' and fee_type_id={f}");
            var total = (long f) => db.Scalar<decimal>($"select coalesce(sum(d.amount_due),0) from tb_student_fee_dues d join tb_student_fee_items i on i.student_fee_item_id=d.student_fee_item_id where i.student_id='{s}' and i.fee_type_id={f}");

            var bad = await r.Billing.UpdateItemAsync(TaxDb.SchoolA, s, Item(monthlyFee), new UpdateFeeItemRequest { InstallmentPlan = "QUARTERLY" }, default);   // a Monthly line allows NONE only
            Assert.Equal(400, bad.StatusCode); Assert.Contains("installment plan", bad.Message);
            Assert.Equal(400, (await r.Billing.UpdateItemAsync(TaxDb.SchoolA, s, Item(half), new UpdateFeeItemRequest { InstallmentPlan = "WEEKLY" }, default)).StatusCode);

            var before = total(half);
            var ok = await r.Billing.UpdateItemAsync(TaxDb.SchoolA, s, Item(half), new UpdateFeeItemRequest { InstallmentPlan = "MONTHLY" }, default);
            Assert.Equal(200, ok.StatusCode);
            var item = ok.Data!.Items.Single(i => i.FeeTypeId == half);
            Assert.Equal(("MONTHLY", "Half-Yearly"), (item.InstallmentPlan, item.FrequencyName));
            Assert.Equal(new[] { "NONE", "QUARTERLY", "MONTHLY" }, item.AllowedPlans.Select(p => p.Code));
            Assert.Equal(new[] { "NONE" }, ok.Data.Items.Single(i => i.FeeTypeId == monthlyFee).AllowedPlans.Select(p => p.Code));
            Assert.Equal(before, total(half));                                           // the year total never changes with the plan
            // monthly pieces: every future due is ONE monthly installment of the cycle's amount
            var dues = DuesOf(db, s, half);
            Assert.Equal(dues.Count, dues.Select(d => d.Date.Year * 12 + d.Date.Month).Distinct().Count());
        }

        [DbFact]
        public async Task ChangingThePlanAfterACycleWasBilled_NeverBillsThatCycleTwice()
        {
            using var db = new TaxDb(); using var r = Open(db);
            var fee = NewFeeType(db, "Lab Fee");
            var start = new DateOnly(r.Today.Month >= 4 ? r.Today.Year : r.Today.Year - 1, 4, 1);
            Assert.Equal(200, (await Save(r, l => l.Add(Line(fee, HalfYearly, 600, 1, r.Today)))).StatusCode);   // a one-installment catch-up dated today (both dates passed) or the coming half
            var s = "GHS001ST002";
            long item = db.Scalar<long>($"select student_fee_item_id from tb_student_fee_items where student_id='{s}' and fee_type_id={fee}");
            var before = DuesOf(db, s, fee).Sum(d => d.Amount);
            await r.Billing.UpdateItemAsync(TaxDb.SchoolA, s, item, new UpdateFeeItemRequest { InstallmentPlan = "MONTHLY" }, default);
            Assert.Equal(before, DuesOf(db, s, fee).Sum(d => d.Amount));
            await r.Billing.UpdateItemAsync(TaxDb.SchoolA, s, item, new UpdateFeeItemRequest { InstallmentPlan = "NONE" }, default);
            Assert.Equal(before, DuesOf(db, s, fee).Sum(d => d.Amount));
        }

        [DbFact]
        public async Task TheShrinkingTotalDefect_IsGone_AGuardRefusesAndLogsInsteadOfDeletingDues()
        {
            using var db = new TaxDb(); using var r = Open(db);
            var s = "GHS001ST002";
            var exam = db.Scalar<long>($"select fee_type_id from tb_fee_types where school_id='{TaxDb.SchoolA}' and fee_type_name='Exam Fee'");   // a Half-Yearly line
            long item = db.Scalar<long>($"select student_fee_item_id from tb_student_fee_items where student_id='{s}' and fee_type_id={exam}");

            // today's half-year cycle: where the last month after today is
            var yearStart = YearStartOf(r);
            var months = (r.Today.Year * 12 + r.Today.Month) - (yearStart.Year * 12 + yearStart.Month);
            var cycleLast = yearStart.AddMonths((months / 6 + 1) * 6 - 1);
            var future = new DateOnly(cycleLast.Year, cycleLast.Month, 25);
            if (future <= r.Today) return;                                              // today is in the last month of its cycle: nothing left to test on this date

            // the old defect: the cycle already had MORE billed than its amount, so "what is left" went negative and the unpaid future dues were deleted
            db.Exec($"update tb_student_fee_items set installment_plan='MONTHLY' where student_fee_item_id={item}");
            db.Exec($"insert into tb_student_fee_dues (student_fee_item_id, student_id, school_id, due_date, amount_due, amount_paid) values ({item}, '{s}', '{TaxDb.SchoolA}', '{r.Today:yyyy-MM-dd}', 5000, 0)");
            db.Exec($"insert into tb_student_fee_dues (student_fee_item_id, student_id, school_id, due_date, amount_due, amount_paid) values ({item}, '{s}', '{TaxDb.SchoolA}', '{future:yyyy-MM-dd}', 100, 0)");
            var snapshot = Snapshot(db, $"where student_fee_item_id={item}");

            await r.Billing.SyncAsync(TaxDb.SchoolA, s, TaxDb.YearA, null, default);
            Assert.Equal(snapshot, Snapshot(db, $"where student_fee_item_id={item}"));      // nothing deleted, nothing changed
            Assert.Contains(r.Spy.Errors, e => e.Contains("already billed"));                // and the refusal is logged
        }

        [DbFact]
        public async Task TheGetHealPath_RespectsTheEffectiveDate()
        {
            using var db = new TaxDb(); using var r = Open(db);
            var fee = NewFeeType(db, "Lab Fee");
            Assert.Equal(200, (await Save(r, l => l.Add(Line(fee, Monthly, 400, 10, r.Today.AddDays(40) <= new DateOnly(r.Today.Year + 1, 3, 31) ? r.Today.AddDays(40) : r.Today)))).StatusCode);
            var effective = ((FeeStructureResponse)(await r.Structures.GetByIdAsync(TaxDb.SchoolA, Nursery)).Data!).Details.Single(d => d.FeeTypeId == fee).EffectiveFrom;
            var s = "GHS001ST003";
            db.Exec($"delete from tb_student_fee_dues where student_id='{s}'; delete from tb_student_fee_items where student_id='{s}'");     // an assigned student without items: the Fees tab heals it on read
            var read = await r.Billing.GetItemsAsync(TaxDb.SchoolA, s, default);
            Assert.Equal(200, read.StatusCode);
            var dues = DuesOf(db, s, fee);
            Assert.All(dues, d => { Assert.True(d.Date >= effective); Assert.True(d.Date >= r.Today); });
        }

        [DbFact]
        public async Task ANewLine_WithAConcessionAndATax_BillsTheNetAndTheTaxIsAddedAtPayment()
        {
            using var db = new TaxDb(); using var r = Open(db);
            var fee = NewFeeType(db, "Lab Fee");
            var concession = db.Scalar<long>($"insert into tb_fee_concessions (school_id, concession_name, concession_code, concession_type, value, isactive) values ('{TaxDb.SchoolA}', 'Ten', 'T10', 'PERCENTAGE', 10, true) returning concession_id");
            db.Exec($"insert into tb_student_fee_concessions (student_id, concession_id, approved_by) values ('GHS001ST002', {concession}, {TaxDb.UserA})");
            Assert.Equal(200, (await Save(r, l => l.Add(Line(fee, Monthly, 1000, 10, r.Today)))).StatusCode);
            var dues = DuesOf(db, "GHS001ST002", fee);
            Assert.NotEmpty(dues);
            Assert.All(dues, d => Assert.Equal(900m, d.Amount));                                     // the concession already comes off the per-cycle amount
            Assert.All(DuesOf(db, "GHS001ST003", fee), d => Assert.Equal(1000m, d.Amount));
            var gst = await db.NewTax(TaxDb.SchoolA, "GST 18%", "GST18", 18);
            var paid = await db.Pay("GHS001ST002", 900, gst.TaxTypeId, new() { new PaymentAllocationRequest { FeeDueId = dues[0].Id, Amount = 900 } });
            Assert.Equal((900m, 162m, 1062m), (paid.Data!.NetFeeAmount, paid.Data.TaxAmount, paid.Data.TotalAmount));
            var after = DuesOf(db, "GHS001ST002", fee)[0];
            Assert.Equal(after.Amount, after.Paid);                                                  // cleared
        }

        // ============================================================== backdating
        private static async Task<(long sports, int studentsAtMost)> AddMonthly(Rig r, TaxDb db)
        {
            var sports = NewFeeType(db, "Sports Fee");
            Assert.Equal(200, (await Save(r, l => l.Add(Line(sports, Monthly, 600, 10)))).StatusCode);   // effective today (students exist)
            return (sports, NurseryStudents(db).Length);
        }

        private static DateOnly YearStartOf(Rig r) => new(r.Today.Month >= 4 ? r.Today.Year : r.Today.Year - 1, 4, 1);

        [DbFact]
        public async Task Backdating_PreviewEqualsWhatIsCreated_NeedsAReasonAndAConfirmation_IsLogged_AndIsIdempotent()
        {
            using var db = new TaxDb(); using var r = Open(db);
            var (sports, _) = await AddMonthly(r, db);
            var from = YearStartOf(r);
            var before = db.Scalar<long>("select count(*) from tb_student_fee_dues");

            var preview = await r.Backdating.PreviewAsync(TaxDb.SchoolA, Nursery, sports, from, default);
            Assert.Equal(200, preview.StatusCode);
            var p = preview.Data!;
            Assert.True(p.DuesCount > 0);
            Assert.Equal(before, db.Scalar<long>("select count(*) from tb_student_fee_dues"));    // a preview writes nothing
            Assert.Equal(p.DuesCount, p.Students.Sum(s => s.DuesCount)); Assert.Equal(p.TotalAmount, p.Students.Sum(s => s.Amount));
            Assert.All(p.Students, s => Assert.Equal(s.DuesCount * 600m, s.Amount));
            // a student enrolled later only owes from their own start: ST001 joined 3 Nov, so at most the dues from then
            var late = p.Students.SingleOrDefault(s => s.StudentId == "GHS001ST001");
            Assert.True(late == null || late.DuesCount <= p.Students.Max(s => s.DuesCount));

            // refused without a reason, without the confirmation
            Assert.Equal(400, (await r.Backdating.ApplyAsync(TaxDb.SchoolA, TaxDb.UserA, Nursery, sports, new ApplyBackdatingRequest { FromDate = from, Reason = "  ", Confirm = true }, default)).StatusCode);
            Assert.Equal(400, (await r.Backdating.ApplyAsync(TaxDb.SchoolA, TaxDb.UserA, Nursery, sports, new ApplyBackdatingRequest { FromDate = from, Reason = "billed late", Confirm = false }, default)).StatusCode);
            Assert.Equal(before, db.Scalar<long>("select count(*) from tb_student_fee_dues"));
            // a preview that no longer matches is refused
            Assert.Equal(409, (await r.Backdating.ApplyAsync(TaxDb.SchoolA, TaxDb.UserA, Nursery, sports, new ApplyBackdatingRequest { FromDate = from, Reason = "x", Confirm = true, ExpectedDuesCount = p.DuesCount + 1 }, default)).StatusCode);

            var applied = await r.Backdating.ApplyAsync(TaxDb.SchoolA, TaxDb.UserA, Nursery, sports, new ApplyBackdatingRequest { FromDate = from, Reason = "Sports fee was agreed in June", Confirm = true, ExpectedDuesCount = p.DuesCount }, default);
            Assert.Equal(200, applied.StatusCode);
            Assert.Equal((p.DuesCount, p.TotalAmount, p.StudentsCount), (applied.Data!.DuesCount, applied.Data.TotalAmount, applied.Data.StudentsCount));   // exactly what the preview said
            Assert.Equal(before + p.DuesCount, db.Scalar<long>("select count(*) from tb_student_fee_dues"));
            var logId = applied.Data.BackdatingLogId!.Value;
            Assert.Equal(p.DuesCount, db.Scalar<long>($"select count(*) from tb_student_fee_dues where source='BACKDATED' and backdating_log_id={logId}"));
            Assert.Equal((TaxDb.UserA, "Sports fee was agreed in June", "LINE", p.DuesCount), (db.Scalar<long>($"select created_by from tb_fee_backdating_log where backdating_log_id={logId}"),
                db.Scalar<string>($"select reason from tb_fee_backdating_log where backdating_log_id={logId}"), db.Scalar<string>($"select kind from tb_fee_backdating_log where backdating_log_id={logId}"),
                (int)db.Scalar<long>($"select dues_count from tb_fee_backdating_log where backdating_log_id={logId}")));
            Assert.Equal(from, applied.Data.EffectiveFrom);                                         // the line is charged from there now
            Assert.All(NurseryStudents(db), s => Assert.All(DuesOf(db, s, sports).Where(d => d.Source == "BACKDATED"), d => Assert.True(d.Date <= r.Today)));

            // idempotent: again creates nothing, and no second log row
            var again = await r.Backdating.ApplyAsync(TaxDb.SchoolA, TaxDb.UserA, Nursery, sports, new ApplyBackdatingRequest { FromDate = from, Reason = "again", Confirm = true }, default);
            Assert.Equal(200, again.StatusCode); Assert.Equal(0, again.Data!.DuesCount); Assert.Null(again.Data.BackdatingLogId);
            Assert.Equal(before + p.DuesCount, db.Scalar<long>("select count(*) from tb_student_fee_dues"));
            Assert.Equal(1, db.Scalar<long>("select count(*) from tb_fee_backdating_log"));
            // and the normal sync afterwards neither removes nor duplicates them
            await r.Billing.SyncStructureAsync(TaxDb.SchoolA, Nursery, default);
            Assert.Equal(before + p.DuesCount, db.Scalar<long>("select count(*) from tb_student_fee_dues"));
            Assert.Equal(0, db.Scalar<long>("select count(*) from (select student_fee_item_id, extract(year from due_date)*12+extract(month from due_date) m from tb_student_fee_dues group by 1,2 having count(*)>1) x"));
        }

        [DbFact]
        public async Task Backdating_IsLimitedToTheCurrentYearAndToEarlierDates_AndAnotherSchoolsStructureIs404()
        {
            using var db = new TaxDb(); using var r = Open(db);
            var (sports, _) = await AddMonthly(r, db);
            var from = YearStartOf(r);
            Assert.Equal(400, (await r.Backdating.PreviewAsync(TaxDb.SchoolA, Nursery, sports, r.Today, default)).StatusCode);                 // today is not "earlier"
            Assert.Equal(400, (await r.Backdating.PreviewAsync(TaxDb.SchoolA, Nursery, sports, r.Today.AddDays(5), default)).StatusCode);
            var tooEarly = await r.Backdating.PreviewAsync(TaxDb.SchoolA, Nursery, sports, from.AddDays(-1), default);
            Assert.Equal(400, tooEarly.StatusCode); Assert.Contains("before the academic year starts", tooEarly.Message);
            Assert.Equal(400, (await r.Backdating.PreviewAsync(TaxDb.SchoolA, Nursery, sports, null, default)).StatusCode);
            Assert.Equal(404, (await r.Backdating.PreviewAsync(TaxDb.SchoolB, Nursery, sports, from, default)).StatusCode);                   // not their structure
            Assert.Equal(404, (await r.Backdating.PreviewAsync(TaxDb.SchoolA, Nursery, 987654, from, default)).StatusCode);                    // not a line of it
            db.Exec($"update tb_academic_years set is_current=false where academic_year_id={TaxDb.YearA}");
            var notCurrent = await r.Backdating.PreviewAsync(TaxDb.SchoolA, Nursery, sports, from, default);
            Assert.Equal(400, notCurrent.StatusCode); Assert.Contains("current academic year", notCurrent.Message);
        }

        [DbFact]
        public async Task ChargeFrom_TodayNeedsNothing_AnEarlierDateNeedsConfirmationAReasonAndTheCurrentYear_AndCreatesLoggedBackdatedDues()
        {
            using var db = new TaxDb(); using var r = Open(db);
            var student = "GHS001ST006";
            var year = TaxDb.YearA;
            var from = YearStartOf(r);
            Assert.Null(await r.Billing.CheckChargeFromAsync(TaxDb.SchoolA, year, r.Today, false, null, default));          // today (the default): nothing to confirm
            Assert.Null(await r.Billing.CheckChargeFromAsync(TaxDb.SchoolA, year, r.Today.AddDays(3), false, null, default));
            var noConfirm = await r.Billing.CheckChargeFromAsync(TaxDb.SchoolA, year, from, false, "x", default);
            Assert.Contains("Review the preview and confirm", noConfirm);
            Assert.Contains("reason is required", (await r.Billing.CheckChargeFromAsync(TaxDb.SchoolA, year, from, true, "  ", default))!);
            Assert.Contains("before the academic year starts", (await r.Billing.CheckChargeFromAsync(TaxDb.SchoolA, year, from.AddDays(-1), true, "x", default))!);
            Assert.Null(await r.Billing.CheckChargeFromAsync(TaxDb.SchoolA, year, from, true, "joined in April, paperwork late", default));
            db.Exec($"update tb_academic_years set is_current=false where academic_year_id={year}");
            Assert.Contains("current academic year", (await r.Billing.CheckChargeFromAsync(TaxDb.SchoolA, year, from, true, "x", default))!);
            db.Exec($"update tb_academic_years set is_current=true where academic_year_id={year}");

            // today: the dues start at the first due on or after today
            db.Exec($"delete from tb_student_fee_dues where student_id='{student}'; delete from tb_student_fee_items where student_id='{student}'");
            var normal = await r.Billing.SyncAsync(TaxDb.SchoolA, student, year, null, default, new FeeSyncOptions { ChargeFrom = r.Today });
            Assert.Equal(0, normal.BackdatedCount);
            Assert.Equal(0, db.Scalar<long>($"select count(*) from tb_student_fee_dues where student_id='{student}' and due_date < '{r.Today:yyyy-MM-dd}'"));

            // an earlier confirmed date: the dues from that date to today are created as BACKDATED and logged
            db.Exec($"delete from tb_student_fee_dues where student_id='{student}'; delete from tb_student_fee_items where student_id='{student}'");
            var back = await r.Billing.SyncAsync(TaxDb.SchoolA, student, year, null, default, new FeeSyncOptions { ChargeFrom = from, Backdate = true, BackdatingReason = "joined in April, paperwork late", UserId = TaxDb.UserA });
            Assert.True(back.BackdatedCount > 0);
            var backdated = db.Scalar<long>($"select count(*) from tb_student_fee_dues where student_id='{student}' and source='BACKDATED'");
            Assert.Equal(back.BackdatedCount, (int)backdated);
            Assert.Equal(back.BackdatedAmount, db.Scalar<decimal>($"select sum(amount_due) from tb_student_fee_dues where student_id='{student}' and source='BACKDATED'"));
            Assert.Equal(1, db.Scalar<long>($"select count(*) from tb_fee_backdating_log where student_id='{student}' and kind='STUDENT' and reason='joined in April, paperwork late' and created_by={TaxDb.UserA} and dues_count={back.BackdatedCount}"));
            Assert.Equal(0, db.Scalar<long>($"select count(*) from tb_student_fee_dues where student_id='{student}' and due_date >= '{r.Today:yyyy-MM-dd}' and source='BACKDATED'"));
        }

        [DbFact]
        public async Task TwoSimultaneousStructureSaves_AreSerialised_NoDuplicatesNoDeadlock()
        {
            using var db = new TaxDb();
            using var r1 = Open(db); using var r2 = new Rig(db);
            var a = NewFeeType(db, "Lab Fee"); var b = NewFeeType(db, "Bus Fee");
            var t1 = Task.Run(() => Save(r1, l => l.Add(Line(a, Monthly, 300, 10, r1.Today))));
            var t2 = Task.Run(() => Save(r2, l => l.Add(Line(b, Monthly, 400, 10, r2.Today))));
            var results = await Task.WhenAll(t1, t2);
            Assert.All(results, x => Assert.Equal(200, x.StatusCode));
            // whatever the final set of lines is, every student has exactly one item per fee head and at most one due per month of an item
            Assert.Equal(0, db.Scalar<long>("select count(*) from (select student_id, academic_year_id, fee_type_id from tb_student_fee_items group by 1,2,3 having count(*)>1) x"));
            Assert.Equal(0, db.Scalar<long>("select count(*) from (select student_fee_item_id, extract(year from due_date)*12+extract(month from due_date) m from tb_student_fee_dues group by 1,2 having count(*)>1) x"));
            // and the items match the lines that are on the structure
            Assert.Equal(0, db.Scalar<long>($@"select count(*) from tb_student_fee_items i join tb_student_fee_structure a on a.student_id=i.student_id and a.academic_year_id=i.academic_year_id
                where a.fee_structure_id={Nursery} and i.is_applicable and i.isactive and not exists (select 1 from tb_fee_structure_details d where d.fee_structure_id={Nursery} and d.fee_type_id=i.fee_type_id)"));
        }

        [DbFact]
        public async Task EveryItemKeepsTheFrequencyOfItsLine_AStudentNeverHasOneOfTheirOwn()
        {
            using var db = new TaxDb(); using var r = Open(db);
            await r.Billing.SyncStructureAsync(TaxDb.SchoolA, Nursery, default);
            Assert.Equal(0, db.Scalar<long>($@"select count(*) from tb_student_fee_items i join tb_student_fee_structure a on a.student_id=i.student_id and a.academic_year_id=i.academic_year_id
                join tb_fee_structure_details d on d.fee_structure_id=a.fee_structure_id and d.fee_type_id=i.fee_type_id where i.frequency_id <> d.frequency_id"));
            // even if one drifted (old data), the next sync puts it back on its line's cycle
            db.Exec($"update tb_student_fee_items set frequency_id={Annual} where student_id='GHS001ST002' and fee_type_id=(select fee_type_id from tb_fee_types where school_id='{TaxDb.SchoolA}' and fee_type_name='Tuition Fee')");
            using var fresh = new Rig(db);   // a new context: the first one still holds the old item in its change tracker
            await fresh.Billing.SyncAsync(TaxDb.SchoolA, "GHS001ST002", TaxDb.YearA, null, default);
            Assert.Equal(Monthly, db.Scalar<long>($"select i.frequency_id from tb_student_fee_items i join tb_fee_types t on t.fee_type_id=i.fee_type_id where i.student_id='GHS001ST002' and t.fee_type_name='Tuition Fee'"));
        }

        [Fact]
        public void TheAuditReportFile_IsReadOnly()
        {
            var dir = AppContext.BaseDirectory;
            string? path = null;
            for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "Docs", "smss-fee-dues-audit-report.sql"))) { path = Path.Combine(d.FullName, "Docs", "smss-fee-dues-audit-report.sql"); break; }
            if (path == null) return;   // run from a build output that is not inside the repository: nothing to check
            var sql = File.ReadAllText(path);
            var code = string.Join("\n", sql.Split('\n').Where(l => !l.TrimStart().StartsWith("--")));
            foreach (var word in new[] { "INSERT ", "UPDATE ", "DELETE ", "DROP ", "ALTER ", "TRUNCATE ", "CREATE ", "GRANT ", "COMMIT" })
                Assert.DoesNotContain(word, code, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("READ ONLY", code, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", code, StringComparison.OrdinalIgnoreCase);
        }
    }
}
