using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using smss_api_db_layer.context;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    /// <summary>Promotion, read side. Plain SELECTs, every one filtered by the school id; nothing is written.</summary>
    public class PromotionRepository : IPromotionRepository
    {
        private const string Act = "(SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status')";
        private readonly dbContext _db;
        public PromotionRepository(dbContext db) => _db = db;

        private static NpgsqlParameter P(string n, NpgsqlDbType t, object? v) => new(n, t) { Value = v ?? DBNull.Value };
        private static NpgsqlParameter Ids(IReadOnlyCollection<string> ids) => new("ids", NpgsqlDbType.Array | NpgsqlDbType.Varchar) { Value = ids.ToArray() };

        public Task<PromoYearRow?> GetYearAsync(string schoolId, long yearId, CancellationToken ct) =>
            _db.Database.SqlQueryRaw<PromoYearRow>($@"
SELECT y.academic_year_id AS ""YearId"", y.year_name AS ""YearName"", y.start_date AS ""StartDate"", y.end_date AS ""EndDate"", y.is_current AS ""IsCurrent"",
       (y.closed_at IS NOT NULL) AS ""IsClosed"", (y.status_id = {Act}) AS ""IsActive""
  FROM public.tb_academic_years y WHERE y.school_id = @school AND y.academic_year_id = @year",
                P("school", NpgsqlDbType.Varchar, schoolId), P("year", NpgsqlDbType.Bigint, yearId)).ToListAsync(ct).ContinueWith(t => t.Result.FirstOrDefault(), ct);

        public Task<List<PromoClassRow>> ListClassesAsync(string schoolId, CancellationToken ct) =>
            _db.Database.SqlQueryRaw<PromoClassRow>(@"SELECT class_id AS ""ClassId"", class_name AS ""ClassName"", next_class_id AS ""NextClassId"" FROM public.tb_classes WHERE school_id = @school ORDER BY sequence_order, class_name",
                P("school", NpgsqlDbType.Varchar, schoolId)).ToListAsync(ct);

        public Task<List<PromoSectionRow>> ListSectionsAsync(string schoolId, long classId, CancellationToken ct) =>
            _db.Database.SqlQueryRaw<PromoSectionRow>(@"SELECT section_id AS ""SectionId"", class_id AS ""ClassId"", section_name AS ""SectionName"" FROM public.tb_sections WHERE school_id = @school AND class_id = @cls ORDER BY section_name",
                P("school", NpgsqlDbType.Varchar, schoolId), P("cls", NpgsqlDbType.Bigint, classId)).ToListAsync(ct);

        public Task<List<PromoCandidateRow>> ListCandidatesAsync(string schoolId, long sourceYearId, long classId, long? sectionId, CancellationToken ct) =>
            _db.Database.SqlQueryRaw<PromoCandidateRow>($@"
SELECT e.enrollment_id AS ""EnrollmentId"", s.student_id AS ""StudentId"", s.admission_number AS ""AdmissionNumber"",
       btrim(concat_ws(' ', s.first_name, s.middle_name, s.last_name)) AS ""StudentName"",
       sec.section_id AS ""SectionId"", sec.section_name AS ""SectionName"", e.roll_number AS ""RollNumber"", e.outcome AS ""Outcome""
  FROM public.tb_student_enrollments e
  JOIN public.tb_students s ON s.student_id = e.student_id AND s.school_id = @school AND s.status_id = {Act}
  JOIN public.tb_sections sec ON sec.section_id = e.class_section_id AND sec.school_id = @school
 WHERE e.school_id = @school AND e.academic_year_id = @src AND e.status_id = {Act} AND sec.class_id = @cls AND (@sec::bigint IS NULL OR sec.section_id = @sec)
 ORDER BY sec.section_name, CASE WHEN btrim(e.roll_number) ~ '^[0-9]+$' THEN btrim(e.roll_number)::numeric END NULLS LAST, e.roll_number, ""StudentName"", s.student_id",
                P("school", NpgsqlDbType.Varchar, schoolId), P("src", NpgsqlDbType.Bigint, sourceYearId), P("cls", NpgsqlDbType.Bigint, classId), P("sec", NpgsqlDbType.Bigint, sectionId)).ToListAsync(ct);

        public Task<List<PromoTargetEnrollmentRow>> ListTargetEnrollmentsAsync(string schoolId, long targetYearId, IReadOnlyCollection<string> studentIds, CancellationToken ct) =>
            studentIds.Count == 0 ? Task.FromResult(new List<PromoTargetEnrollmentRow>()) :
            _db.Database.SqlQueryRaw<PromoTargetEnrollmentRow>(@"SELECT student_id AS ""StudentId"", enrollment_id AS ""EnrollmentId"", class_section_id AS ""SectionId"" FROM public.tb_student_enrollments WHERE school_id = @school AND academic_year_id = @tgt AND student_id = ANY(@ids)",
                P("school", NpgsqlDbType.Varchar, schoolId), P("tgt", NpgsqlDbType.Bigint, targetYearId), Ids(studentIds)).ToListAsync(ct);

        public Task<List<PromoTeacherDecisionRow>> ListTeacherDecisionsAsync(string schoolId, long sourceYearId, IReadOnlyCollection<string> studentIds, CancellationToken ct) =>
            studentIds.Count == 0 ? Task.FromResult(new List<PromoTeacherDecisionRow>()) :
            _db.Database.SqlQueryRaw<PromoTeacherDecisionRow>(@"
SELECT DISTINCT ON (r.student_id) r.student_id AS ""StudentId"", r.promotion_decision AS ""Decision"", r.promoted_to AS ""PromotedTo""
  FROM public.tb_report_year_student r JOIN public.tb_report_schemes sc ON sc.scheme_id = r.scheme_id AND sc.school_id = r.school_id
 WHERE r.school_id = @school AND sc.academic_year_id = @src AND r.student_id = ANY(@ids) AND r.promotion_decision IS NOT NULL
 ORDER BY r.student_id, r.updated_at DESC, r.scheme_id DESC",
                P("school", NpgsqlDbType.Varchar, schoolId), P("src", NpgsqlDbType.Bigint, sourceYearId), Ids(studentIds)).ToListAsync(ct);

        public Task<List<PromoDueRow>> ListUnpaidDuesAsync(string schoolId, long sourceYearId, IReadOnlyCollection<string> studentIds, CancellationToken ct) =>
            studentIds.Count == 0 ? Task.FromResult(new List<PromoDueRow>()) :
            _db.Database.SqlQueryRaw<PromoDueRow>(@"
SELECT d.student_id AS ""StudentId"", d.fee_due_id AS ""FeeDueId"", ft.fee_type_name AS ""FeeTypeName"", d.due_date AS ""DueDate"", d.amount_due AS ""AmountDue"", d.amount_paid AS ""AmountPaid""
  FROM public.tb_student_fee_dues d
  JOIN public.tb_student_fee_items i ON i.student_fee_item_id = d.student_fee_item_id AND i.school_id = @school AND i.academic_year_id = @src
  JOIN public.tb_fee_types ft ON ft.fee_type_id = i.fee_type_id AND ft.school_id = @school
 WHERE d.school_id = @school AND d.student_id = ANY(@ids) AND d.amount_due > d.amount_paid
 ORDER BY d.student_id, d.due_date, ft.fee_type_name",
                P("school", NpgsqlDbType.Varchar, schoolId), P("src", NpgsqlDbType.Bigint, sourceYearId), Ids(studentIds)).ToListAsync(ct);

        public Task<List<PromoOfferRow>> ListTargetOfferAsync(string schoolId, long targetYearId, CancellationToken ct) =>
            _db.Database.SqlQueryRaw<PromoOfferRow>($@"
SELECT c.class_id AS ""ClassId"", c.class_name AS ""ClassName"", c.sequence_order AS ""Sequence"", sec.section_id AS ""SectionId"", sec.section_name AS ""SectionName"",
       ays.max_strength::int AS ""MaxStrength"",
       CASE WHEN sec.section_id IS NULL THEN 0 ELSE (SELECT count(*)::int FROM public.tb_student_enrollments e WHERE e.school_id = @school AND e.academic_year_id = @tgt AND e.class_section_id = sec.section_id AND e.status_id = {Act}) END AS ""Enrolled"",
       EXISTS (SELECT 1 FROM public.tb_fee_structures fs WHERE fs.school_id = @school AND fs.academic_year_id = @tgt AND fs.class_id = c.class_id AND fs.isactive) AS ""HasFeeStructure""
  FROM public.tb_academic_year_classes ayc
  JOIN public.tb_classes c ON c.class_id = ayc.class_id AND c.school_id = @school AND c.status_id = {Act}
  LEFT JOIN public.tb_academic_year_sections ays ON ays.academic_year_id = ayc.academic_year_id AND ays.class_id = ayc.class_id AND ays.school_id = @school AND ays.status_id = {Act}
  LEFT JOIN public.tb_sections sec ON sec.section_id = ays.section_id AND sec.school_id = @school
 WHERE ayc.school_id = @school AND ayc.academic_year_id = @tgt AND ayc.status_id = {Act}
 ORDER BY c.sequence_order, c.class_name, sec.section_name",
                P("school", NpgsqlDbType.Varchar, schoolId), P("tgt", NpgsqlDbType.Bigint, targetYearId)).ToListAsync(ct);

        public Task<List<PromoRollRow>> ListTargetRollsAsync(string schoolId, long targetYearId, CancellationToken ct) =>
            _db.Database.SqlQueryRaw<PromoRollRow>(@"SELECT class_section_id AS ""SectionId"", roll_number AS ""RollNumber"" FROM public.tb_student_enrollments WHERE school_id = @school AND academic_year_id = @tgt AND roll_number IS NOT NULL AND btrim(roll_number) <> ''",
                P("school", NpgsqlDbType.Varchar, schoolId), P("tgt", NpgsqlDbType.Bigint, targetYearId)).ToListAsync(ct);
    }
}
