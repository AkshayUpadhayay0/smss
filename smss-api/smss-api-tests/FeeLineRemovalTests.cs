using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using smss_api_db_layer.context;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Removing a fee line works like moving an effective date later: a dry run first (counts and amounts of the unpaid dues that would go, past and future, and the items that would be switched off),
    /// a refusal (409) until the request carries ConfirmLineRemoval, one audit row per confirmed removal, and dues with any payment untouched. Scratch PostgreSQL (see TaxTestKit);
    /// school GHS261103002, structure 17 = the Nursery structure, Tuition Fee (fee type 12) is Monthly.
    /// </summary>
    public class FeeLineRemovalTests
    {
        private const long Nursery = 17, Tuition = 12, Library = 16;
        private const long UserA = TaxDb.UserA;

        private sealed class Rig : IDisposable
        {
            public readonly dbContext C;
            public readonly FeeStructureService Structures;
            public readonly DateOnly Today = SchoolClock.Today();
            public Rig(TaxDb db)
            {
                var start = new DateOnly(Today.Month >= 4 ? Today.Year : Today.Year - 1, 4, 1);
                db.Exec($"update tb_academic_years set start_date='{start:yyyy-MM-dd}', end_date='{start.AddYears(1).AddDays(-1):yyyy-MM-dd}', is_current=true where academic_year_id={TaxDb.YearA}");
                C = db.Ctx();
                var billing = new StudentFeeBillingService(new StudentFeeBillingRepository(C), new StudentFeeRepository(C), NullLogger<StudentFeeBillingService>.Instance);
                Structures = new FeeStructureService(new FeeStructureRepository(C), billing, NullLogger<FeeStructureService>.Instance);
            }
            public void Dispose() => C.Dispose();
        }

        /// <summary>The structure's lines as a save request, without the given fee types.</summary>
        private static async Task<UpdateFeeStructureRequest> Without(Rig r, params long[] feeTypes)
        {
            var current = (FeeStructureResponse)(await r.Structures.GetByIdAsync(TaxDb.SchoolA, Nursery)).Data!;
            return new UpdateFeeStructureRequest
            {
                StructureName = current.StructureName,
                Details = current.Details.Where(d => !feeTypes.Contains(d.FeeTypeId))
                    .Select(d => new FeeStructureLineRequest { FeeTypeId = d.FeeTypeId, FrequencyId = d.FrequencyId, Amount = d.Amount, DueDay = d.DueDay ?? 10, EffectiveFrom = d.EffectiveFrom }).ToList()
            };
        }

        private static string Snapshot(TaxDb db)
        {
            using var c = new NpgsqlConnection(db.Conn); c.Open();
            using var cmd = new NpgsqlCommand(@"select coalesce((select string_agg(fee_due_id||'|'||student_fee_item_id||'|'||due_date||'|'||amount_due||'|'||amount_paid, ';' order by fee_due_id) from tb_student_fee_dues), '')
                || '#' || coalesce((select string_agg(student_fee_item_id||'|'||isactive, ';' order by student_fee_item_id) from tb_student_fee_items), '')
                || '#' || (select count(*) from tb_fee_structure_details)", c);
            return (string)cmd.ExecuteScalar()!;
        }

        private static long Logs(TaxDb db) => db.Scalar<long>("select count(*) from tb_fee_line_removal_log");

        /// <summary>Two students of the structure pay part of their Tuition (one past installment in full; part of a future one), so the line has both paid and unpaid dues.</summary>
        private static void SomePayments(TaxDb db, DateOnly today)
        {
            var paid = db.Scalar<long>($"select d.fee_due_id from tb_student_fee_dues d join tb_student_fee_items i on i.student_fee_item_id=d.student_fee_item_id where i.student_id='GHS001ST002' and i.fee_type_id={Tuition} and d.due_date < '{today:yyyy-MM-dd}' order by d.due_date limit 1");
            db.Exec($"update tb_student_fee_dues set amount_paid = amount_due where fee_due_id={paid}");
            var part = db.Scalar<long>($"select d.fee_due_id from tb_student_fee_dues d join tb_student_fee_items i on i.student_fee_item_id=d.student_fee_item_id where i.student_id='GHS001ST003' and i.fee_type_id={Tuition} and d.due_date >= '{today:yyyy-MM-dd}' order by d.due_date limit 1");
            db.Exec($"update tb_student_fee_dues set amount_paid = 50 where fee_due_id={part}");
        }

        // what the dry run should say, computed straight from the tables (not from the app)
        private sealed record Expected(long Items, long Deactivated, long Removed, long PastN, decimal PastAmt, long FutureN, decimal FutureAmt, long PaidKept);
        private static Expected FromSql(TaxDb db, long feeType, DateOnly today)
        {
            long N(string sql) => db.Scalar<long>(sql);
            decimal M(string sql) => db.Scalar<decimal>(sql);
            var from = $@"from tb_student_fee_dues d join tb_student_fee_items i on i.student_fee_item_id=d.student_fee_item_id
                          join tb_student_fee_structure a on a.student_id=i.student_id and a.academic_year_id=i.academic_year_id and a.fee_structure_id={Nursery} where i.fee_type_id={feeType}";
            var items = $@"from tb_student_fee_items i join tb_student_fee_structure a on a.student_id=i.student_id and a.academic_year_id=i.academic_year_id and a.fee_structure_id={Nursery} where i.fee_type_id={feeType}";
            var paidItem = "exists (select 1 from tb_student_fee_dues p where p.student_fee_item_id=i.student_fee_item_id and p.amount_paid>0)";
            return new Expected(
                N($"select count(*) {items}"), N($"select count(*) {items} and {paidItem}"), N($"select count(*) {items} and not {paidItem}"),
                N($"select count(*) {from} and d.amount_paid=0 and d.due_date < '{today:yyyy-MM-dd}'"), M($"select coalesce(sum(d.amount_due),0) {from} and d.amount_paid=0 and d.due_date < '{today:yyyy-MM-dd}'"),
                N($"select count(*) {from} and d.amount_paid=0 and d.due_date >= '{today:yyyy-MM-dd}'"), M($"select coalesce(sum(d.amount_due),0) {from} and d.amount_paid=0 and d.due_date >= '{today:yyyy-MM-dd}'"),
                N($"select count(*) {from} and d.amount_paid>0"));
        }

        private static JsonElement Json(object? data) => JsonSerializer.SerializeToElement(data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        // ============================================================== the dry run and the refusal
        [DbFact]
        public async Task RemovingALine_WithoutConfirmation_IsRefusedWithTheNumbers_AndNothingIsDeleted()
        {
            using var db = new TaxDb(); using var r = new Rig(db);
            SomePayments(db, r.Today);
            var expected = FromSql(db, Tuition, r.Today);
            Assert.True(expected.PastN > 0 && expected.FutureN > 0 && expected.PaidKept == 2 && expected.Deactivated == 2, "the test data needs past and future unpaid dues and two paid ones");
            var before = Snapshot(db); var logs = Logs(db);

            var res = await r.Structures.UpdateAsync(TaxDb.SchoolA, Nursery, await Without(r, Tuition), UserA);

            Assert.Equal(409, res.StatusCode);
            Assert.False(res.Status);
            var data = Json(res.Data);
            Assert.True(data.GetProperty("confirmationRequired").GetBoolean());
            Assert.Equal("LINE_REMOVAL", data.GetProperty("kind").GetString());
            // the dry run's numbers equal an independent SQL count
            Assert.Equal(expected.PastN, data.GetProperty("pastCount").GetInt64());
            Assert.Equal(expected.PastAmt, data.GetProperty("pastAmount").GetDecimal());
            Assert.Equal(expected.FutureN, data.GetProperty("futureCount").GetInt64());
            Assert.Equal(expected.FutureAmt, data.GetProperty("futureAmount").GetDecimal());
            Assert.Equal(expected.Deactivated, data.GetProperty("itemsDeactivated").GetInt64());
            Assert.Equal(expected.Removed, data.GetProperty("itemsRemoved").GetInt64());
            var line = Assert.Single(data.GetProperty("lines").EnumerateArray());
            Assert.Equal("Tuition Fee", line.GetProperty("feeTypeName").GetString());
            Assert.Equal(expected.PaidKept, line.GetProperty("paidDuesKept").GetInt64());
            Assert.Contains("deletes", res.Message);

            Assert.Equal(before, Snapshot(db));          // not a single due, item or line changed
            Assert.Equal(logs, Logs(db));                 // and nothing was logged
        }

        // ============================================================== the confirmed removal
        [DbFact]
        public async Task ConfirmedRemoval_DeletesTheUnpaidDues_AndWritesOneLogRowWithTheSameNumbers()
        {
            using var db = new TaxDb(); using var r = new Rig(db);
            SomePayments(db, r.Today);
            var expected = FromSql(db, Tuition, r.Today);

            var req = await Without(r, Tuition); req.ConfirmLineRemoval = true;
            var res = await r.Structures.UpdateAsync(TaxDb.SchoolA, Nursery, req, UserA);
            Assert.Equal(200, res.StatusCode);

            // the unpaid dues of that fee head are gone, for every student of the structure
            Assert.Equal(0, db.Scalar<long>($@"select count(*) from tb_student_fee_dues d join tb_student_fee_items i on i.student_fee_item_id=d.student_fee_item_id
                join tb_student_fee_structure a on a.student_id=i.student_id and a.academic_year_id=i.academic_year_id and a.fee_structure_id={Nursery} where i.fee_type_id={Tuition} and d.amount_paid=0"));
            Assert.Equal(0, db.Scalar<long>($"select count(*) from tb_fee_structure_details where fee_structure_id={Nursery} and fee_type_id={Tuition}"));

            // exactly ONE log row, with the numbers the dry run showed, the user from the token and the fee head's name
            Assert.Equal(1, Logs(db));
            using var c = new NpgsqlConnection(db.Conn); c.Open();
            using var cmd = new NpgsqlCommand("select school_id, fee_structure_id, fee_type_id, fee_type_name, items_deactivated, items_removed, past_dues_deleted, past_amount, future_dues_deleted, future_amount, paid_dues_kept, created_by, created_at from tb_fee_line_removal_log", c);
            using var rd = cmd.ExecuteReader();
            Assert.True(rd.Read());
            Assert.Equal(TaxDb.SchoolA, rd.GetString(0));
            Assert.Equal(Nursery, rd.GetInt64(1)); Assert.Equal(Tuition, rd.GetInt64(2)); Assert.Equal("Tuition Fee", rd.GetString(3));
            Assert.Equal(expected.Deactivated, rd.GetInt32(4)); Assert.Equal(expected.Removed, rd.GetInt32(5));
            Assert.Equal(expected.PastN, rd.GetInt32(6)); Assert.Equal(expected.PastAmt, rd.GetDecimal(7));
            Assert.Equal(expected.FutureN, rd.GetInt32(8)); Assert.Equal(expected.FutureAmt, rd.GetDecimal(9));
            Assert.Equal(expected.PaidKept, rd.GetInt32(10));
            Assert.Equal(UserA, rd.GetInt64(11));
            Assert.True(DateTime.UtcNow - rd.GetDateTime(12).ToUniversalTime() < TimeSpan.FromMinutes(5));
        }

        // ============================================================== paid dues survive
        [DbFact]
        public async Task ConfirmedRemoval_NeverTouchesADueWithAPayment_AndSwitchesOffOnlyItemsThatHaveOne()
        {
            using var db = new TaxDb(); using var r = new Rig(db);
            SomePayments(db, r.Today);
            var paidRows = db.Scalar<string>($@"select string_agg(d.fee_due_id||'|'||d.due_date||'|'||d.amount_due||'|'||d.amount_paid, ';' order by d.fee_due_id) from tb_student_fee_dues d
                join tb_student_fee_items i on i.student_fee_item_id=d.student_fee_item_id where i.fee_type_id={Tuition} and d.amount_paid > 0");
            var otherFees = db.Scalar<string>($@"select string_agg(d.fee_due_id||'|'||d.amount_due||'|'||d.amount_paid, ';' order by d.fee_due_id) from tb_student_fee_dues d
                join tb_student_fee_items i on i.student_fee_item_id=d.student_fee_item_id where i.fee_type_id<>{Tuition}");

            var req = await Without(r, Tuition); req.ConfirmLineRemoval = true;
            Assert.Equal(200, (await r.Structures.UpdateAsync(TaxDb.SchoolA, Nursery, req, UserA)).StatusCode);

            // the two dues with money (a fully paid one and a part-paid one) are exactly as they were
            Assert.Equal(paidRows, db.Scalar<string>($"select string_agg(fee_due_id||'|'||due_date||'|'||amount_due||'|'||amount_paid, ';' order by fee_due_id) from tb_student_fee_dues where fee_due_id in ({string.Join(",", paidRows.Split(';').Select(x => x.Split('|')[0]))})"));
            // only the two students who paid keep their Tuition item (switched off); everyone else's item is gone with its dues
            Assert.Equal(2, db.Scalar<long>($"select count(*) from tb_student_fee_items where fee_type_id={Tuition} and student_id in (select student_id from tb_student_fee_structure where fee_structure_id={Nursery}) and not isactive"));
            Assert.Equal(2, db.Scalar<long>($"select count(*) from tb_student_fee_items where fee_type_id={Tuition} and student_id in (select student_id from tb_student_fee_structure where fee_structure_id={Nursery})"));
            // other fee heads are untouched
            Assert.Equal(otherFees, db.Scalar<string>($@"select string_agg(d.fee_due_id||'|'||d.amount_due||'|'||d.amount_paid, ';' order by d.fee_due_id) from tb_student_fee_dues d
                join tb_student_fee_items i on i.student_fee_item_id=d.student_fee_item_id where i.fee_type_id<>{Tuition}"));
        }

        // ============================================================== several lines at once, and a line nobody has dues for
        [DbFact]
        public async Task RemovingTwoLines_NeedsOneConfirmation_AndWritesOneRowPerLine()
        {
            using var db = new TaxDb(); using var r = new Rig(db);
            var refused = await r.Structures.UpdateAsync(TaxDb.SchoolA, Nursery, await Without(r, Tuition, Library), UserA);
            Assert.Equal(409, refused.StatusCode);
            Assert.Equal(2, Json(refused.Data).GetProperty("lines").GetArrayLength());
            Assert.Equal(0, Logs(db));

            var req = await Without(r, Tuition, Library); req.ConfirmLineRemoval = true;
            Assert.Equal(200, (await r.Structures.UpdateAsync(TaxDb.SchoolA, Nursery, req, UserA)).StatusCode);
            Assert.Equal(2, Logs(db));
            Assert.Equal(2, db.Scalar<long>($"select count(distinct fee_type_id) from tb_fee_line_removal_log where fee_structure_id={Nursery} and created_by={UserA}"));
        }

        [DbFact]
        public async Task ASaveThatRemovesNoLine_NeverAsksAndNeverLogs()
        {
            using var db = new TaxDb(); using var r = new Rig(db);
            var res = await r.Structures.UpdateAsync(TaxDb.SchoolA, Nursery, await Without(r), UserA);
            Assert.Equal(200, res.StatusCode);
            Assert.Equal(0, Logs(db));
        }
    }
}
