using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class StudentDocumentRepository : IStudentDocumentRepository
    {
        private readonly dbContext _db;
        public StudentDocumentRepository(dbContext db) => _db = db;

        private static string Name(string first, string? middle, string? last) =>
            string.Join(' ', new[] { first, middle, last }.Where(p => !string.IsNullOrWhiteSpace(p)));

        public Task<int?> GetGeneralStatusIdAsync(string name, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == name && s.Stype == "general status").Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public async Task<StudentDocStudentRow?> GetStudentAsync(string schoolId, string studentId, CancellationToken ct)
        {
            var s = await _db.Students.AsNoTracking().Where(x => x.SchoolId == schoolId && x.StudentId == studentId)
                .Select(x => new { x.StudentId, x.FirstName, x.MiddleName, x.LastName, x.AdmissionNumber, x.AdmissionDate, x.StatusId }).FirstOrDefaultAsync(ct);
            if (s == null) return null;
            var classes = await ClassesOfAsync(schoolId, new[] { studentId }, ct);
            classes.TryGetValue(studentId, out var c);
            return new StudentDocStudentRow(s.StudentId, Name(s.FirstName, s.MiddleName, s.LastName), s.AdmissionNumber, s.AdmissionDate, s.StatusId, c.Class, c.Section);
        }

        /// <summary>The class and section of each student's most recent enrolment.</summary>
        private async Task<Dictionary<string, (string? Class, string? Section)>> ClassesOfAsync(string schoolId, IReadOnlyCollection<string> studentIds, CancellationToken ct)
        {
            var rows = await (from e in _db.StudentEnrollments.AsNoTracking()
                              where e.SchoolId == schoolId && studentIds.Contains(e.StudentId)
                              join sec in _db.Sections.AsNoTracking() on e.ClassSectionId equals sec.SectionId
                              join c in _db.Classes.AsNoTracking() on sec.ClassId equals c.ClassId
                              select new { e.StudentId, e.EnrollmentId, c.ClassName, sec.SectionName }).ToListAsync(ct);
            return rows.GroupBy(r => r.StudentId).ToDictionary(g => g.Key, g => { var r = g.OrderByDescending(x => x.EnrollmentId).First(); return ((string?)r.ClassName, (string?)r.SectionName); });
        }

        public Task<string?> GetSchoolCodeAsync(string schoolId, CancellationToken ct) =>
            _db.Schools.AsNoTracking().Where(s => s.SchoolId == schoolId).Select(s => (string?)s.SchoolCode).FirstOrDefaultAsync(ct);

        public Task<bool> DocumentTypeUsableAsync(string schoolId, long documentTypeId, int activeStatusId, CancellationToken ct) =>
            _db.DocumentTypes.AnyAsync(t => t.SchoolId == schoolId && t.DocumentTypeId == documentTypeId && t.StatusId == activeStatusId && (t.AppliesTo == "STUDENT" || t.AppliesTo == "BOTH"), ct);

        public async Task<List<RequiredDocTypeRow>> GetRequiredTypesAsync(string schoolId, int activeStatusId, CancellationToken ct) =>
            (await _db.DocumentTypes.AsNoTracking()
                .Where(t => t.SchoolId == schoolId && t.StatusId == activeStatusId && t.IsRequired && t.RequiredFrom != null && (t.AppliesTo == "STUDENT" || t.AppliesTo == "BOTH"))
                .OrderBy(t => t.DocumentName).Select(t => new { t.DocumentTypeId, t.DocumentName, t.DocumentCode, From = t.RequiredFrom!.Value }).ToListAsync(ct))
            .Select(t => new RequiredDocTypeRow(t.DocumentTypeId, t.DocumentName, t.DocumentCode, t.From)).ToList();

        public async Task<List<StudentDocumentRow>> GetDocumentsAsync(string schoolId, string studentId, bool includeVoided, CancellationToken ct)
        {
            var rows = await (from d in _db.StudentDocuments.AsNoTracking()
                              where d.SchoolId == schoolId && d.StudentId == studentId && (includeVoided || d.VoidedAt == null)
                              join t in _db.DocumentTypes.AsNoTracking() on d.DocumentTypeId equals t.DocumentTypeId
                              select new
                              {
                                  d, t.DocumentName,
                                  By = _db.Users.Where(u => u.UserId == (d.Status == "WAIVED" ? d.WaivedBy : d.CreatedBy)).Select(u => u.DisplayName ?? u.OrgUserId).FirstOrDefault()
                              }).ToListAsync(ct);
            return rows.OrderByDescending(r => r.d.CreatedAt).ThenByDescending(r => r.d.StudentDocumentId).Select(r => new StudentDocumentRow(r.d, r.DocumentName, r.By)).ToList();
        }

        public Task<TbStudentDocument?> GetDocumentAsync(string schoolId, string studentId, long documentId, bool tracked, CancellationToken ct)
        {
            var q = tracked ? _db.StudentDocuments.AsTracking() : _db.StudentDocuments.AsNoTracking();
            return q.Where(d => d.SchoolId == schoolId && d.StudentId == studentId && d.StudentDocumentId == documentId).FirstOrDefaultAsync(ct);
        }

        public Task<TbStudentDocument?> GetActiveForTypeAsync(string schoolId, string studentId, long documentTypeId, CancellationToken ct) =>
            _db.StudentDocuments.AsTracking().Where(d => d.SchoolId == schoolId && d.StudentId == studentId && d.DocumentTypeId == documentTypeId && d.VoidedAt == null).FirstOrDefaultAsync(ct);

        public async Task AddAsync(TbStudentDocument document, CancellationToken ct) => await _db.StudentDocuments.AddAsync(document, ct);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);
        public async Task LockStudentAsync(string schoolId, string studentId, CancellationToken ct)
        {
            var key = $"student-documents:{schoolId}:{studentId}";
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
        public void ClearTracking() => _db.ChangeTracker.Clear();

        // ---------------------------------------------------------------- pending (derived, never stored)
        private async Task<Dictionary<string, List<string>>> MissingByStudentAsync(string schoolId, int activeStudentStatusId, int activeTypeStatusId, CancellationToken ct)
        {
            var missing = new Dictionary<string, List<string>>();
            // who is on the list: ACTIVE students with an ACTIVE enrolment in the school's current-flagged year (a student who passed out or left, or who is deactivated, stays on no list)
            var currentYearId = await _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.IsCurrent).Select(y => (long?)y.AcademicYearId).FirstOrDefaultAsync(ct);
            if (currentYearId == null) return missing;
            foreach (var t in await GetRequiredTypesAsync(schoolId, activeTypeStatusId, ct))
            {
                var typeId = t.TypeId;
                var from = t.RequiredFrom;
                var ids = await _db.Students.AsNoTracking()
                    .Where(s => s.SchoolId == schoolId && s.StatusId == activeStudentStatusId && s.AdmissionDate >= from
                                && _db.StudentEnrollments.Any(e => e.SchoolId == schoolId && e.StudentId == s.StudentId && e.AcademicYearId == currentYearId && e.StatusId == activeStudentStatusId))
                    .Where(s => !_db.StudentDocuments.Any(d => d.SchoolId == schoolId && d.StudentId == s.StudentId && d.DocumentTypeId == typeId && d.VoidedAt == null))
                    .Select(s => s.StudentId).ToListAsync(ct);
                foreach (var id in ids)
                {
                    if (!missing.TryGetValue(id, out var list)) missing[id] = list = new List<string>();
                    list.Add(t.Name);
                }
            }
            return missing;
        }

        public async Task<int> CountPendingStudentsAsync(string schoolId, int activeStudentStatusId, int activeTypeStatusId, CancellationToken ct) =>
            (await MissingByStudentAsync(schoolId, activeStudentStatusId, activeTypeStatusId, ct)).Count;

        public async Task<List<PendingStudentRow>> ListPendingAsync(string schoolId, int activeStudentStatusId, int activeTypeStatusId, long? classId, long? sectionId, string? search, CancellationToken ct)
        {
            var missing = await MissingByStudentAsync(schoolId, activeStudentStatusId, activeTypeStatusId, ct);
            if (missing.Count == 0) return new List<PendingStudentRow>();
            var ids = missing.Keys.ToList();
            if (classId != null || sectionId != null)
            {
                var inClass = _db.StudentEnrollments.AsNoTracking().Where(e => e.SchoolId == schoolId && ids.Contains(e.StudentId));
                if (sectionId != null) inClass = inClass.Where(e => e.ClassSectionId == sectionId);
                if (classId != null) inClass = inClass.Where(e => _db.Sections.Any(s => s.SectionId == e.ClassSectionId && s.ClassId == classId));
                var keep = (await inClass.Select(e => e.StudentId).Distinct().ToListAsync(ct)).ToHashSet();
                ids = ids.Where(keep.Contains).ToList();
            }
            var people = await _db.Students.AsNoTracking().Where(s => s.SchoolId == schoolId && ids.Contains(s.StudentId))
                .Select(s => new { s.StudentId, s.FirstName, s.MiddleName, s.LastName, s.AdmissionNumber }).ToListAsync(ct);
            var classes = await ClassesOfAsync(schoolId, ids, ct);
            var term = search?.Trim();
            return people.Select(p =>
                {
                    classes.TryGetValue(p.StudentId, out var c);
                    return new PendingStudentRow(p.StudentId, Name(p.FirstName, p.MiddleName, p.LastName), p.AdmissionNumber, c.Class, c.Section, missing[p.StudentId]);
                })
                .Where(r => string.IsNullOrEmpty(term) || r.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) || r.AdmissionNumber.Contains(term, StringComparison.OrdinalIgnoreCase) || r.StudentId.Contains(term, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.FullName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.StudentId).ToList();
        }

        // ---------------------------------------------------------------- teacher scope
        public Task<string?> GetLoginIdAsync(string schoolId, long userId, CancellationToken ct) =>
            _db.Users.AsNoTracking().Where(u => u.UserId == userId && u.SchoolId == schoolId).Select(u => u.OrgUserId).FirstOrDefaultAsync(ct);

        public Task<bool> IsClassTeacherOfStudentAsync(string schoolId, string employeeId, string studentId, int activeStatusId, CancellationToken ct) =>
            (from e in _db.StudentEnrollments.AsNoTracking()
             where e.SchoolId == schoolId && e.StudentId == studentId
             join a in _db.ClassTeacherAssignments.AsNoTracking() on new { e.SchoolId, e.AcademicYearId, SectionId = e.ClassSectionId } equals new { a.SchoolId, a.AcademicYearId, a.SectionId }
             where a.EmployeeId == employeeId && a.StatusId == activeStatusId
             select a.EmployeeId).AnyAsync(ct);

        // ---------------------------------------------------------------- carry-over at confirmation
        public async Task<List<TbAdmissionApplicationDocument>> GetApplicationDocumentsToCarryAsync(string schoolId, long admissionId, int activeStatusId, CancellationToken ct)
        {
            var rows = await (from d in _db.AdmissionApplicationDocuments.AsNoTracking()
                              where d.SchoolId == schoolId && d.AdmissionId == admissionId && d.StatusId == activeStatusId
                              join t in _db.DocumentTypes.AsNoTracking() on d.DocumentTypeId equals t.DocumentTypeId
                              where t.SchoolId == schoolId && (t.AppliesTo == "STUDENT" || t.AppliesTo == "BOTH")
                              select d).ToListAsync(ct);
            return rows.GroupBy(d => d.DocumentTypeId).Select(g => g.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.ApplicationDocumentId).First()).OrderBy(d => d.ApplicationDocumentId).ToList();
        }
    }
}
