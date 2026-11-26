using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

        // ================================================================== 4b-3: the run
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);

        public async Task LockSchoolAsync(string schoolId, CancellationToken ct)
        {
            var key = "promotion:" + schoolId;
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }

        public async Task<long> InsertEnrollmentAsync(string schoolId, string studentId, long yearId, long sectionId, string? roll, DateOnly enrollmentDate, long sourceEnrollmentId, CancellationToken ct)
        {
            var ids = await _db.Database.SqlQueryRaw<long>($@"
WITH ins AS (
    INSERT INTO public.tb_student_enrollments (school_id, student_id, academic_year_id, class_section_id, roll_number, enrollment_date, admission_type_id, status_id)
    SELECT @school, @student, @year, @section, @roll, @date,
           COALESCE((SELECT admission_type_id FROM public.lut_admission_type WHERE type_code = 'EXISTING_STUDENT'), (SELECT admission_type_id FROM public.tb_student_enrollments WHERE enrollment_id = @src AND school_id = @school)),
           {Act}
    RETURNING enrollment_id)
SELECT enrollment_id AS ""Value"" FROM ins",
                P("school", NpgsqlDbType.Varchar, schoolId), P("student", NpgsqlDbType.Varchar, studentId), P("year", NpgsqlDbType.Bigint, yearId), P("section", NpgsqlDbType.Bigint, sectionId),
                P("roll", NpgsqlDbType.Varchar, string.IsNullOrWhiteSpace(roll) ? null : roll), P("date", NpgsqlDbType.Date, enrollmentDate), P("src", NpgsqlDbType.Bigint, sourceEnrollmentId)).ToListAsync(ct);
            return ids.Single();
        }

        public async Task<bool> SetOutcomeAsync(string schoolId, long enrollmentId, string outcome, CancellationToken ct) =>
            await _db.Database.ExecuteSqlRawAsync("UPDATE public.tb_student_enrollments SET outcome = @o, outcome_at = CURRENT_TIMESTAMP WHERE enrollment_id = @id AND school_id = @school AND outcome IS NULL",
                new object[] { P("o", NpgsqlDbType.Varchar, outcome), P("id", NpgsqlDbType.Bigint, enrollmentId), P("school", NpgsqlDbType.Varchar, schoolId) }, ct) == 1;

        public async Task<long> InsertBatchAsync(PromoBatchWrite b, CancellationToken ct) =>
            (await _db.Database.SqlQueryRaw<long>(@"
WITH ins AS (
    INSERT INTO public.tb_promotion_batches (school_id, source_year_id, target_year_id, scope, class_id, section_id, promotion_date, created_by, students_total, promoted_count, repeated_count, passed_out_count, left_count,
                                             skipped_count, override_count, override_reason, no_fee_structure_acknowledged)
    VALUES (@school, @src, @tgt, @scope, @cls, @sec, @date, @by, @total, @promoted, @repeated, @passed, @left, @skipped, @overrides, @reason, @ack)
    RETURNING batch_id)
SELECT batch_id AS ""Value"" FROM ins",
                P("school", NpgsqlDbType.Varchar, b.SchoolId), P("src", NpgsqlDbType.Bigint, b.SourceYearId), P("tgt", NpgsqlDbType.Bigint, b.TargetYearId), P("scope", NpgsqlDbType.Varchar, b.Scope),
                P("cls", NpgsqlDbType.Bigint, b.ClassId), P("sec", NpgsqlDbType.Bigint, b.SectionId), P("date", NpgsqlDbType.Date, b.PromotionDate), P("by", NpgsqlDbType.Bigint, b.CreatedBy),
                P("total", NpgsqlDbType.Integer, b.StudentsTotal), P("promoted", NpgsqlDbType.Integer, b.Promoted), P("repeated", NpgsqlDbType.Integer, b.Repeated), P("passed", NpgsqlDbType.Integer, b.PassedOut),
                P("left", NpgsqlDbType.Integer, b.Left), P("skipped", NpgsqlDbType.Integer, b.Skipped), P("overrides", NpgsqlDbType.Integer, b.Overrides), P("reason", NpgsqlDbType.Varchar, b.OverrideReason),
                P("ack", NpgsqlDbType.Boolean, b.NoFeeStructureAcknowledged)).ToListAsync(ct)).Single();

        public Task InsertItemAsync(string schoolId, long batchId, PromoItemWrite i, CancellationToken ct) =>
            _db.Database.ExecuteSqlRawAsync(@"
INSERT INTO public.tb_promotion_items (batch_id, school_id, student_id, source_enrollment_id, target_enrollment_id, decision, from_class_id, from_section_id, to_class_id, to_section_id, roll_number,
                                       outstanding_amount, override_applied, override_reason, skip_reason)
VALUES (@batch, @school, @student, @src, @tgt, @decision, @fromCls, @fromSec, @toCls, @toSec, @roll, @amount, @ovr, @ovrReason, @skip)",
                new object[]
                {
                    P("batch", NpgsqlDbType.Bigint, batchId), P("school", NpgsqlDbType.Varchar, schoolId), P("student", NpgsqlDbType.Varchar, i.StudentId), P("src", NpgsqlDbType.Bigint, i.SourceEnrollmentId),
                    P("tgt", NpgsqlDbType.Bigint, i.TargetEnrollmentId), P("decision", NpgsqlDbType.Varchar, i.Decision), P("fromCls", NpgsqlDbType.Bigint, i.FromClassId), P("fromSec", NpgsqlDbType.Bigint, i.FromSectionId),
                    P("toCls", NpgsqlDbType.Bigint, i.ToClassId), P("toSec", NpgsqlDbType.Bigint, i.ToSectionId), P("roll", NpgsqlDbType.Varchar, i.RollNumber), P("amount", NpgsqlDbType.Numeric, i.OutstandingAmount),
                    P("ovr", NpgsqlDbType.Boolean, i.OverrideApplied), P("ovrReason", NpgsqlDbType.Varchar, i.OverrideReason), P("skip", NpgsqlDbType.Varchar, i.SkipReason),
                }, ct);

        // ================================================================== pending promotion
        private const string PendingFrom = $@"
  FROM public.tb_student_enrollments e
  JOIN public.tb_students s ON s.student_id = e.student_id AND s.school_id = @school AND s.status_id = {Act}
  JOIN public.tb_sections sec ON sec.section_id = e.class_section_id AND sec.school_id = @school
  JOIN public.tb_classes c ON c.class_id = sec.class_id AND c.school_id = @school
 WHERE e.school_id = @school AND e.academic_year_id = @earlier AND e.status_id = {Act}
   AND (e.outcome IS NULL OR e.outcome NOT IN ('PASSED_OUT', 'LEFT', 'TRANSFERRED_OUT'))
   AND NOT EXISTS (SELECT 1 FROM public.tb_student_enrollments n WHERE n.school_id = @school AND n.student_id = e.student_id AND n.academic_year_id = @current)";

        /// <summary>
        /// The pair pending promotion talks about. A real year-end promotes BEFORE the current flag is switched, so: when an open, Active year starts after the current-flagged one (the nearest such year) the pair is
        /// current to that next year; otherwise it is the year before the current-flagged one to the current-flagged one. No current-flagged year, or neither year exists: no pair.
        /// </summary>
        public async Task<PromoPendingYears?> GetPendingYearsAsync(string schoolId, CancellationToken ct) =>
            (await _db.Database.SqlQueryRaw<PromoPendingYears>($@"
SELECT CASE WHEN nxt.academic_year_id IS NOT NULL THEN cur.academic_year_id ELSE prv.academic_year_id END AS ""SourceYearId"",
       CASE WHEN nxt.academic_year_id IS NOT NULL THEN cur.year_name ELSE prv.year_name END AS ""SourceYearName"",
       CASE WHEN nxt.academic_year_id IS NOT NULL THEN nxt.academic_year_id WHEN prv.academic_year_id IS NOT NULL THEN cur.academic_year_id END AS ""TargetYearId"",
       CASE WHEN nxt.academic_year_id IS NOT NULL THEN nxt.year_name WHEN prv.academic_year_id IS NOT NULL THEN cur.year_name END AS ""TargetYearName"",
       (nxt.academic_year_id IS NULL) AS ""TargetIsCurrent""
  FROM public.tb_academic_years cur
  LEFT JOIN LATERAL (SELECT n.academic_year_id, n.year_name FROM public.tb_academic_years n
                      WHERE n.school_id = cur.school_id AND n.start_date > cur.start_date AND n.closed_at IS NULL AND n.status_id = {Act} ORDER BY n.start_date LIMIT 1) nxt ON true
  LEFT JOIN LATERAL (SELECT p.academic_year_id, p.year_name FROM public.tb_academic_years p WHERE p.school_id = cur.school_id AND p.start_date < cur.start_date ORDER BY p.start_date DESC LIMIT 1) prv ON true
 WHERE cur.school_id = @school AND cur.is_current LIMIT 1",
                P("school", NpgsqlDbType.Varchar, schoolId)).ToListAsync(ct)).FirstOrDefault();

        public async Task<int> CountPendingAsync(string schoolId, DateOnly today, CancellationToken ct)
        {
            var y = await GetPendingYearsAsync(schoolId, ct);
            if (y?.SourceYearId == null) return 0;
            return (await _db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" " + PendingFrom,
                P("school", NpgsqlDbType.Varchar, schoolId), P("earlier", NpgsqlDbType.Bigint, y.SourceYearId), P("current", NpgsqlDbType.Bigint, y.TargetYearId)).ToListAsync(ct)).Single();
        }

        public async Task<List<PromoPendingRow>> ListPendingAsync(string schoolId, DateOnly today, int limit, CancellationToken ct)
        {
            var y = await GetPendingYearsAsync(schoolId, ct);
            if (y?.SourceYearId == null) return new List<PromoPendingRow>();
            return await _db.Database.SqlQueryRaw<PromoPendingRow>(@"
SELECT s.student_id AS ""StudentId"", s.admission_number AS ""AdmissionNumber"", btrim(concat_ws(' ', s.first_name, s.middle_name, s.last_name)) AS ""StudentName"",
       c.class_id AS ""ClassId"", c.class_name AS ""ClassName"", sec.section_id AS ""SectionId"", sec.section_name AS ""SectionName"", e.roll_number AS ""RollNumber"",
       (SELECT r.promotion_decision FROM public.tb_report_year_student r JOIN public.tb_report_schemes sc ON sc.scheme_id = r.scheme_id AND sc.school_id = r.school_id
         WHERE r.school_id = @school AND r.student_id = s.student_id AND sc.academic_year_id = @earlier AND r.promotion_decision IS NOT NULL ORDER BY r.updated_at DESC, r.scheme_id DESC LIMIT 1) AS ""TeacherDecision"",
       COALESCE((SELECT sum(d.amount_due - d.amount_paid) FROM public.tb_student_fee_dues d JOIN public.tb_student_fee_items i ON i.student_fee_item_id = d.student_fee_item_id AND i.school_id = @school AND i.academic_year_id = @earlier
                  WHERE d.school_id = @school AND d.student_id = s.student_id AND d.amount_due > d.amount_paid AND d.due_date <= @today), 0) AS ""Outstanding"""
                + PendingFrom + @"
 ORDER BY c.sequence_order, sec.section_name, ""StudentName"", s.student_id LIMIT @limit",
                P("school", NpgsqlDbType.Varchar, schoolId), P("earlier", NpgsqlDbType.Bigint, y.SourceYearId), P("current", NpgsqlDbType.Bigint, y.TargetYearId), P("today", NpgsqlDbType.Date, today), P("limit", NpgsqlDbType.Integer, limit)).ToListAsync(ct);
        }

        // ================================================================== 4b-4: the correction of one promotion
        public async Task<PromoCorrectionContext?> GetCorrectionContextAsync(string schoolId, string studentId, long sourceYearId, CancellationToken ct) =>
            (await _db.Database.SqlQueryRaw<PromoCorrectionContext>(@"
SELECT btrim(concat_ws(' ', s.first_name, s.middle_name, s.last_name)) AS ""StudentName"", e.enrollment_id AS ""SourceEnrollmentId"", sy.year_name AS ""SourceYearName"", (sy.closed_at IS NOT NULL) AS ""SourceYearClosed"",
       e.outcome AS ""Outcome"", i.item_id AS ""ItemId"", i.batch_id AS ""BatchId"", i.decision AS ""Decision"", t.enrollment_id AS ""TargetEnrollmentId"", ty.academic_year_id AS ""TargetYearId"", ty.year_name AS ""TargetYearName"",
       COALESCE(ty.closed_at IS NOT NULL, false) AS ""TargetYearClosed"", tc.class_id AS ""TargetClassId"", tc.class_name AS ""TargetClassName"", ts.section_id AS ""TargetSectionId"", ts.section_name AS ""TargetSectionName"",
       t.roll_number AS ""TargetRoll"", t.enrollment_date AS ""TargetEnrollmentDate"", t.outcome AS ""TargetOutcome""
  FROM public.tb_student_enrollments e
  JOIN public.tb_students s ON s.student_id = e.student_id AND s.school_id = @school
  JOIN public.tb_academic_years sy ON sy.academic_year_id = e.academic_year_id AND sy.school_id = @school
  LEFT JOIN LATERAL (SELECT pi.* FROM public.tb_promotion_items pi WHERE pi.school_id = @school AND pi.source_enrollment_id = e.enrollment_id AND pi.skip_reason IS NULL AND pi.reversed_at IS NULL ORDER BY pi.item_id DESC LIMIT 1) i ON true
  LEFT JOIN public.tb_student_enrollments t ON t.enrollment_id = i.target_enrollment_id AND t.school_id = @school
  LEFT JOIN public.tb_academic_years ty ON ty.academic_year_id = t.academic_year_id AND ty.school_id = @school
  LEFT JOIN public.tb_sections ts ON ts.section_id = t.class_section_id AND ts.school_id = @school
  LEFT JOIN public.tb_classes tc ON tc.class_id = ts.class_id AND tc.school_id = @school
 WHERE e.school_id = @school AND e.student_id = @student AND e.academic_year_id = @year",
                P("school", NpgsqlDbType.Varchar, schoolId), P("student", NpgsqlDbType.Varchar, studentId), P("year", NpgsqlDbType.Bigint, sourceYearId)).ToListAsync(ct)).FirstOrDefault();

        public async Task<PromoCorrectionBlockers> GetCorrectionBlockersAsync(string schoolId, string studentId, long targetYearId, CancellationToken ct) =>
            (await _db.Database.SqlQueryRaw<PromoCorrectionBlockers>(@"
SELECT
 (SELECT count(*)::int FROM public.tb_student_attendance a WHERE a.school_id = @school AND a.student_id = @student AND a.academic_year_id = @year) AS ""Attendance"",
 (SELECT count(*)::int FROM public.tb_fee_payment_allocations al JOIN public.tb_student_fee_dues d ON d.fee_due_id = al.fee_due_id JOIN public.tb_student_fee_items i ON i.student_fee_item_id = d.student_fee_item_id
   WHERE i.school_id = @school AND i.student_id = @student AND i.academic_year_id = @year) AS ""PaymentAllocations"",
 (SELECT count(*)::int FROM public.tb_student_fee_dues d JOIN public.tb_student_fee_items i ON i.student_fee_item_id = d.student_fee_item_id
   WHERE i.school_id = @school AND i.student_id = @student AND i.academic_year_id = @year AND d.amount_paid > 0) AS ""PaidDues"",
 ((SELECT count(*) FROM public.tb_student_assessment_marks m JOIN public.tb_class_assessments a ON a.assessment_id = m.assessment_id WHERE a.school_id = @school AND a.academic_year_id = @year AND m.student_id = @student)
 + (SELECT count(*) FROM public.tb_report_marks m JOIN public.tb_report_exam_subjects es ON es.exam_subject_id = m.exam_subject_id JOIN public.tb_report_schemes sc ON sc.scheme_id = es.scheme_id AND sc.school_id = @school
     WHERE m.school_id = @school AND m.student_id = @student AND sc.academic_year_id = @year)
 + (SELECT count(*) FROM public.tb_report_term_student x JOIN public.tb_report_schemes sc ON sc.scheme_id = x.scheme_id AND sc.school_id = @school WHERE x.school_id = @school AND x.student_id = @student AND sc.academic_year_id = @year)
 + (SELECT count(*) FROM public.tb_report_year_student x JOIN public.tb_report_schemes sc ON sc.scheme_id = x.scheme_id AND sc.school_id = @school WHERE x.school_id = @school AND x.student_id = @student AND sc.academic_year_id = @year)
 + (SELECT count(*) FROM public.tb_report_student_activity_grades x JOIN public.tb_report_schemes sc ON sc.scheme_id = x.scheme_id AND sc.school_id = @school WHERE x.school_id = @school AND x.student_id = @student AND sc.academic_year_id = @year)
 + (SELECT count(*) FROM public.tb_report_subject_remarks x JOIN public.tb_report_schemes sc ON sc.scheme_id = x.scheme_id AND sc.school_id = @school WHERE x.school_id = @school AND x.student_id = @student AND sc.academic_year_id = @year)
 + (SELECT count(*) FROM public.tb_report_publication_students ps JOIN public.tb_report_publications p ON p.publication_id = ps.publication_id AND p.school_id = @school JOIN public.tb_report_schemes sc ON sc.scheme_id = p.scheme_id AND sc.school_id = @school
     WHERE ps.school_id = @school AND ps.student_id = @student AND sc.academic_year_id = @year))::int AS ""Marks"",
 (SELECT count(*)::int FROM public.tb_student_fee_concessions c WHERE c.student_id = @student AND c.academic_year_id = @year AND EXISTS (SELECT 1 FROM public.tb_students s WHERE s.student_id = c.student_id AND s.school_id = @school)) AS ""Concessions"",
 (SELECT count(*)::int FROM public.tb_student_transport t WHERE t.school_id = @school AND t.student_id = @student AND t.academic_year_id = @year) AS ""Transport""",
                P("school", NpgsqlDbType.Varchar, schoolId), P("student", NpgsqlDbType.Varchar, studentId), P("year", NpgsqlDbType.Bigint, targetYearId)).ToListAsync(ct)).Single();

        public Task<List<string>> ListConcessionNamesAsync(string schoolId, string studentId, long targetYearId, CancellationToken ct) =>
            _db.Database.SqlQueryRaw<string>(@"
SELECT COALESCE(cm.concession_name, 'Concession ' || c.concession_id) AS ""Value""
  FROM public.tb_student_fee_concessions c
  JOIN public.tb_students s ON s.student_id = c.student_id AND s.school_id = @school
  LEFT JOIN public.tb_fee_concessions cm ON cm.concession_id = c.concession_id
 WHERE c.student_id = @student AND c.academic_year_id = @year ORDER BY 1",
                P("school", NpgsqlDbType.Varchar, schoolId), P("student", NpgsqlDbType.Varchar, studentId), P("year", NpgsqlDbType.Bigint, targetYearId)).ToListAsync(ct);

        public async Task<PromoFeeRemoval> CountFeesAsync(string schoolId, string studentId, long yearId, CancellationToken ct) =>
            (await _db.Database.SqlQueryRaw<PromoFeeRemoval>(@"
SELECT (SELECT count(*)::int FROM public.tb_student_fee_items i WHERE i.school_id = @school AND i.student_id = @student AND i.academic_year_id = @year) AS ""FeeItems"",
       (SELECT count(*)::int FROM public.tb_student_fee_dues d JOIN public.tb_student_fee_items i ON i.student_fee_item_id = d.student_fee_item_id WHERE i.school_id = @school AND i.student_id = @student AND i.academic_year_id = @year) AS ""Dues"",
       COALESCE((SELECT sum(d.amount_due) FROM public.tb_student_fee_dues d JOIN public.tb_student_fee_items i ON i.student_fee_item_id = d.student_fee_item_id WHERE i.school_id = @school AND i.student_id = @student AND i.academic_year_id = @year), 0) AS ""DuesAmount"",
       EXISTS (SELECT 1 FROM public.tb_student_fee_structure f WHERE f.school_id = @school AND f.student_id = @student AND f.academic_year_id = @year) AS ""FeeStructure""",
                P("school", NpgsqlDbType.Varchar, schoolId), P("student", NpgsqlDbType.Varchar, studentId), P("year", NpgsqlDbType.Bigint, yearId)).ToListAsync(ct)).Single();

        public async Task<PromoFeeRemoval> RemoveFeesAsync(string schoolId, string studentId, long yearId, CancellationToken ct)
        {
            var counted = await CountFeesAsync(schoolId, studentId, yearId, ct);
            // dues first, then the items they hang on, then the assignment; one statement so it is all or nothing
            await _db.Database.ExecuteSqlRawAsync(@"
WITH items AS (SELECT student_fee_item_id FROM public.tb_student_fee_items WHERE school_id = @school AND student_id = @student AND academic_year_id = @year),
     d AS (DELETE FROM public.tb_student_fee_dues WHERE student_fee_item_id IN (SELECT student_fee_item_id FROM items) AND school_id = @school RETURNING 1),
     i AS (DELETE FROM public.tb_student_fee_items WHERE student_fee_item_id IN (SELECT student_fee_item_id FROM items) AND school_id = @school RETURNING 1)
DELETE FROM public.tb_student_fee_structure WHERE school_id = @school AND student_id = @student AND academic_year_id = @year",
                new object[] { P("school", NpgsqlDbType.Varchar, schoolId), P("student", NpgsqlDbType.Varchar, studentId), P("year", NpgsqlDbType.Bigint, yearId) }, ct);
            return counted;
        }

        public Task ReverseItemAsync(string schoolId, long itemId, CancellationToken ct) =>
            _db.Database.ExecuteSqlRawAsync("UPDATE public.tb_promotion_items SET reversed_at = CURRENT_TIMESTAMP, target_enrollment_id = NULL WHERE item_id = @id AND school_id = @school AND reversed_at IS NULL",
                new object[] { P("id", NpgsqlDbType.Bigint, itemId), P("school", NpgsqlDbType.Varchar, schoolId) }, ct);

        public async Task<bool> DeleteEnrollmentAsync(string schoolId, long enrollmentId, CancellationToken ct) =>
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM public.tb_student_enrollments WHERE enrollment_id = @id AND school_id = @school",
                new object[] { P("id", NpgsqlDbType.Bigint, enrollmentId), P("school", NpgsqlDbType.Varchar, schoolId) }, ct) == 1;

        public async Task<bool> ClearOutcomeAsync(string schoolId, long enrollmentId, string expectedOutcome, CancellationToken ct) =>
            await _db.Database.ExecuteSqlRawAsync("UPDATE public.tb_student_enrollments SET outcome = NULL, outcome_at = NULL WHERE enrollment_id = @id AND school_id = @school AND outcome = @o",
                new object[] { P("id", NpgsqlDbType.Bigint, enrollmentId), P("school", NpgsqlDbType.Varchar, schoolId), P("o", NpgsqlDbType.Varchar, expectedOutcome) }, ct) == 1;

        public async Task<long> InsertCorrectionAsync(PromoCorrectionWrite c, CancellationToken ct) =>
            (await _db.Database.SqlQueryRaw<long>(@"
WITH ins AS (
    INSERT INTO public.tb_promotion_corrections (school_id, student_id, batch_id, item_id, source_enrollment_id, removed_enrollment_id, decision, outcome_cleared, removed, reason, corrected_by)
    VALUES (@school, @student, @batch, @item, @src, @removed, @decision, @outcome, @json::jsonb, @reason, @by)
    RETURNING correction_id)
SELECT correction_id AS ""Value"" FROM ins",
                P("school", NpgsqlDbType.Varchar, c.SchoolId), P("student", NpgsqlDbType.Varchar, c.StudentId), P("batch", NpgsqlDbType.Bigint, c.BatchId), P("item", NpgsqlDbType.Bigint, c.ItemId),
                P("src", NpgsqlDbType.Bigint, c.SourceEnrollmentId), P("removed", NpgsqlDbType.Bigint, c.RemovedEnrollmentId), P("decision", NpgsqlDbType.Varchar, c.Decision),
                P("outcome", NpgsqlDbType.Varchar, c.OutcomeCleared), P("json", NpgsqlDbType.Text, c.RemovedJson), P("reason", NpgsqlDbType.Varchar, c.Reason), P("by", NpgsqlDbType.Bigint, c.CorrectedBy)).ToListAsync(ct)).Single();
    }
}
