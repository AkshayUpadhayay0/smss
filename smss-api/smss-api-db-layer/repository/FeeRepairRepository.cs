using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using smss_api_db_layer.context;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class FeeRepairRepository : IFeeRepairRepository
    {
        private const string Act = "(SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status')";
        private readonly dbContext _db;
        public FeeRepairRepository(dbContext db) { _db = db; }

        public Task<List<MissingFeeRow>> ListMissingAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            _db.Database.SqlQueryRaw<MissingFeeRow>($@"
SELECT s.student_id AS ""StudentId"",
       btrim(concat_ws(' ', s.first_name, s.middle_name, s.last_name)) AS ""StudentName"",
       cl.class_id AS ""ClassId"", cl.class_name AS ""ClassName"", sec.section_name AS ""SectionName"",
       (SELECT fs.fee_structure_id FROM public.tb_fee_structures fs WHERE fs.school_id = @school AND fs.academic_year_id = @year AND fs.class_id = cl.class_id AND fs.isactive LIMIT 1) AS ""FeeStructureId""
  FROM public.tb_student_enrollments e
  JOIN public.tb_students s ON s.student_id = e.student_id AND s.school_id = @school AND s.status_id = {Act}
  JOIN public.tb_sections sec ON sec.section_id = e.class_section_id AND sec.school_id = @school
  JOIN public.tb_classes cl ON cl.class_id = sec.class_id AND cl.school_id = @school
 WHERE e.school_id = @school AND e.academic_year_id = @year AND e.status_id = {Act}
   AND NOT EXISTS (SELECT 1 FROM public.tb_student_fee_structure f WHERE f.school_id = @school AND f.student_id = s.student_id AND f.academic_year_id = @year)
 ORDER BY cl.class_name, sec.section_name, ""StudentName"", s.student_id",
                new NpgsqlParameter("school", NpgsqlDbType.Varchar) { Value = schoolId }, new NpgsqlParameter("year", NpgsqlDbType.Bigint) { Value = academicYearId }).ToListAsync(ct);

        public async Task<List<PreviousChoiceRow>> GetPreviousChoicesAsync(string schoolId, long academicYearId, IReadOnlyCollection<string> studentIds, CancellationToken ct)
        {
            if (studentIds.Count == 0) return new List<PreviousChoiceRow>();
            return await _db.Database.SqlQueryRaw<PreviousChoiceRow>($@"
SELECT i.student_id AS ""StudentId"", i.fee_type_id AS ""FeeTypeId"", i.is_applicable AS ""IsApplicable"", i.installment_plan AS ""InstallmentPlan""
  FROM public.tb_student_fee_items i
  JOIN public.tb_fee_types ft ON ft.fee_type_id = i.fee_type_id AND ft.school_id = @school AND ft.is_optional
 WHERE i.school_id = @school AND i.student_id = ANY(@ids)
   AND i.academic_year_id = (SELECT p.academic_year_id FROM public.tb_academic_years p, public.tb_academic_years c
                              WHERE c.school_id = @school AND c.academic_year_id = @year AND p.school_id = @school AND p.start_date < c.start_date ORDER BY p.start_date DESC LIMIT 1)",
                new NpgsqlParameter("school", NpgsqlDbType.Varchar) { Value = schoolId }, new NpgsqlParameter("year", NpgsqlDbType.Bigint) { Value = academicYearId },
                new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Varchar) { Value = studentIds.ToArray() }).ToListAsync(ct);
        }
    }
}
