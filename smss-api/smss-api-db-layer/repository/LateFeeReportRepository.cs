using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using smss_api_db_layer.context;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class LateFeeReportRepository : ILateFeeReportRepository
    {
        private readonly dbContext _db;
        public LateFeeReportRepository(dbContext db) => _db = db;

        private static NpgsqlParameter P(string n, NpgsqlDbType t, object? v) => new(n, t) { Value = v ?? DBNull.Value };

        public async Task<LateFeeReceivedRow> GetReceivedBetweenAsync(string schoolId, DateTime fromUtc, DateTime toExclusiveUtc, CancellationToken ct) =>
            (await _db.Database.SqlQueryRaw<LateFeeReceivedRow>(@"
SELECT COALESCE(sum(fee_amount), 0) AS ""FeeAmount"", COALESCE(sum(late_fee_amount), 0) AS ""LateFeeAmount"", COALESCE(sum(tax_amount), 0) AS ""TaxAmount"", COALESCE(sum(amount), 0) AS ""TotalAmount""
  FROM public.tb_fee_payments WHERE school_id = @school AND status = 'Active' AND payment_date >= @from AND payment_date < @to",
                P("school", NpgsqlDbType.Varchar, schoolId), P("from", NpgsqlDbType.TimestampTz, fromUtc), P("to", NpgsqlDbType.TimestampTz, toExclusiveUtc)).ToListAsync(ct)).Single();

        public async Task<decimal> GetLateFeeCollectedAsync(string schoolId, long academicYearId, DateTime? fromUtc, DateTime? toExclusiveUtc, CancellationToken ct) =>
            (await _db.Database.SqlQueryRaw<decimal>(@"
SELECT COALESCE(sum(l.amount), 0) AS ""Value""
  FROM public.tb_fee_payment_late_fees l
  JOIN public.tb_fee_payments p ON p.payment_id = l.payment_id AND p.school_id = l.school_id
  JOIN public.tb_student_fee_dues d ON d.fee_due_id = l.fee_due_id AND d.school_id = l.school_id
  JOIN public.tb_student_fee_items i ON i.student_fee_item_id = d.student_fee_item_id AND i.school_id = l.school_id
 WHERE l.school_id = @school AND p.status = 'Active' AND i.academic_year_id = @year
   AND (@from::timestamptz IS NULL OR p.payment_date >= @from) AND (@to::timestamptz IS NULL OR p.payment_date < @to)",
                P("school", NpgsqlDbType.Varchar, schoolId), P("year", NpgsqlDbType.Bigint, academicYearId), P("from", NpgsqlDbType.TimestampTz, fromUtc), P("to", NpgsqlDbType.TimestampTz, toExclusiveUtc)).ToListAsync(ct)).Single();

        // The same scope as the other fee reports: students enrolled in the year (optionally one class and/or section); an EXISTS so a student never counts twice.
        private const string Scope = @"
   AND (@class::bigint IS NULL AND @section::bigint IS NULL OR EXISTS (SELECT 1 FROM public.tb_student_enrollments e JOIN public.tb_sections s ON s.section_id = e.class_section_id AND s.school_id = @school
        WHERE e.school_id = @school AND e.student_id = i.student_id AND e.academic_year_id = @year AND (@section::bigint IS NULL OR e.class_section_id = @section) AND (@class::bigint IS NULL OR s.class_id = @class)))";

        public Task<List<LateFeeMonthRow>> GetByMonthAsync(string schoolId, long academicYearId, long? classId, long? sectionId, CancellationToken ct) =>
            _db.Database.SqlQueryRaw<LateFeeMonthRow>(@"
WITH late AS (
    SELECT extract(year FROM p.payment_date AT TIME ZONE 'Asia/Kolkata')::int AS y, extract(month FROM p.payment_date AT TIME ZONE 'Asia/Kolkata')::int AS m, sum(l.amount) AS late_fee
      FROM public.tb_fee_payment_late_fees l
      JOIN public.tb_fee_payments p ON p.payment_id = l.payment_id AND p.school_id = l.school_id
      JOIN public.tb_student_fee_dues d ON d.fee_due_id = l.fee_due_id AND d.school_id = l.school_id
      JOIN public.tb_student_fee_items i ON i.student_fee_item_id = d.student_fee_item_id AND i.school_id = l.school_id
     WHERE l.school_id = @school AND p.status = 'Active' AND i.academic_year_id = @year" + Scope + @"
     GROUP BY 1, 2),
tax AS (
    SELECT extract(year FROM p.payment_date AT TIME ZONE 'Asia/Kolkata')::int AS y, extract(month FROM p.payment_date AT TIME ZONE 'Asia/Kolkata')::int AS m, sum(p.tax_amount) AS tax
      FROM public.tb_fee_payments p
     WHERE p.school_id = @school AND p.status = 'Active' AND p.tax_amount > 0
       AND EXISTS (SELECT 1 FROM public.tb_fee_payment_allocations a JOIN public.tb_student_fee_dues d ON d.fee_due_id = a.fee_due_id JOIN public.tb_student_fee_items i ON i.student_fee_item_id = d.student_fee_item_id
                    WHERE a.payment_id = p.payment_id AND i.school_id = @school AND i.academic_year_id = @year" + Scope + @")
     GROUP BY 1, 2)
SELECT COALESCE(late.y, tax.y) AS ""Year"", COALESCE(late.m, tax.m) AS ""Month"", COALESCE(late.late_fee, 0) AS ""LateFee"", COALESCE(tax.tax, 0) AS ""Tax""
  FROM late FULL OUTER JOIN tax ON tax.y = late.y AND tax.m = late.m",
                P("school", NpgsqlDbType.Varchar, schoolId), P("year", NpgsqlDbType.Bigint, academicYearId), P("class", NpgsqlDbType.Bigint, classId), P("section", NpgsqlDbType.Bigint, sectionId)).ToListAsync(ct);

        public async Task<bool> ClassExistsAsync(string schoolId, long classId, CancellationToken ct) =>
            (await _db.Database.SqlQueryRaw<int>(@"SELECT count(*)::int AS ""Value"" FROM public.tb_classes WHERE school_id = @school AND class_id = @cls",
                P("school", NpgsqlDbType.Varchar, schoolId), P("cls", NpgsqlDbType.Bigint, classId)).ToListAsync(ct)).Single() > 0;

        // the class of the student in the year of the due (charged rows) or of the payment's earliest due (waived payments)
        private const string ChargedFrom = @"
  FROM public.tb_fee_payment_late_fees l
  JOIN public.tb_fee_payments p ON p.payment_id = l.payment_id AND p.school_id = l.school_id
  JOIN public.tb_students s ON s.student_id = p.student_id AND s.school_id = p.school_id
  JOIN public.tb_student_fee_dues d ON d.fee_due_id = l.fee_due_id AND d.school_id = l.school_id
  JOIN public.tb_student_fee_items i ON i.student_fee_item_id = d.student_fee_item_id AND i.school_id = l.school_id
  JOIN public.tb_fee_types ft ON ft.fee_type_id = i.fee_type_id
  LEFT JOIN public.tb_fee_payment_allocations a ON a.payment_id = l.payment_id AND a.fee_due_id = l.fee_due_id
  LEFT JOIN public.tb_student_enrollments e ON e.student_id = p.student_id AND e.school_id = p.school_id AND e.academic_year_id = i.academic_year_id
  LEFT JOIN public.tb_sections sec ON sec.section_id = e.class_section_id AND sec.school_id = p.school_id
  LEFT JOIN public.tb_classes c ON c.class_id = sec.class_id AND c.school_id = p.school_id
 WHERE l.school_id = @school AND p.status = 'Active' AND p.payment_date >= @from AND p.payment_date < @to AND (@class::bigint IS NULL OR c.class_id = @class)";

        private const string WaivedFrom = @"
  FROM public.tb_fee_payments p
  JOIN public.tb_students s ON s.student_id = p.student_id AND s.school_id = p.school_id
  LEFT JOIN public.tb_users u ON u.user_id = p.late_fee_decided_by
  LEFT JOIN LATERAL (SELECT i.academic_year_id FROM public.tb_fee_payment_allocations a JOIN public.tb_student_fee_dues d ON d.fee_due_id = a.fee_due_id JOIN public.tb_student_fee_items i ON i.student_fee_item_id = d.student_fee_item_id
                      WHERE a.payment_id = p.payment_id ORDER BY d.due_date, a.allocation_id LIMIT 1) y ON true
  LEFT JOIN public.tb_student_enrollments e ON e.student_id = p.student_id AND e.school_id = p.school_id AND e.academic_year_id = y.academic_year_id
  LEFT JOIN public.tb_sections sec ON sec.section_id = e.class_section_id AND sec.school_id = p.school_id
  LEFT JOIN public.tb_classes c ON c.class_id = sec.class_id AND c.school_id = p.school_id
 WHERE p.school_id = @school AND p.status = 'Active' AND p.late_fee_decision = 'WAIVED' AND p.payment_date >= @from AND p.payment_date < @to AND (@class::bigint IS NULL OR c.class_id = @class)";

        private static NpgsqlParameter[] Ps(string school, DateTime from, DateTime to, long? cls) => new[]
        {
            P("school", NpgsqlDbType.Varchar, school), P("from", NpgsqlDbType.TimestampTz, from), P("to", NpgsqlDbType.TimestampTz, to), P("class", NpgsqlDbType.Bigint, cls),
        };

        public async Task<LateFeeReportSummaryRow> GetSummaryAsync(string schoolId, DateTime fromUtc, DateTime toExclusiveUtc, long? classId, CancellationToken ct)
        {
            var charged = (await _db.Database.SqlQueryRaw<LateFeeReportSummaryRow>(@"SELECT count(*)::int AS ""ChargedCount"", COALESCE(sum(l.amount), 0) AS ""ChargedAmount"", 0 AS ""WaivedCount"", 0::numeric AS ""WaivedAmount"" " + ChargedFrom, Ps(schoolId, fromUtc, toExclusiveUtc, classId)).ToListAsync(ct)).Single();
            var waived = (await _db.Database.SqlQueryRaw<LateFeeReportSummaryRow>(@"SELECT 0 AS ""ChargedCount"", 0::numeric AS ""ChargedAmount"", count(*)::int AS ""WaivedCount"", COALESCE(sum((p.late_fee_rule_snapshot->>'total')::numeric), 0) AS ""WaivedAmount"" " + WaivedFrom, Ps(schoolId, fromUtc, toExclusiveUtc, classId)).ToListAsync(ct)).Single();
            return new LateFeeReportSummaryRow { ChargedCount = charged.ChargedCount, ChargedAmount = charged.ChargedAmount, WaivedCount = waived.WaivedCount, WaivedAmount = waived.WaivedAmount };
        }

        public Task<List<LateFeeChargedRow>> ListChargedAsync(string schoolId, DateTime fromUtc, DateTime toExclusiveUtc, long? classId, int offset, int limit, CancellationToken ct) =>
            _db.Database.SqlQueryRaw<LateFeeChargedRow>(@"
SELECT p.payment_id AS ""PaymentId"", p.receipt_number AS ""ReceiptNumber"", p.payment_date AS ""PaymentDate"", p.student_id AS ""StudentId"",
       btrim(concat_ws(' ', s.first_name, s.middle_name, s.last_name)) AS ""StudentName"", c.class_name AS ""ClassName"",
       COALESCE(NULLIF(btrim(a.fee_type_name_snapshot), ''), ft.fee_type_name) AS ""FeeType"", d.due_date AS ""DueDate"", l.days_late AS ""DaysLate"", l.days_charged AS ""DaysCharged"",
       l.base_amount AS ""BaseAmount"", l.amount AS ""Amount"", (count(*) OVER ())::int AS ""TotalRows"" " + ChargedFrom + @"
 ORDER BY p.payment_date DESC, p.payment_id DESC, d.due_date, l.late_fee_id OFFSET @offset LIMIT @limit",
                Ps(schoolId, fromUtc, toExclusiveUtc, classId).Concat(new[] { P("offset", NpgsqlDbType.Integer, offset), P("limit", NpgsqlDbType.Integer, limit) }).ToArray()).ToListAsync(ct);

        public Task<List<LateFeeWaivedRow>> ListWaivedAsync(string schoolId, DateTime fromUtc, DateTime toExclusiveUtc, long? classId, int offset, int limit, CancellationToken ct) =>
            _db.Database.SqlQueryRaw<LateFeeWaivedRow>(@"
SELECT p.payment_id AS ""PaymentId"", p.receipt_number AS ""ReceiptNumber"", p.payment_date AS ""PaymentDate"", p.student_id AS ""StudentId"",
       btrim(concat_ws(' ', s.first_name, s.middle_name, s.last_name)) AS ""StudentName"", c.class_name AS ""ClassName"",
       COALESCE((p.late_fee_rule_snapshot->>'total')::numeric, 0) AS ""WouldHaveBeen"", COALESCE(u.display_name, u.org_user_id) AS ""WaivedBy"", p.late_fee_note AS ""Note"", (count(*) OVER ())::int AS ""TotalRows"" " + WaivedFrom + @"
 ORDER BY p.payment_date DESC, p.payment_id DESC OFFSET @offset LIMIT @limit",
                Ps(schoolId, fromUtc, toExclusiveUtc, classId).Concat(new[] { P("offset", NpgsqlDbType.Integer, offset), P("limit", NpgsqlDbType.Integer, limit) }).ToArray()).ToListAsync(ct);
    }
}
