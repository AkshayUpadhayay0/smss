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
    /// End to end on a scratch PostgreSQL (see TaxTestKit): the REAL StudentOnboardingService (Add Existing Student) with today, with an earlier CONFIRMED "Charge fees from" date and with an earlier
    /// date WITHOUT confirmation, checking the student, the dues and the back-dating log row. Also pins down what removing a fee line does to a student's paid and unpaid dues.
    /// School GHS261103002, structure 17 = the Nursery structure (section 53), user 120 is its School Admin.
    /// </summary>
    public class StudentOnboardingFeeTests
    {
        private const long Nursery = 17, NurserySection = 53;
        private const long UserA = TaxDb.UserA;

        private sealed class Rig : IDisposable
        {
            public readonly dbContext C;
            public readonly StudentOnboardingService Onboarding;
            public readonly StudentFeeBillingService Billing;
            public readonly FeeStructureService Structures;
            public readonly DateOnly Today = SchoolClock.Today();
            public readonly DateOnly YearStart;

            public Rig(TaxDb db)
            {
                // the test year covers today and is the current one, so the tests do not depend on the date they run on
                YearStart = new DateOnly(Today.Month >= 4 ? Today.Year : Today.Year - 1, 4, 1);
                db.Exec($"update tb_academic_years set start_date='{YearStart:yyyy-MM-dd}', end_date='{YearStart.AddYears(1).AddDays(-1):yyyy-MM-dd}', is_current=true where academic_year_id={TaxDb.YearA}");
                C = db.Ctx();
                Billing = new StudentFeeBillingService(new StudentFeeBillingRepository(C), new StudentFeeRepository(C), NullLogger<StudentFeeBillingService>.Instance);
                Onboarding = new StudentOnboardingService(new AdmissionRepository(C), new FakeNotifier(), Billing, NullLogger<StudentOnboardingService>.Instance);
                Structures = new FeeStructureService(new FeeStructureRepository(C), Billing, NullLogger<FeeStructureService>.Instance);
            }
            public void Dispose() => C.Dispose();
        }

        private static DirectStudentEntryRequestDto Req(string first, DateOnly admission, DateOnly? chargeFrom = null, bool confirm = false, string? reason = null) => new()
        {
            AdmissionDate = admission.ToDateTime(TimeOnly.MinValue), AcademicYearId = TaxDb.YearA, ClassSectionId = NurserySection,
            FirstName = first, LastName = "Onboarded", DateOfBirth = new DateTime(2020, 1, 15), GuardianName = "Guardian", GuardianRelationship = "Father", GuardianMobileNumber = "9876543210",
            ChargeFrom = chargeFrom, ConfirmBackdating = confirm, BackdatingReason = reason,
        };

        private sealed record Due(DateOnly Date, decimal Amount, string Source, long? Log, long FeeTypeId);

        private static List<Due> DuesOf(TaxDb db, string studentId)
        {
            var list = new List<Due>();
            using var c = new NpgsqlConnection(db.Conn); c.Open();
            using var cmd = new NpgsqlCommand($@"select d.due_date, d.amount_due, d.source, d.backdating_log_id, i.fee_type_id from tb_student_fee_dues d
                join tb_student_fee_items i on i.student_fee_item_id = d.student_fee_item_id where d.student_id='{studentId}' order by d.due_date, i.fee_type_id", c);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(new Due(DateOnly.FromDateTime(r.GetDateTime(0)), r.GetDecimal(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetInt64(3), r.GetInt64(4)));
            return list;
        }

        private static long Students(TaxDb db) => db.Scalar<long>($"select count(*) from tb_students where school_id='{TaxDb.SchoolA}'");
        private static long LogRows(TaxDb db) => db.Scalar<long>($"select count(*) from tb_fee_backdating_log where school_id='{TaxDb.SchoolA}'");

        // ============================================================== Add Existing Student, "Charge fees from" = today (or left out)
        [DbFact]
        public async Task AddExistingStudent_WithToday_CreatesNoDueBeforeToday_AndNoLogRow()
        {
            using var db = new TaxDb(); using var r = new Rig(db);
            var logs = LogRows(db);
            foreach (var chargeFrom in new DateOnly?[] { null, r.Today })
            {
                var res = await r.Onboarding.CreateDirectEntryAsync(TaxDb.SchoolA, Req("Today" + (chargeFrom == null ? "Blank" : "Date"), r.YearStart, chargeFrom), default, UserA);
                Assert.Equal(201, res.StatusCode);
                Assert.Empty(res.Data!.Warnings);
                Assert.NotNull(res.Data.FeeStructureName);

                var dues = DuesOf(db, res.Data.StudentId);
                Assert.NotEmpty(dues);
                Assert.All(dues, d => { Assert.True(d.Date >= r.Today, $"a due dated {d.Date} before today {r.Today}"); Assert.Equal("AUTO", d.Source); Assert.Null(d.Log); });
            }
            Assert.Equal(logs, LogRows(db));   // nothing was back-dated, so nothing was logged
        }

        // ============================================================== an earlier date, confirmed with a reason
        [DbFact]
        public async Task AddExistingStudent_WithAnEarlierConfirmedDate_CreatesTheEarlierDuesAsBackdated_AndLogsThem()
        {
            using var db = new TaxDb(); using var r = new Rig(db);
            var from = r.YearStart;
            var res = await r.Onboarding.CreateDirectEntryAsync(TaxDb.SchoolA, Req("Earlier", from, from, true, "  Joined in April, entered late  "), default, UserA);
            Assert.Equal(201, res.StatusCode);
            var studentId = res.Data!.StudentId;

            var dues = DuesOf(db, studentId);
            Assert.NotEmpty(dues);
            Assert.All(dues, d => Assert.True(d.Date >= from, $"a due dated {d.Date} before the charge-from date {from}"));
            var past = dues.Where(d => d.Date < r.Today).ToList();
            Assert.NotEmpty(past);                                                      // the earlier months are really there
            Assert.All(past, d => { Assert.Equal("BACKDATED", d.Source); Assert.NotNull(d.Log); });
            Assert.All(dues.Where(d => d.Date >= r.Today), d => { Assert.Equal("AUTO", d.Source); Assert.Null(d.Log); });

            // exactly ONE log row, for this student, matching what was created
            Assert.Equal(1, LogRows(db));
            using var c = new NpgsqlConnection(db.Conn); c.Open();
            using var cmd = new NpgsqlCommand("select backdating_log_id, kind, student_id, academic_year_id, from_date, to_date, students_count, dues_count, total_amount, reason, created_by, fee_structure_id from tb_fee_backdating_log", c);
            using var rd = cmd.ExecuteReader();
            Assert.True(rd.Read());
            Assert.Equal(past.First().Log, rd.GetInt64(0));
            Assert.Equal("STUDENT", rd.GetString(1));
            Assert.Equal(studentId, rd.GetString(2));
            Assert.Equal(TaxDb.YearA, rd.GetInt64(3));
            Assert.Equal(from, DateOnly.FromDateTime(rd.GetDateTime(4)));
            Assert.Equal(r.Today, DateOnly.FromDateTime(rd.GetDateTime(5)));
            Assert.Equal(1, rd.GetInt32(6));
            Assert.Equal(past.Count, rd.GetInt32(7));
            Assert.Equal(past.Sum(d => d.Amount), rd.GetDecimal(8));
            Assert.Equal("Joined in April, entered late", rd.GetString(9));            // trimmed
            Assert.Equal(UserA, rd.GetInt64(10));                                      // the signed-in user, from the token
            Assert.True(rd.IsDBNull(11));                                              // a per-student log row names no structure
        }

        // ============================================================== an earlier date without confirmation (or without a reason)
        [DbFact]
        public async Task AddExistingStudent_WithAnEarlierDateButNoConfirmation_IsRefused_AndNothingIsCreated()
        {
            using var db = new TaxDb(); using var r = new Rig(db);
            var students = Students(db); var logs = LogRows(db);
            var dues = db.Scalar<long>("select count(*) from tb_student_fee_dues");

            var unconfirmed = await r.Onboarding.CreateDirectEntryAsync(TaxDb.SchoolA, Req("NoConfirm", r.YearStart, r.YearStart, false, "a reason"), default, UserA);
            Assert.Equal(400, unconfirmed.StatusCode);
            Assert.Contains("confirm", unconfirmed.Message, StringComparison.OrdinalIgnoreCase);

            var noReason = await r.Onboarding.CreateDirectEntryAsync(TaxDb.SchoolA, Req("NoReason", r.YearStart, r.YearStart, true, "   "), default, UserA);
            Assert.Equal(400, noReason.StatusCode);

            Assert.Equal(students, Students(db));                                       // refused BEFORE the student is saved
            Assert.Equal(logs, LogRows(db));
            Assert.Equal(dues, db.Scalar<long>("select count(*) from tb_student_fee_dues"));
        }

        // ============================================================== a plan that starts mid-cycle
        [DbFact]
        public async Task AddExistingStudent_OnAMonthlyPlanMidCycle_IsBilledTheFullCycleAmount_AndAResyncChangesNothing()
        {
            using var db = new TaxDb(); using var r = new Rig(db);
            const long exam = 15;   // Exam Fee: Half-Yearly 800, due day 20
            var req = Req("MidCycle", r.YearStart);
            req.FeeItems = new() { new FeeItemSelection { FeeTypeId = exam, InstallmentPlan = FeeSchedule.PlanMonthly, IsApplicable = true } };
            var res = await r.Onboarding.CreateDirectEntryAsync(TaxDb.SchoolA, req, default, UserA);
            Assert.Equal(201, res.StatusCode);
            var id = res.Data!.StudentId;

            var dues = DuesOf(db, id).Where(d => d.FeeTypeId == exam).ToList();
            Assert.NotEmpty(dues);
            var cycleEnd = r.YearStart.AddMonths(((r.Today.Year * 12 + r.Today.Month - (r.YearStart.Year * 12 + r.YearStart.Month)) / 6 + 1) * 6);   // first month AFTER today's half-year
            var cycleDues = dues.Where(d => d.Date < cycleEnd).ToList();
            Assert.All(dues, d => Assert.True(d.Date >= r.Today));
            // the cycle's FULL amount is billed over the months left, never a part of it
            if (cycleDues.Count > 0)
            {
                Assert.Equal(800m, cycleDues.Sum(d => d.Amount));
                Assert.True(cycleDues.Max(d => d.Amount) - cycleDues.Min(d => d.Amount) <= 0.05m);
            }

            // the same fee with NO plan owes the same 800 for the cycle: choosing a plan is not a discount
            var noPlan = await r.Onboarding.CreateDirectEntryAsync(TaxDb.SchoolA, Req("MidCycleNone", r.YearStart), default, UserA);
            Assert.Equal(cycleDues.Sum(d => d.Amount), DuesOf(db, noPlan.Data!.StudentId).Where(d => d.FeeTypeId == exam && d.Date < cycleEnd).Sum(d => d.Amount));

            // running the sync again (any structure edit, any read that heals) leaves the dues exactly as they are
            var before = string.Join(";", DuesOf(db, id).Select(d => $"{d.FeeTypeId}|{d.Date}|{d.Amount}"));
            using var fresh = new Rig(db);
            await fresh.Billing.SyncAsync(TaxDb.SchoolA, id, TaxDb.YearA, null, default);
            Assert.Equal(before, string.Join(";", DuesOf(db, id).Select(d => $"{d.FeeTypeId}|{d.Date}|{d.Amount}")));
        }

        // ============================================================== removing a structure line
        // The behaviour this pins down is unchanged by the effective-date work (the same code ran before): see the report.
        [DbFact]
        public async Task RemovingAStructureLine_KeepsDuesWithPayments_DeactivatesTheItem_AndDeletesTheUnpaidOnes()
        {
            using var db = new TaxDb(); using var r = new Rig(db);
            const long tuition = 12;   // Tuition Fee, Monthly, in the Nursery structure
            var paidStudent = "GHS001ST002"; var cleanStudent = "GHS001ST003";
            var item = db.Scalar<long>($"select student_fee_item_id from tb_student_fee_items where student_id='{paidStudent}' and fee_type_id={tuition}");

            // ST002 pays one PAST installment in full and part of one FUTURE installment; the rest of its tuition is unpaid (past and future)
            var all = Rows(db, $"select fee_due_id, due_date from tb_student_fee_dues where student_fee_item_id={item} order by due_date");
            var pastIds = all.Where(x => AsDate(x[1]) < r.Today).Select(x => (long)x[0]!).ToList();
            var futureIds = all.Where(x => AsDate(x[1]) >= r.Today).Select(x => (long)x[0]!).ToList();
            Assert.True(pastIds.Count >= 2 && futureIds.Count >= 2, "the test data needs past and future tuition dues");
            db.Exec($"update tb_student_fee_dues set amount_paid = amount_due where fee_due_id={pastIds[0]}");
            db.Exec($"update tb_student_fee_dues set amount_paid = 50 where fee_due_id={futureIds[0]}");
            var keptIds = new[] { pastIds[0], futureIds[0] };
            var otherBefore = Others(db, paidStudent, tuition);
            var cleanOtherBefore = Others(db, cleanStudent, tuition);

            var current = (FeeStructureResponse)(await r.Structures.GetByIdAsync(TaxDb.SchoolA, Nursery)).Data!;
            var lines = current.Details.Where(d => d.FeeTypeId != tuition).Select(d => new FeeStructureLineRequest { FeeTypeId = d.FeeTypeId, FrequencyId = d.FrequencyId, Amount = d.Amount, DueDay = d.DueDay ?? 10, EffectiveFrom = d.EffectiveFrom }).ToList();
            var res = await r.Structures.UpdateAsync(TaxDb.SchoolA, Nursery, new UpdateFeeStructureRequest { StructureName = current.StructureName, Details = lines });
            Assert.Equal(200, res.StatusCode);

            // the student WITH payments: item kept but switched off; the two dues with money stay exactly as they were; every unpaid due (past and future) is gone
            Assert.False(db.Scalar<bool>($"select isactive from tb_student_fee_items where student_fee_item_id={item}"));
            var left = Rows(db, $"select fee_due_id, amount_due, amount_paid from tb_student_fee_dues where student_fee_item_id={item} order by fee_due_id");
            Assert.Equal(keptIds.OrderBy(x => x), left.Select(x => (long)x[0]!).OrderBy(x => x));
            Assert.All(left, x => Assert.True((decimal)x[2]! > 0));
            // unpaid past dues of the removed line: deleted (this is the behaviour the report asks about)
            Assert.DoesNotContain(left, x => pastIds.Skip(1).Contains((long)x[0]!));

            // the student WITHOUT payments: the item and all its dues are gone
            Assert.Equal(0, db.Scalar<long>($"select count(*) from tb_student_fee_items where student_id='{cleanStudent}' and fee_type_id={tuition}"));
            Assert.Equal(0, db.Scalar<long>($"select count(*) from tb_student_fee_dues d join tb_student_fee_items i on i.student_fee_item_id=d.student_fee_item_id where i.student_id='{cleanStudent}' and i.fee_type_id={tuition}"));

            // the other fee heads of both students are untouched
            Assert.Equal(otherBefore, Others(db, paidStudent, tuition));
            Assert.Equal(cleanOtherBefore, Others(db, cleanStudent, tuition));
        }

        private static DateOnly AsDate(object? v) => v is DateOnly d ? d : DateOnly.FromDateTime((DateTime)v!);

        private static List<object?[]> Rows(TaxDb db, string sql)
        {
            using var c = new NpgsqlConnection(db.Conn); c.Open();
            using var cmd = new NpgsqlCommand(sql, c);
            using var r = cmd.ExecuteReader();
            var list = new List<object?[]>();
            while (r.Read()) { var row = new object?[r.FieldCount]; for (var i = 0; i < row.Length; i++) row[i] = r.IsDBNull(i) ? null : r.GetValue(i); list.Add(row); }
            return list;
        }

        private static string Others(TaxDb db, string student, long exceptFeeType)
        {
            using var c = new NpgsqlConnection(db.Conn); c.Open();
            using var cmd = new NpgsqlCommand($@"select string_agg(d.fee_due_id||'|'||d.due_date||'|'||d.amount_due||'|'||d.amount_paid, ';' order by d.fee_due_id) from tb_student_fee_dues d
                join tb_student_fee_items i on i.student_fee_item_id=d.student_fee_item_id where i.student_id='{student}' and i.fee_type_id<>{exceptFeeType}", c);
            return (string?)cmd.ExecuteScalar() ?? "";
        }
    }
}
