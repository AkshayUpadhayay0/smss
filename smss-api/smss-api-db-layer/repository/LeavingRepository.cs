using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using smss_api_db_layer.context;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    /// <summary>Record leaving (Stage 8a). Every statement is filtered by the school id. tb_students.status_id is never touched: "left" is the outcome of the enrolment.</summary>
    public class LeavingRepository : ILeavingRepository
    {
        private const string Act = "(SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status')";
        private readonly dbContext _db;
        public LeavingRepository(dbContext db) => _db = db;

        private static NpgsqlParameter P(string n, NpgsqlDbType t, object? v) => new(n, t) { Value = v ?? DBNull.Value };

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);

        public async Task<LeavingContext?> GetContextAsync(string schoolId, string studentId, CancellationToken ct) =>
            (await _db.Database.SqlQueryRaw<LeavingContext>($@"
SELECT btrim(concat_ws(' ', s.first_name, s.middle_name, s.last_name)) AS ""StudentName"", s.admission_number AS ""AdmissionNumber"", (s.status_id = {Act}) AS ""StudentActive"",
       e.enrollment_id AS ""EnrollmentId"", (e.status_id = {Act}) AS ""EnrolmentActive"", y.academic_year_id AS ""YearId"", y.year_name AS ""YearName"", y.start_date AS ""YearStart"", y.end_date AS ""YearEnd"",
       y.is_current AS ""YearIsCurrent"", (y.closed_at IS NOT NULL) AS ""YearClosed"", e.enrollment_date AS ""EnrollmentDate"", e.outcome AS ""Outcome"", c.class_name AS ""ClassName"", sec.section_name AS ""SectionName""
  FROM public.tb_students s
  JOIN public.tb_student_enrollments e ON e.student_id = s.student_id AND e.school_id = @school
  JOIN public.tb_academic_years y ON y.academic_year_id = e.academic_year_id AND y.school_id = @school
  LEFT JOIN public.tb_sections sec ON sec.section_id = e.class_section_id AND sec.school_id = @school
  LEFT JOIN public.tb_classes c ON c.class_id = sec.class_id AND c.school_id = @school
 WHERE s.school_id = @school AND s.student_id = @student
 ORDER BY y.start_date DESC, e.enrollment_id DESC
 LIMIT 1",
                P("school", NpgsqlDbType.Varchar, schoolId), P("student", NpgsqlDbType.Varchar, studentId)).ToListAsync(ct)).FirstOrDefault();

        public Task<List<LeavingDueRow>> ListUnpaidDuesAsync(string schoolId, string studentId, long yearId, CancellationToken ct) =>
            _db.Database.SqlQueryRaw<LeavingDueRow>(@"
SELECT d.fee_due_id AS ""FeeDueId"", ft.fee_type_name AS ""FeeTypeName"", d.due_date AS ""DueDate"", d.amount_due AS ""AmountDue"", d.amount_paid AS ""AmountPaid""
  FROM public.tb_student_fee_dues d
  JOIN public.tb_student_fee_items i ON i.student_fee_item_id = d.student_fee_item_id AND i.school_id = @school AND i.academic_year_id = @year
  JOIN public.tb_fee_types ft ON ft.fee_type_id = i.fee_type_id AND ft.school_id = @school
 WHERE d.school_id = @school AND d.student_id = @student AND d.amount_due > d.amount_paid
 ORDER BY d.due_date, ft.fee_type_name",
                P("school", NpgsqlDbType.Varchar, schoolId), P("student", NpgsqlDbType.Varchar, studentId), P("year", NpgsqlDbType.Bigint, yearId)).ToListAsync(ct);

        public async Task<bool> ApplyAsync(LeavingWrite w, DateTime nowUtc, CancellationToken ct)
        {
            var n = await _db.Database.ExecuteSqlRawAsync(@"
UPDATE public.tb_student_enrollments
   SET outcome = @outcome, outcome_at = CURRENT_TIMESTAMP, leaving_date = @date, leaving_reason = @reason, outcome_by = @user,
       leaving_override_reason = @ovr, leaving_outstanding = @out, updated_at = CURRENT_TIMESTAMP
 WHERE enrollment_id = @enr AND school_id = @school AND student_id = @student AND outcome IS NULL",
                new object[]
                {
                    P("outcome", NpgsqlDbType.Varchar, w.Outcome), P("date", NpgsqlDbType.Date, w.LeavingDate), P("reason", NpgsqlDbType.Varchar, w.Reason), P("user", NpgsqlDbType.Bigint, w.UserId),
                    P("ovr", NpgsqlDbType.Varchar, w.OverrideApplied ? w.OverrideReason : null), P("out", NpgsqlDbType.Numeric, w.Outstanding), P("enr", NpgsqlDbType.Bigint, w.EnrollmentId),
                    P("school", NpgsqlDbType.Varchar, w.SchoolId), P("student", NpgsqlDbType.Varchar, w.StudentId),
                }, ct);
            if (n != 1) return false;
            // the student's login ends at once: every refresh token of that login is revoked (the 15-minute access token simply expires)
            await _db.Database.ExecuteSqlRawAsync(@"
UPDATE public.tb_refresh_tokens SET revoked_at = @now
 WHERE revoked_at IS NULL AND user_id IN (SELECT u.user_id FROM public.tb_users u WHERE u.school_id = @school AND u.org_user_id = @student AND u.user_type = 'Student')",
                new object[] { P("now", NpgsqlDbType.TimestampTz, nowUtc), P("school", NpgsqlDbType.Varchar, w.SchoolId), P("student", NpgsqlDbType.Varchar, w.StudentId) }, ct);
            await _db.Database.ExecuteSqlRawAsync(@"
INSERT INTO public.tb_student_leaving_log (school_id, student_id, enrollment_id, action, leaving_date, reason, outstanding_amount, override_applied, override_reason, acted_by)
VALUES (@school, @student, @enr, @action, @date, @reason, @out, @ovrApplied, @ovr, @user)",
                new object[]
                {
                    P("school", NpgsqlDbType.Varchar, w.SchoolId), P("student", NpgsqlDbType.Varchar, w.StudentId), P("enr", NpgsqlDbType.Bigint, w.EnrollmentId), P("action", NpgsqlDbType.Varchar, w.Outcome),
                    P("date", NpgsqlDbType.Date, w.LeavingDate), P("reason", NpgsqlDbType.Varchar, w.Reason), P("out", NpgsqlDbType.Numeric, w.Outstanding), P("ovrApplied", NpgsqlDbType.Boolean, w.OverrideApplied),
                    P("ovr", NpgsqlDbType.Varchar, w.OverrideApplied ? w.OverrideReason : null), P("user", NpgsqlDbType.Bigint, w.UserId),
                }, ct);
            return true;
        }

        public async Task<LeavingCorrectionContext?> GetCorrectionContextAsync(string schoolId, string studentId, CancellationToken ct) =>
            (await _db.Database.SqlQueryRaw<LeavingCorrectionContext>(@"
SELECT btrim(concat_ws(' ', s.first_name, s.middle_name, s.last_name)) AS ""StudentName"", e.enrollment_id AS ""EnrollmentId"", y.academic_year_id AS ""YearId"", y.year_name AS ""YearName"",
       (y.closed_at IS NOT NULL) AS ""YearClosed"", e.outcome AS ""Outcome"", e.leaving_date AS ""LeavingDate"",
       (SELECT count(*)::int FROM public.tb_student_enrollments n JOIN public.tb_academic_years ny ON ny.academic_year_id = n.academic_year_id AND ny.school_id = @school
         WHERE n.school_id = @school AND n.student_id = s.student_id AND n.enrollment_id <> e.enrollment_id AND (ny.start_date > y.start_date OR (ny.start_date = y.start_date AND n.enrollment_id > e.enrollment_id))) AS ""LaterEnrolments"",
       (e.leaving_date IS NULL) AS ""FromPromotion"",
       (SELECT count(*)::int FROM public.tb_transfer_certificates tc WHERE tc.school_id = @school AND tc.enrollment_id = e.enrollment_id AND tc.status = 'ISSUED') AS ""Certificates""
  FROM public.tb_students s
  JOIN public.tb_student_enrollments e ON e.student_id = s.student_id AND e.school_id = @school
  JOIN public.tb_academic_years y ON y.academic_year_id = e.academic_year_id AND y.school_id = @school
 WHERE s.school_id = @school AND s.student_id = @student
 ORDER BY (e.outcome IS NULL), y.start_date DESC, e.enrollment_id DESC
 LIMIT 1",
                P("school", NpgsqlDbType.Varchar, schoolId), P("student", NpgsqlDbType.Varchar, studentId)).ToListAsync(ct)).FirstOrDefault();

        public async Task<bool> ApplyCorrectionAsync(string schoolId, string studentId, long enrollmentId, string clearedOutcome, string reason, long userId, CancellationToken ct)
        {
            var n = await _db.Database.ExecuteSqlRawAsync(@"
UPDATE public.tb_student_enrollments
   SET outcome = NULL, outcome_at = NULL, leaving_date = NULL, leaving_reason = NULL, outcome_by = NULL, leaving_override_reason = NULL, leaving_outstanding = NULL, updated_at = CURRENT_TIMESTAMP
 WHERE enrollment_id = @enr AND school_id = @school AND student_id = @student AND outcome = @cleared AND leaving_date IS NOT NULL",
                new object[] { P("enr", NpgsqlDbType.Bigint, enrollmentId), P("school", NpgsqlDbType.Varchar, schoolId), P("student", NpgsqlDbType.Varchar, studentId), P("cleared", NpgsqlDbType.Varchar, clearedOutcome) }, ct);
            if (n != 1) return false;
            await _db.Database.ExecuteSqlRawAsync(@"
INSERT INTO public.tb_student_leaving_log (school_id, student_id, enrollment_id, action, reason, outcome_cleared, acted_by)
VALUES (@school, @student, @enr, 'CORRECTION', @reason, @cleared, @user)",
                new object[] { P("school", NpgsqlDbType.Varchar, schoolId), P("student", NpgsqlDbType.Varchar, studentId), P("enr", NpgsqlDbType.Bigint, enrollmentId), P("reason", NpgsqlDbType.Varchar, reason),
                               P("cleared", NpgsqlDbType.Varchar, clearedOutcome), P("user", NpgsqlDbType.Bigint, userId) }, ct);
            return true;
        }

        public Task<List<LeaverRow>> ListLeaversAsync(string schoolId, long? yearId, CancellationToken ct) =>
            _db.Database.SqlQueryRaw<LeaverRow>(@"
SELECT s.student_id AS ""StudentId"", btrim(concat_ws(' ', s.first_name, s.middle_name, s.last_name)) AS ""StudentName"", s.admission_number AS ""AdmissionNumber"", c.class_name AS ""ClassName"",
       sec.section_name AS ""SectionName"", e.outcome AS ""Outcome"", e.leaving_date AS ""LeavingDate"", e.leaving_reason AS ""Reason"", e.leaving_outstanding AS ""Outstanding"",
       (e.leaving_override_reason IS NOT NULL) AS ""Overridden"", u.display_name AS ""RecordedBy"", y.year_name AS ""YearName""
  FROM public.tb_student_enrollments e
  JOIN public.tb_students s ON s.student_id = e.student_id AND s.school_id = @school
  JOIN public.tb_academic_years y ON y.academic_year_id = e.academic_year_id AND y.school_id = @school
  LEFT JOIN public.tb_sections sec ON sec.section_id = e.class_section_id AND sec.school_id = @school
  LEFT JOIN public.tb_classes c ON c.class_id = sec.class_id AND c.school_id = @school
  LEFT JOIN public.tb_users u ON u.user_id = e.outcome_by
 WHERE e.school_id = @school AND e.outcome IN ('LEFT', 'TRANSFERRED_OUT') AND (@year::bigint IS NULL OR e.academic_year_id = @year)
 ORDER BY e.leaving_date DESC NULLS LAST, e.outcome_at DESC, s.student_id
 LIMIT 500",
                P("school", NpgsqlDbType.Varchar, schoolId), P("year", NpgsqlDbType.Bigint, yearId)).ToListAsync(ct);
    }
}
