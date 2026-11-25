using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using smss_api_db_layer.context;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class LateFeeRepository : ILateFeeRepository
    {
        private readonly dbContext _db;
        public LateFeeRepository(dbContext db) => _db = db;

        private static NpgsqlParameter P(string n, NpgsqlDbType t, object? v) => new(n, t) { Value = v ?? DBNull.Value };

        public async Task<LateFeeRuleRow?> GetRuleAsync(string schoolId, CancellationToken ct) =>
            (await _db.Database.SqlQueryRaw<LateFeeRuleRow>(@"
SELECT r.is_enabled AS ""IsEnabled"", r.rule_type AS ""RuleType"", r.amount AS ""Amount"", r.percent AS ""Percent"", r.grace_days AS ""GraceDays"", r.cap_per_due AS ""CapPerDue"", r.tax_applies AS ""TaxApplies"",
       r.updated_at AS ""UpdatedAt"", u.org_user_id AS ""UpdatedByName""
  FROM public.tb_school_late_fee_rules r LEFT JOIN public.tb_users u ON u.user_id = r.updated_by
 WHERE r.school_id = @school", P("school", NpgsqlDbType.Varchar, schoolId)).ToListAsync(ct)).FirstOrDefault();

        public Task SaveRuleAsync(string schoolId, long userId, bool enabled, string? ruleType, decimal? amount, decimal? percent, int graceDays, decimal? capPerDue, bool taxApplies, CancellationToken ct) =>
            _db.Database.ExecuteSqlRawAsync(@"
INSERT INTO public.tb_school_late_fee_rules (school_id, is_enabled, rule_type, amount, percent, grace_days, cap_per_due, tax_applies, updated_by)
VALUES (@school, @enabled, @type, @amount, @percent, @grace, @cap, @tax, @by)
ON CONFLICT (school_id) DO UPDATE SET is_enabled = EXCLUDED.is_enabled, rule_type = EXCLUDED.rule_type, amount = EXCLUDED.amount, percent = EXCLUDED.percent, grace_days = EXCLUDED.grace_days,
    cap_per_due = EXCLUDED.cap_per_due, tax_applies = EXCLUDED.tax_applies, updated_by = EXCLUDED.updated_by, updated_at = CURRENT_TIMESTAMP",
                new object[]
                {
                    P("school", NpgsqlDbType.Varchar, schoolId), P("enabled", NpgsqlDbType.Boolean, enabled), P("type", NpgsqlDbType.Varchar, ruleType), P("amount", NpgsqlDbType.Numeric, amount),
                    P("percent", NpgsqlDbType.Numeric, percent), P("grace", NpgsqlDbType.Integer, graceDays), P("cap", NpgsqlDbType.Numeric, capPerDue), P("tax", NpgsqlDbType.Boolean, taxApplies), P("by", NpgsqlDbType.Bigint, userId),
                }, ct);

        public Task<List<LateFeeTypeRow>> ListFeeTypesAsync(string schoolId, CancellationToken ct) =>
            _db.Database.SqlQueryRaw<LateFeeTypeRow>(@"SELECT fee_type_id AS ""FeeTypeId"", fee_type_name AS ""Name"", isactive AS ""IsActive"", applies_late_fee AS ""AppliesLateFee"" FROM public.tb_fee_types WHERE school_id = @school ORDER BY fee_type_name",
                P("school", NpgsqlDbType.Varchar, schoolId)).ToListAsync(ct);

        public async Task<int> SetFlagsAsync(string schoolId, IReadOnlyCollection<long> flaggedIds, CancellationToken ct)
        {
            var ids = flaggedIds.ToArray();
            await _db.Database.ExecuteSqlRawAsync("UPDATE public.tb_fee_types SET applies_late_fee = (fee_type_id = ANY(@ids)) WHERE school_id = @school AND applies_late_fee IS DISTINCT FROM (fee_type_id = ANY(@ids))",
                new object[] { P("school", NpgsqlDbType.Varchar, schoolId), new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = ids } }, ct);
            return (await _db.Database.SqlQueryRaw<int>(@"SELECT count(*)::int AS ""Value"" FROM public.tb_fee_types WHERE school_id = @school AND fee_type_id = ANY(@ids)",
                P("school", NpgsqlDbType.Varchar, schoolId), new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = ids }).ToListAsync(ct)).Single();
        }

        public async Task<bool> StudentExistsAsync(string schoolId, string studentId, CancellationToken ct) =>
            (await _db.Database.SqlQueryRaw<int>(@"SELECT count(*)::int AS ""Value"" FROM public.tb_students WHERE school_id = @school AND student_id = @student",
                P("school", NpgsqlDbType.Varchar, schoolId), P("student", NpgsqlDbType.Varchar, studentId)).ToListAsync(ct)).Single() > 0;

        private const string DueSelect = @"
SELECT d.fee_due_id AS ""FeeDueId"", d.due_date AS ""DueDate"", d.amount_due AS ""AmountDue"", d.amount_paid AS ""AmountPaid"", ft.fee_type_name AS ""FeeTypeName"", ft.applies_late_fee AS ""AppliesLateFee""
  FROM public.tb_student_fee_dues d
  JOIN public.tb_student_fee_items i ON i.student_fee_item_id = d.student_fee_item_id AND i.school_id = @school
  JOIN public.tb_fee_types ft ON ft.fee_type_id = i.fee_type_id AND ft.school_id = @school
 WHERE d.school_id = @school AND d.student_id = @student";

        public Task<List<LateFeeDueRow>> ListOutstandingDuesAsync(string schoolId, string studentId, CancellationToken ct) =>
            _db.Database.SqlQueryRaw<LateFeeDueRow>(DueSelect + " AND d.amount_due > d.amount_paid ORDER BY d.due_date, d.fee_due_id", P("school", NpgsqlDbType.Varchar, schoolId), P("student", NpgsqlDbType.Varchar, studentId)).ToListAsync(ct);

        public Task<List<LateFeeDueRow>> GetDuesAsync(string schoolId, string studentId, IReadOnlyCollection<long> dueIds, CancellationToken ct) =>
            _db.Database.SqlQueryRaw<LateFeeDueRow>(DueSelect + " AND d.fee_due_id = ANY(@ids) ORDER BY d.due_date, d.fee_due_id", P("school", NpgsqlDbType.Varchar, schoolId), P("student", NpgsqlDbType.Varchar, studentId),
                new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = dueIds.ToArray() }).ToListAsync(ct);

        public Task<List<LateFeeCollectedRow>> GetCollectedAsync(string schoolId, IReadOnlyCollection<long> dueIds, CancellationToken ct) =>
            dueIds.Count == 0 ? Task.FromResult(new List<LateFeeCollectedRow>()) :
            _db.Database.SqlQueryRaw<LateFeeCollectedRow>(@"
SELECT l.fee_due_id AS ""FeeDueId"", sum(l.amount) AS ""Amount""
  FROM public.tb_fee_payment_late_fees l JOIN public.tb_fee_payments p ON p.payment_id = l.payment_id AND p.school_id = l.school_id
 WHERE l.school_id = @school AND l.fee_due_id = ANY(@ids) AND p.status = 'Active' GROUP BY l.fee_due_id",
                P("school", NpgsqlDbType.Varchar, schoolId), new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = dueIds.ToArray() }).ToListAsync(ct);

        public async Task InsertPaymentLateFeesAsync(string schoolId, long paymentId, string ruleType, decimal? ruleAmount, decimal? rulePercent, int graceDays, decimal? capPerDue, IReadOnlyList<LateFeeRowWrite> rows, CancellationToken ct)
        {
            foreach (var r in rows)
                await _db.Database.ExecuteSqlRawAsync(@"
INSERT INTO public.tb_fee_payment_late_fees (school_id, payment_id, fee_due_id, amount, days_late, days_charged, base_amount, computed_total, already_collected, rule_type, rule_amount, rule_percent, grace_days, cap_per_due)
VALUES (@school, @payment, @due, @amount, @late, @charged, @base, @total, @already, @type, @ramount, @rpercent, @grace, @cap)",
                    new object[]
                    {
                        P("school", NpgsqlDbType.Varchar, schoolId), P("payment", NpgsqlDbType.Bigint, paymentId), P("due", NpgsqlDbType.Bigint, r.FeeDueId), P("amount", NpgsqlDbType.Numeric, r.Amount),
                        P("late", NpgsqlDbType.Integer, r.DaysLate), P("charged", NpgsqlDbType.Integer, r.DaysCharged), P("base", NpgsqlDbType.Numeric, r.BaseAmount), P("total", NpgsqlDbType.Numeric, r.ComputedTotal),
                        P("already", NpgsqlDbType.Numeric, r.AlreadyCollected), P("type", NpgsqlDbType.Varchar, ruleType), P("ramount", NpgsqlDbType.Numeric, ruleAmount), P("rpercent", NpgsqlDbType.Numeric, rulePercent),
                        P("grace", NpgsqlDbType.Integer, graceDays), P("cap", NpgsqlDbType.Numeric, capPerDue),
                    }, ct);
        }

        public Task<List<PaymentLateFeeLineRow>> GetPaymentLateFeeLinesAsync(string schoolId, IReadOnlyCollection<long> paymentIds, CancellationToken ct) =>
            paymentIds.Count == 0 ? Task.FromResult(new List<PaymentLateFeeLineRow>()) :
            _db.Database.SqlQueryRaw<PaymentLateFeeLineRow>(@"
SELECT l.payment_id AS ""PaymentId"", l.fee_due_id AS ""FeeDueId"", COALESCE(NULLIF(btrim(a.fee_type_name_snapshot), ''), ft.fee_type_name) AS ""FeeTypeName"", d.due_date AS ""DueDate"",
       l.days_late AS ""DaysLate"", l.days_charged AS ""DaysCharged"", l.base_amount AS ""BaseAmount"", l.amount AS ""Amount""
  FROM public.tb_fee_payment_late_fees l
  JOIN public.tb_student_fee_dues d ON d.fee_due_id = l.fee_due_id AND d.school_id = l.school_id
  JOIN public.tb_student_fee_items i ON i.student_fee_item_id = d.student_fee_item_id
  JOIN public.tb_fee_types ft ON ft.fee_type_id = i.fee_type_id
  LEFT JOIN public.tb_fee_payment_allocations a ON a.payment_id = l.payment_id AND a.fee_due_id = l.fee_due_id
 WHERE l.school_id = @school AND l.payment_id = ANY(@ids) ORDER BY l.payment_id, d.due_date, l.fee_due_id",
                P("school", NpgsqlDbType.Varchar, schoolId), new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = paymentIds.ToArray() }).ToListAsync(ct);

        public async Task<LateFeeTaxRow?> GetTaxTypeAsync(string schoolId, long taxTypeId, CancellationToken ct) =>
            (await _db.Database.SqlQueryRaw<LateFeeTaxRow>(@"SELECT tax_name AS ""Name"", rate_percent AS ""RatePercent"", is_active AS ""IsActive"" FROM public.tb_tax_types WHERE school_id = @school AND tax_type_id = @id",
                P("school", NpgsqlDbType.Varchar, schoolId), P("id", NpgsqlDbType.Bigint, taxTypeId)).ToListAsync(ct)).FirstOrDefault();
    }
}
