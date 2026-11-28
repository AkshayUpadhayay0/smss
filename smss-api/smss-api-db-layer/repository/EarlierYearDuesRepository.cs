using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using smss_api_db_layer.context;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class EarlierYearDuesRepository : IEarlierYearDuesRepository
    {
        private readonly dbContext _db;
        public EarlierYearDuesRepository(dbContext db) { _db = db; }

        public async Task<List<EarlierYearAmountRow>> GetOutstandingAsync(string schoolId, IReadOnlyList<string> studentIds, IReadOnlyList<DateOnly> referenceStarts, DateOnly overdueBefore, CancellationToken ct)
        {
            if (studentIds.Count == 0 || studentIds.Count != referenceStarts.Count) return new();
            return await _db.Database.SqlQueryRaw<EarlierYearAmountRow>(@"
SELECT x.student_id AS ""StudentId"", y.academic_year_id AS ""AcademicYearId"", y.year_name AS ""YearName"", y.start_date AS ""YearStart"",
       SUM(d.amount_due - d.amount_paid) AS ""Outstanding"",
       COUNT(*) FILTER (WHERE d.due_date < @cutoff)::int AS ""OverdueCount""
  FROM unnest(@ids, @starts) AS x(student_id, ref_start)
  JOIN public.tb_student_fee_dues d ON d.school_id = @school AND d.student_id = x.student_id AND d.amount_paid < d.amount_due
  JOIN public.tb_student_fee_items i ON i.student_fee_item_id = d.student_fee_item_id AND i.school_id = @school
  JOIN public.tb_academic_years y ON y.academic_year_id = i.academic_year_id AND y.school_id = @school
 WHERE y.start_date < x.ref_start
 GROUP BY x.student_id, y.academic_year_id, y.year_name, y.start_date
 ORDER BY x.student_id, y.start_date",
                new NpgsqlParameter("school", NpgsqlDbType.Varchar) { Value = schoolId },
                new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Varchar) { Value = studentIds.ToArray() },
                new NpgsqlParameter("starts", NpgsqlDbType.Array | NpgsqlDbType.Date) { Value = referenceStarts.ToArray() },
                new NpgsqlParameter("cutoff", NpgsqlDbType.Date) { Value = overdueBefore }).ToListAsync(ct);
        }

        public Task<DateOnly?> GetYearStartAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.AcademicYearId == academicYearId).Select(y => (DateOnly?)y.StartDate).FirstOrDefaultAsync(ct);

        public Task<List<EarlierDueRow>> GetDuesAsync(string schoolId, string studentId, DateOnly referenceStart, CancellationToken ct) =>
            _db.Database.SqlQueryRaw<EarlierDueRow>(@"
SELECT d.fee_due_id AS ""FeeDueId"", d.student_fee_item_id AS ""StudentFeeItemId"", i.fee_type_id AS ""FeeTypeId"",
       ft.fee_type_name AS ""FeeTypeName"", COALESCE(f.frequency_name, '') AS ""FrequencyName"",
       COALESCE(i.installment_plan, 'NONE') AS ""InstallmentPlan"", d.source AS ""Source"", d.due_date AS ""DueDate"", d.amount_due AS ""AmountDue"", d.amount_paid AS ""AmountPaid"",
       y.academic_year_id AS ""AcademicYearId"", y.year_name AS ""YearName""
  FROM public.tb_student_fee_dues d
  JOIN public.tb_student_fee_items i ON i.student_fee_item_id = d.student_fee_item_id AND i.school_id = @school
  JOIN public.tb_academic_years y ON y.academic_year_id = i.academic_year_id AND y.school_id = @school
  JOIN public.tb_fee_types ft ON ft.fee_type_id = i.fee_type_id AND ft.school_id = @school
  LEFT JOIN public.lut_fee_frequency f ON f.frequency_id = i.frequency_id
 WHERE d.school_id = @school AND d.student_id = @student AND d.amount_paid < d.amount_due AND y.start_date < @ref
 ORDER BY d.due_date, d.fee_due_id",
                new NpgsqlParameter("school", NpgsqlDbType.Varchar) { Value = schoolId },
                new NpgsqlParameter("student", NpgsqlDbType.Varchar) { Value = studentId },
                new NpgsqlParameter("ref", NpgsqlDbType.Date) { Value = referenceStart }).ToListAsync(ct);
    }
}
