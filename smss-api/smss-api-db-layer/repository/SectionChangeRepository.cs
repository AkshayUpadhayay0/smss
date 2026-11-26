using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using smss_api_db_layer.context;
using smss_api_db_layer.helper;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    /// <summary>Mid-year section change (Stage 8c). The enrolment is kept and updated in place; class, year, fees and transport are not touched; a history row is written for every change.</summary>
    public class SectionChangeRepository : ISectionChangeRepository
    {
        private const string Act = "(SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status')";
        private readonly dbContext _db;
        public SectionChangeRepository(dbContext db) => _db = db;

        private static NpgsqlParameter P(string n, NpgsqlDbType t, object? v) => new(n, t) { Value = v ?? DBNull.Value };

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);

        public async Task LockAsync(string schoolId, CancellationToken ct)
        {
            var key = "sectionchange:" + schoolId;
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }

        public async Task<SecContext?> GetContextAsync(string schoolId, string studentId, CancellationToken ct) =>
            (await _db.Database.SqlQueryRaw<SecContext>($@"
SELECT btrim(concat_ws(' ', s.first_name, s.middle_name, s.last_name)) AS ""StudentName"", s.admission_number AS ""AdmissionNumber"", (s.status_id = {Act}) AS ""StudentActive"",
       e.enrollment_id AS ""EnrollmentId"", (e.status_id = {Act}) AS ""EnrolmentActive"", e.outcome AS ""Outcome"", y.academic_year_id AS ""YearId"", y.year_name AS ""YearName"", y.is_current AS ""YearIsCurrent"",
       (y.closed_at IS NOT NULL) AS ""YearClosed"", e.enrollment_date AS ""EnrollmentDate"", c.class_id AS ""ClassId"", c.class_name AS ""ClassName"", sec.section_id AS ""SectionId"", sec.section_name AS ""SectionName"",
       e.roll_number AS ""RollNumber"",
       (SELECT count(*)::int FROM public.tb_report_publication_students ps
          JOIN public.tb_report_publications p ON p.publication_id = ps.publication_id AND p.school_id = @school AND p.status = 'PUBLISHED'
          JOIN public.tb_report_schemes sc ON sc.scheme_id = p.scheme_id AND sc.school_id = @school AND sc.academic_year_id = y.academic_year_id
         WHERE ps.school_id = @school AND ps.student_id = s.student_id) AS ""PublishedCards""
  FROM public.tb_students s
  JOIN public.tb_student_enrollments e ON e.student_id = s.student_id AND e.school_id = @school
  JOIN public.tb_academic_years y ON y.academic_year_id = e.academic_year_id AND y.school_id = @school
  JOIN public.tb_sections sec ON sec.section_id = e.class_section_id AND sec.school_id = @school
  JOIN public.tb_classes c ON c.class_id = sec.class_id AND c.school_id = @school
 WHERE s.school_id = @school AND s.student_id = @student
 ORDER BY y.start_date DESC, e.enrollment_id DESC
 LIMIT 1",
                P("school", NpgsqlDbType.Varchar, schoolId), P("student", NpgsqlDbType.Varchar, studentId)).ToListAsync(ct)).FirstOrDefault();

        public Task<List<SecTargetRow>> ListTargetsAsync(string schoolId, long yearId, long classId, long exceptSectionId, CancellationToken ct) =>
            _db.Database.SqlQueryRaw<SecTargetRow>($@"
SELECT sec.section_id AS ""SectionId"", sec.section_name AS ""SectionName"", ays.max_strength::int AS ""MaxStrength"",
       (SELECT count(*)::int FROM public.tb_student_enrollments e
         WHERE e.school_id = @school AND e.academic_year_id = @year AND e.class_section_id = sec.section_id AND e.status_id = {Act} AND {LiveEnrolment.SqlForYear("e")}) AS ""Enrolled""
  FROM public.tb_sections sec
  JOIN public.tb_academic_year_sections ays ON ays.academic_year_id = @year AND ays.section_id = sec.section_id AND ays.school_id = @school AND ays.status_id = {Act}
 WHERE sec.school_id = @school AND sec.class_id = @cls AND sec.section_id <> @except AND sec.status_id = {Act}
 ORDER BY sec.section_name",
                P("school", NpgsqlDbType.Varchar, schoolId), P("year", NpgsqlDbType.Bigint, yearId), P("cls", NpgsqlDbType.Bigint, classId), P("except", NpgsqlDbType.Bigint, exceptSectionId)).ToListAsync(ct);

        public Task<List<string?>> ListRollsAsync(string schoolId, long yearId, long sectionId, CancellationToken ct) =>
            _db.StudentEnrollments.AsNoTracking().Where(e => e.SchoolId == schoolId && e.AcademicYearId == yearId && e.ClassSectionId == sectionId).Select(e => e.RollNumber).ToListAsync(ct);

        public async Task<bool> ApplyAsync(SecChangeWrite w, CancellationToken ct)
        {
            var n = await _db.Database.ExecuteSqlRawAsync(@"
UPDATE public.tb_student_enrollments SET class_section_id = @to, roll_number = @roll, updated_at = CURRENT_TIMESTAMP
 WHERE enrollment_id = @enr AND school_id = @school AND student_id = @student AND academic_year_id = @year AND class_section_id = @from",
                new object[]
                {
                    P("to", NpgsqlDbType.Bigint, w.ToSectionId), P("roll", NpgsqlDbType.Varchar, w.ToRoll), P("enr", NpgsqlDbType.Bigint, w.EnrollmentId), P("school", NpgsqlDbType.Varchar, w.SchoolId),
                    P("student", NpgsqlDbType.Varchar, w.StudentId), P("year", NpgsqlDbType.Bigint, w.YearId), P("from", NpgsqlDbType.Bigint, w.FromSectionId),
                }, ct);
            if (n != 1) return false;
            await _db.Database.ExecuteSqlRawAsync(@"
INSERT INTO public.tb_student_section_changes (school_id, student_id, enrollment_id, academic_year_id, from_section_id, to_section_id, from_roll, to_roll, effective_date, reason, changed_by)
VALUES (@school, @student, @enr, @year, @from, @to, @fromRoll, @toRoll, @date, @reason, @user)",
                new object[]
                {
                    P("school", NpgsqlDbType.Varchar, w.SchoolId), P("student", NpgsqlDbType.Varchar, w.StudentId), P("enr", NpgsqlDbType.Bigint, w.EnrollmentId), P("year", NpgsqlDbType.Bigint, w.YearId),
                    P("from", NpgsqlDbType.Bigint, w.FromSectionId), P("to", NpgsqlDbType.Bigint, w.ToSectionId), P("fromRoll", NpgsqlDbType.Varchar, w.FromRoll), P("toRoll", NpgsqlDbType.Varchar, w.ToRoll),
                    P("date", NpgsqlDbType.Date, w.EffectiveDate), P("reason", NpgsqlDbType.Varchar, w.Reason), P("user", NpgsqlDbType.Bigint, w.UserId),
                }, ct);
            return true;
        }

        public Task<List<SecHistoryRow>> ListHistoryAsync(string schoolId, string studentId, CancellationToken ct) =>
            _db.Database.SqlQueryRaw<SecHistoryRow>(@"
SELECT h.change_id AS ""ChangeId"", y.year_name AS ""YearName"", fs.section_name AS ""FromSection"", ts.section_name AS ""ToSection"", h.from_roll AS ""FromRoll"", h.to_roll AS ""ToRoll"",
       h.effective_date AS ""EffectiveDate"", h.reason AS ""Reason"", u.display_name AS ""ChangedBy"", h.changed_at AS ""ChangedAt""
  FROM public.tb_student_section_changes h
  JOIN public.tb_academic_years y ON y.academic_year_id = h.academic_year_id AND y.school_id = h.school_id
  JOIN public.tb_sections fs ON fs.section_id = h.from_section_id AND fs.school_id = h.school_id
  JOIN public.tb_sections ts ON ts.section_id = h.to_section_id AND ts.school_id = h.school_id
  LEFT JOIN public.tb_users u ON u.user_id = h.changed_by
 WHERE h.school_id = @school AND h.student_id = @student
 ORDER BY h.changed_at DESC, h.change_id DESC",
                P("school", NpgsqlDbType.Varchar, schoolId), P("student", NpgsqlDbType.Varchar, studentId)).ToListAsync(ct);
    }
}
