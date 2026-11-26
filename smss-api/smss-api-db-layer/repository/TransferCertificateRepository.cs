using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using smss_api_db_layer.context;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    /// <summary>Transfer certificates (Stage 8b). Every statement is filtered by the school id; a certificate row is never deleted.</summary>
    public class TransferCertificateRepository : ITransferCertificateRepository
    {
        private readonly dbContext _db;
        public TransferCertificateRepository(dbContext db) => _db = db;

        private static NpgsqlParameter P(string n, NpgsqlDbType t, object? v) => new(n, t) { Value = v ?? DBNull.Value };

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);

        public async Task<TcContext?> GetContextAsync(string schoolId, string studentId, CancellationToken ct) =>
            (await _db.Database.SqlQueryRaw<TcContext>(@"
SELECT s.student_id AS ""StudentId"", s.first_name AS ""FirstName"", s.middle_name AS ""MiddleName"", s.last_name AS ""LastName"", s.admission_number AS ""AdmissionNumber"", s.date_of_birth AS ""DateOfBirth"",
       s.admission_date AS ""AdmissionDate"", g.gender_name AS ""Gender"",
       e.enrollment_id AS ""EnrollmentId"", e.outcome AS ""Outcome"", e.outcome_at AS ""OutcomeAt"", e.leaving_date AS ""LeavingDate"", e.leaving_reason AS ""LeavingReason"", y.academic_year_id AS ""YearId"",
       y.year_name AS ""YearName"", c.class_name AS ""ClassName"", sec.section_name AS ""SectionName"", e.enrollment_date AS ""EnrollmentDate"",
       pi.decision AS ""EnteredBy"", fc.class_name AS ""FromClassName"", tc.certificate_id AS ""ExistingCertificateId"", tc.certificate_number AS ""ExistingCertificateNumber"",
       sch.school_name AS ""SchoolName"", sch.school_code AS ""SchoolCode"", bt.board_name AS ""BoardName"", sch.school_establish_year AS ""EstablishYear"", sch.address_line1 AS ""AddressLine1"",
       sch.address_line2 AS ""AddressLine2"", ci.city_name AS ""CityName"", st.sname AS ""StateName"", sch.pincode AS ""Pincode"", sch.email AS ""SchoolEmail"", sch.mobile_number AS ""SchoolMobile"",
       sch.website AS ""Website"",
       (SELECT u.display_name FROM public.tb_users u JOIN public.tb_user_roles ur ON ur.user_id = u.user_id JOIN public.lut_roles r ON r.role_id = ur.role_id AND r.role_code = 'PRINCIPAL'
         WHERE u.school_id = @school AND u.status_id = (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status') ORDER BY u.user_id LIMIT 1) AS ""PrincipalName"",
       (SELECT btrim(concat_ws(' ', gd.first_name, gd.last_name)) FROM public.tb_student_guardians l JOIN public.tb_guardians gd ON gd.guardian_id = l.guardian_id AND gd.school_id = @school
         WHERE l.student_id = s.student_id ORDER BY l.is_primary DESC, l.student_guardian_id LIMIT 1) AS ""GuardianName"",
       (SELECT gd.relationship FROM public.tb_student_guardians l JOIN public.tb_guardians gd ON gd.guardian_id = l.guardian_id AND gd.school_id = @school
         WHERE l.student_id = s.student_id ORDER BY l.is_primary DESC, l.student_guardian_id LIMIT 1) AS ""GuardianRelationship"",
       (SELECT btrim(concat_ws(' ', gd.first_name, gd.last_name)) FROM public.tb_student_guardians l JOIN public.tb_guardians gd ON gd.guardian_id = l.guardian_id AND gd.school_id = @school
         WHERE l.student_id = s.student_id AND upper(gd.relationship) = 'FATHER' ORDER BY l.is_primary DESC, l.student_guardian_id LIMIT 1) AS ""FatherName"",
       (SELECT btrim(concat_ws(' ', gd.first_name, gd.last_name)) FROM public.tb_student_guardians l JOIN public.tb_guardians gd ON gd.guardian_id = l.guardian_id AND gd.school_id = @school
         WHERE l.student_id = s.student_id AND upper(gd.relationship) = 'MOTHER' ORDER BY l.is_primary DESC, l.student_guardian_id LIMIT 1) AS ""MotherName""
  FROM public.tb_students s
  JOIN public.tb_schools sch ON sch.school_id = s.school_id
  JOIN public.tb_student_enrollments e ON e.student_id = s.student_id AND e.school_id = @school
  JOIN public.tb_academic_years y ON y.academic_year_id = e.academic_year_id AND y.school_id = @school
  LEFT JOIN public.lut_gender g ON g.gender_id = s.gender_id
  LEFT JOIN public.tb_sections sec ON sec.section_id = e.class_section_id AND sec.school_id = @school
  LEFT JOIN public.tb_classes c ON c.class_id = sec.class_id AND c.school_id = @school
  LEFT JOIN public.lut_board_type bt ON bt.board_type_id = sch.board_type_id
  LEFT JOIN public.lut_city ci ON ci.cid = sch.country_id AND ci.sid = sch.state_id AND ci.did = sch.district_id AND ci.city_id = sch.city_id
  LEFT JOIN public.lut_state st ON st.cid = sch.country_id AND st.sid = sch.state_id
  LEFT JOIN LATERAL (SELECT i.decision, i.from_class_id FROM public.tb_promotion_items i WHERE i.school_id = @school AND i.target_enrollment_id = e.enrollment_id AND i.reversed_at IS NULL ORDER BY i.item_id DESC LIMIT 1) pi ON true
  LEFT JOIN public.tb_classes fc ON fc.class_id = pi.from_class_id AND fc.school_id = @school
  LEFT JOIN public.tb_transfer_certificates tc ON tc.school_id = @school AND tc.enrollment_id = e.enrollment_id AND tc.status = 'ISSUED'
 WHERE s.school_id = @school AND s.student_id = @student
 ORDER BY y.start_date DESC, e.enrollment_id DESC
 LIMIT 1",
                P("school", NpgsqlDbType.Varchar, schoolId), P("student", NpgsqlDbType.Varchar, studentId)).ToListAsync(ct)).FirstOrDefault();

        public async Task<int> AllocateNumberAsync(string schoolId, string yearLabel, CancellationToken ct)
        {
            var key = "tc:" + schoolId;
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
            return (await _db.Database.SqlQueryRaw<int>(@"
INSERT INTO public.tb_transfer_certificate_sequences (school_id, year_label, last_number) VALUES (@school, @year, 1)
ON CONFLICT (school_id, year_label) DO UPDATE SET last_number = public.tb_transfer_certificate_sequences.last_number + 1, updated_at = CURRENT_TIMESTAMP
RETURNING last_number AS ""Value""",
                P("school", NpgsqlDbType.Varchar, schoolId), P("year", NpgsqlDbType.Varchar, yearLabel)).ToListAsync(ct)).Single();
        }

        public async Task<long> InsertAsync(TcInsert w, CancellationToken ct)
        {
            var id = (await _db.Database.SqlQueryRaw<long>(@"
INSERT INTO public.tb_transfer_certificates (school_id, student_id, enrollment_id, certificate_number, year_label, sequence_no, snapshot, conduct, remarks, fees_cleared, override_applied, override_reason,
                                             outstanding_amount, issued_by)
VALUES (@school, @student, @enr, @number, @year, @seq, @snap::jsonb, @conduct, @remarks, @cleared, @ovr, @ovrReason, @out, @user)
RETURNING certificate_id AS ""Value""",
                P("school", NpgsqlDbType.Varchar, w.SchoolId), P("student", NpgsqlDbType.Varchar, w.StudentId), P("enr", NpgsqlDbType.Bigint, w.EnrollmentId), P("number", NpgsqlDbType.Varchar, w.Number),
                P("year", NpgsqlDbType.Varchar, w.YearLabel), P("seq", NpgsqlDbType.Integer, w.Sequence), P("snap", NpgsqlDbType.Text, w.SnapshotJson), P("conduct", NpgsqlDbType.Varchar, w.Conduct),
                P("remarks", NpgsqlDbType.Varchar, w.Remarks), P("cleared", NpgsqlDbType.Boolean, w.FeesCleared), P("ovr", NpgsqlDbType.Boolean, w.OverrideApplied),
                P("ovrReason", NpgsqlDbType.Varchar, w.OverrideApplied ? w.OverrideReason : null), P("out", NpgsqlDbType.Numeric, w.Outstanding), P("user", NpgsqlDbType.Bigint, w.UserId)).ToListAsync(ct)).Single();
            await _db.Database.ExecuteSqlRawAsync(@"INSERT INTO public.tb_transfer_certificate_copies (school_id, certificate_id, kind, copy_no, printed_by) VALUES (@school, @id, 'ORIGINAL', 0, @user)",
                new object[] { P("school", NpgsqlDbType.Varchar, w.SchoolId), P("id", NpgsqlDbType.Bigint, id), P("user", NpgsqlDbType.Bigint, w.UserId) }, ct);
            return id;
        }

        private const string RowSql = @"
SELECT t.certificate_id AS ""CertificateId"", t.student_id AS ""StudentId"", btrim(concat_ws(' ', s.first_name, s.middle_name, s.last_name)) AS ""StudentName"", t.enrollment_id AS ""EnrollmentId"",
       t.certificate_number AS ""CertificateNumber"", t.status AS ""Status"", t.snapshot::text AS ""Snapshot"", t.conduct AS ""Conduct"", t.remarks AS ""Remarks"", t.fees_cleared AS ""FeesCleared"",
       t.override_applied AS ""OverrideApplied"", t.override_reason AS ""OverrideReason"", t.outstanding_amount AS ""OutstandingAmount"", t.issued_at AS ""IssuedAt"", iu.display_name AS ""IssuedBy"",
       t.voided_at AS ""VoidedAt"", vu.display_name AS ""VoidedBy"", t.void_reason AS ""VoidReason"",
       (SELECT count(*)::int FROM public.tb_transfer_certificate_copies c WHERE c.school_id = t.school_id AND c.certificate_id = t.certificate_id AND c.kind = 'DUPLICATE') AS ""Duplicates"",
       cl.class_name AS ""ClassName"", sec.section_name AS ""SectionName"", e.outcome AS ""Outcome""
  FROM public.tb_transfer_certificates t
  JOIN public.tb_students s ON s.student_id = t.student_id AND s.school_id = t.school_id
  JOIN public.tb_student_enrollments e ON e.enrollment_id = t.enrollment_id AND e.school_id = t.school_id
  LEFT JOIN public.tb_sections sec ON sec.section_id = e.class_section_id AND sec.school_id = t.school_id
  LEFT JOIN public.tb_classes cl ON cl.class_id = sec.class_id AND cl.school_id = t.school_id
  LEFT JOIN public.tb_users iu ON iu.user_id = t.issued_by
  LEFT JOIN public.tb_users vu ON vu.user_id = t.voided_by";

        public async Task<TcRow?> GetAsync(string schoolId, long certificateId, CancellationToken ct) =>
            (await _db.Database.SqlQueryRaw<TcRow>(RowSql + " WHERE t.school_id = @school AND t.certificate_id = @id",
                P("school", NpgsqlDbType.Varchar, schoolId), P("id", NpgsqlDbType.Bigint, certificateId)).ToListAsync(ct)).FirstOrDefault();

        public Task<List<TcRow>> ListAsync(string schoolId, string? studentId, string? status, CancellationToken ct) =>
            _db.Database.SqlQueryRaw<TcRow>(RowSql + @" WHERE t.school_id = @school AND (@student::varchar IS NULL OR t.student_id = @student) AND (@status::varchar IS NULL OR t.status = @status)
 ORDER BY t.issued_at DESC, t.certificate_id DESC LIMIT 500",
                P("school", NpgsqlDbType.Varchar, schoolId), P("student", NpgsqlDbType.Varchar, studentId), P("status", NpgsqlDbType.Varchar, status)).ToListAsync(ct);

        public Task<List<TcCopyRow>> ListCopiesAsync(string schoolId, long certificateId, CancellationToken ct) =>
            _db.Database.SqlQueryRaw<TcCopyRow>(@"
SELECT c.kind AS ""Kind"", c.copy_no AS ""CopyNo"", c.printed_at AS ""PrintedAt"", u.display_name AS ""PrintedBy""
  FROM public.tb_transfer_certificate_copies c LEFT JOIN public.tb_users u ON u.user_id = c.printed_by
 WHERE c.school_id = @school AND c.certificate_id = @id ORDER BY c.copy_id",
                P("school", NpgsqlDbType.Varchar, schoolId), P("id", NpgsqlDbType.Bigint, certificateId)).ToListAsync(ct);

        public async Task<int> AddCopyAsync(string schoolId, long certificateId, bool duplicate, long userId, CancellationToken ct)
        {
            var key = $"tcdup:{schoolId}:{certificateId}";
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
            var no = 0;
            if (duplicate)
                no = (await _db.Database.SqlQueryRaw<int>(@"SELECT (COALESCE(MAX(copy_no), 0) + 1)::int AS ""Value"" FROM public.tb_transfer_certificate_copies WHERE school_id = @school AND certificate_id = @id AND kind = 'DUPLICATE'",
                    P("school", NpgsqlDbType.Varchar, schoolId), P("id", NpgsqlDbType.Bigint, certificateId)).ToListAsync(ct)).Single();
            await _db.Database.ExecuteSqlRawAsync(@"INSERT INTO public.tb_transfer_certificate_copies (school_id, certificate_id, kind, copy_no, printed_by) VALUES (@school, @id, @kind, @no, @user)",
                new object[] { P("school", NpgsqlDbType.Varchar, schoolId), P("id", NpgsqlDbType.Bigint, certificateId), P("kind", NpgsqlDbType.Varchar, duplicate ? "DUPLICATE" : "ORIGINAL"),
                               P("no", NpgsqlDbType.Integer, no), P("user", NpgsqlDbType.Bigint, userId) }, ct);
            return no;
        }

        public async Task<bool> VoidAsync(string schoolId, long certificateId, string reason, long userId, CancellationToken ct) =>
            await _db.Database.ExecuteSqlRawAsync(@"
UPDATE public.tb_transfer_certificates SET status = 'VOID', voided_at = CURRENT_TIMESTAMP, voided_by = @user, void_reason = @reason WHERE school_id = @school AND certificate_id = @id AND status = 'ISSUED'",
                new object[] { P("user", NpgsqlDbType.Bigint, userId), P("reason", NpgsqlDbType.Varchar, reason), P("school", NpgsqlDbType.Varchar, schoolId), P("id", NpgsqlDbType.Bigint, certificateId) }, ct) == 1;
    }
}
