using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class ReportResultRepository : IReportResultRepository
    {
        private readonly dbContext _db;
        public ReportResultRepository(dbContext db) => _db = db;

        public async Task<List<ResultBand>> GetBandsAsync(string schoolId, long scaleId, CancellationToken ct) =>
            (await _db.GradeScaleBands.AsNoTracking().Where(b => b.SchoolId == schoolId && b.GradeScaleId == scaleId).OrderBy(b => b.Sequence)
                .Select(b => new { b.GradeCode, b.Label, b.MinPercent, b.IsFail, b.Sequence }).ToListAsync(ct))
            .Select(b => new ResultBand(b.GradeCode, b.Label, b.MinPercent, b.IsFail, b.Sequence)).ToList();

        public async Task<List<ResultStudent>> GetStudentsAsync(string schoolId, long academicYearId, IReadOnlyCollection<long> sectionIds, int activeStatusId, CancellationToken ct)
        {
            var ids = sectionIds.ToList();
            return (await (from e in _db.StudentEnrollments.AsNoTracking()
                           join s in _db.Students.AsNoTracking() on e.StudentId equals s.StudentId
                           join sec in _db.Sections.AsNoTracking() on e.ClassSectionId equals sec.SectionId
                           join c in _db.Classes.AsNoTracking() on sec.ClassId equals c.ClassId
                           where e.SchoolId == schoolId && s.SchoolId == schoolId && e.AcademicYearId == academicYearId && ids.Contains(e.ClassSectionId) && e.StatusId == activeStatusId && s.StatusId == activeStatusId
                           select new { s.StudentId, s.AdmissionNumber, s.FirstName, s.MiddleName, s.LastName, e.RollNumber, s.DateOfBirth, e.ClassSectionId, sec.SectionName, c.ClassId, c.ClassName, s.AdmissionDate, s.AddressLine1, s.AddressLine2, s.Pincode, s.MobileNumber }).ToListAsync(ct))
                .Select(r => new ResultStudent(r.StudentId, r.AdmissionNumber, r.FirstName, r.MiddleName, r.LastName, r.RollNumber, r.DateOfBirth, r.ClassSectionId, r.SectionName, r.ClassId, r.ClassName, r.AdmissionDate,
                    string.Join(", ", new[] { r.AddressLine1, r.AddressLine2 }.Where(x => !string.IsNullOrWhiteSpace(x))) + (string.IsNullOrWhiteSpace(r.Pincode) ? "" : " - " + r.Pincode), r.MobileNumber)).ToList();
        }

        public async Task<List<ResultMark>> GetMarksAsync(string schoolId, IReadOnlyCollection<long> examSubjectIds, IReadOnlyCollection<string> studentIds, CancellationToken ct)
        {
            if (examSubjectIds.Count == 0 || studentIds.Count == 0) return new();
            var es = examSubjectIds.ToList(); var st = studentIds.ToList();
            return (await _db.ReportMarks.AsNoTracking().Where(m => m.SchoolId == schoolId && es.Contains(m.ExamSubjectId) && st.Contains(m.StudentId))
                    .Select(m => new { m.ExamSubjectId, m.SkillId, m.StudentId, m.EntryState, m.Marks, m.GradeCode }).ToListAsync(ct))
                .Select(m => new ResultMark(m.ExamSubjectId, m.SkillId, m.StudentId, m.EntryState, m.Marks, m.GradeCode)).ToList();
        }

        public async Task<List<ResultGuardian>> GetParentsAsync(string schoolId, IReadOnlyCollection<string> studentIds, CancellationToken ct)
        {
            if (studentIds.Count == 0) return new();
            var ids = studentIds.ToList();
            return (await (from l in _db.StudentGuardians.AsNoTracking()
                           join g in _db.Guardians.AsNoTracking() on l.GuardianId equals g.GuardianId
                           where g.SchoolId == schoolId && ids.Contains(l.StudentId) && (g.Relationship == "FATHER" || g.Relationship == "MOTHER")
                           select new { l.StudentId, g.Relationship, g.FirstName, g.LastName, g.MobileNumber }).ToListAsync(ct))
                .Select(g => new ResultGuardian(g.StudentId, g.Relationship, string.Join(' ', new[] { g.FirstName, g.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))), g.MobileNumber)).ToList();
        }

        public Task<List<TbReportSubjectRemark>> GetSubjectRemarksAsync(long schemeId, IReadOnlyCollection<string> studentIds, CancellationToken ct)
        {
            var ids = studentIds.ToList();
            return _db.ReportSubjectRemarks.AsNoTracking().Where(r => r.SchemeId == schemeId && ids.Contains(r.StudentId)).ToListAsync(ct);
        }

        public Task<List<TbReportStudentActivityGrade>> GetActivityGradesAsync(long schemeId, long sectionId, CancellationToken ct) =>
            _db.ReportActivityGrades.AsNoTracking().Where(g => g.SchemeId == schemeId && g.SectionId == sectionId).ToListAsync(ct);

        public Task<List<TbReportTermStudent>> GetTermInputsAsync(long schemeId, long sectionId, CancellationToken ct) =>
            _db.ReportTermStudents.AsNoTracking().Where(g => g.SchemeId == schemeId && g.SectionId == sectionId).ToListAsync(ct);

        public async Task<Dictionary<long, string>> GetSubjectNamesAsync(string schoolId, IReadOnlyCollection<long> subjectIds, CancellationToken ct)
        {
            var ids = subjectIds.ToList();
            return await _db.Subjects.AsNoTracking().Where(s => s.SchoolId == schoolId && ids.Contains(s.SubjectId)).ToDictionaryAsync(s => s.SubjectId, s => s.SubjectName, ct);
        }

        public Task<List<long>> GetClassSectionIdsAsync(string schoolId, long classId, int activeStatusId, CancellationToken ct) =>
            _db.Sections.AsNoTracking().Where(s => s.SchoolId == schoolId && s.ClassId == classId && s.StatusId == activeStatusId).Select(s => s.SectionId).ToListAsync(ct);

        public async Task<Dictionary<long, string>> GetSectionNamesAsync(string schoolId, IReadOnlyCollection<long> sectionIds, CancellationToken ct)
        {
            var ids = sectionIds.ToList();
            return await _db.Sections.AsNoTracking().Where(s => s.SchoolId == schoolId && ids.Contains(s.SectionId)).ToDictionaryAsync(s => s.SectionId, s => s.SectionName, ct);
        }

        // ------------------------------------------------------------------ publications
        // The joined rows are shaped with object initialisers (not a positional record), so a Where / OrderBy can still be composed on them and still translate; the record is built last.
        private sealed class PubJoin
        {
            public long PublicationId { get; set; } public long SchemeId { get; set; } public string SchemeName { get; set; } = ""; public long SectionId { get; set; } public long ClassId { get; set; }
            public string ClassName { get; set; } = ""; public string SectionName { get; set; } = ""; public string Scope { get; set; } = ""; public long? TermId { get; set; } public string? TermName { get; set; }
            public int Version { get; set; } public string Status { get; set; } = ""; public long PublishedBy { get; set; } public DateTime PublishedAt { get; set; } public string? Note { get; set; }
            public int StudentCount { get; set; } public string? ExcludedStudents { get; set; }
        }

        private IQueryable<PubJoin> Rows(IQueryable<TbReportPublication> source) =>
            from p in source
            join sch in _db.ReportSchemes.AsNoTracking() on p.SchemeId equals sch.SchemeId
            join sec in _db.Sections.AsNoTracking() on p.SectionId equals sec.SectionId
            join c in _db.Classes.AsNoTracking() on p.ClassId equals c.ClassId
            join t in _db.ReportTerms.AsNoTracking() on p.TermId equals (long?)t.TermId into terms
            from t in terms.DefaultIfEmpty()
            select new PubJoin
            {
                PublicationId = p.PublicationId, SchemeId = p.SchemeId, SchemeName = sch.SchemeName, SectionId = p.SectionId, ClassId = p.ClassId, ClassName = c.ClassName, SectionName = sec.SectionName, Scope = p.Scope,
                TermId = p.TermId, TermName = t == null ? null : t.TermName, Version = p.Version, Status = p.Status, PublishedBy = p.PublishedBy, PublishedAt = p.PublishedAt, Note = p.Note, StudentCount = p.StudentCount, ExcludedStudents = p.ExcludedStudents
            };

        private static PublicationRow ToRow(PubJoin r) => new(r.PublicationId, r.SchemeId, r.SchemeName, r.SectionId, r.ClassId, r.ClassName, r.SectionName, r.Scope, r.TermId, r.TermName, r.Version, r.Status, r.PublishedBy, r.PublishedAt, r.Note, r.StudentCount, r.ExcludedStudents);

        public async Task<List<PublicationRow>> ListPublicationsAsync(string schoolId, long? sectionId, long? schemeId, CancellationToken ct) =>
            (await Rows(_db.ReportPublications.AsNoTracking().Where(p => p.SchoolId == schoolId && (sectionId == null || p.SectionId == sectionId) && (schemeId == null || p.SchemeId == schemeId)))
                .OrderByDescending(p => p.PublishedAt).ThenByDescending(p => p.PublicationId).ToListAsync(ct)).Select(ToRow).ToList();

        public async Task<PublicationRow?> GetPublicationAsync(string schoolId, long publicationId, CancellationToken ct)
        {
            var r = await Rows(_db.ReportPublications.AsNoTracking().Where(p => p.SchoolId == schoolId && p.PublicationId == publicationId)).FirstOrDefaultAsync(ct);
            return r == null ? null : ToRow(r);
        }

        public Task<TbReportPublication?> GetPublicationEntityAsync(string schoolId, long publicationId, CancellationToken ct) =>
            _db.ReportPublications.AsNoTracking().Where(p => p.SchoolId == schoolId && p.PublicationId == publicationId).FirstOrDefaultAsync(ct);

        public Task<TbReportPublication?> GetCurrentAsync(string schoolId, long schemeId, long sectionId, string scope, long? termId, bool track, CancellationToken ct)
        {
            var q = _db.ReportPublications.Where(p => p.SchoolId == schoolId && p.SchemeId == schemeId && p.SectionId == sectionId && p.Scope == scope && p.TermId == termId && p.Status == "PUBLISHED");
            return (track ? q : q.AsNoTracking()).FirstOrDefaultAsync(ct);
        }

        public async Task<int> MaxVersionAsync(string schoolId, long schemeId, long sectionId, string scope, long? termId, CancellationToken ct) =>
            await _db.ReportPublications.AsNoTracking().Where(p => p.SchoolId == schoolId && p.SchemeId == schemeId && p.SectionId == sectionId && p.Scope == scope && p.TermId == termId).Select(p => (int?)p.Version).MaxAsync(ct) ?? 0;

        public async Task<List<PublicationCardRow>> GetCardRowsAsync(long publicationId, CancellationToken ct) =>
            (await _db.ReportPublicationStudents.AsNoTracking().Where(s => s.PublicationId == publicationId)
                .Select(s => new { s.PubStudentId, s.PublicationId, s.StudentId, s.StudentName, s.AdmissionNumber, s.RollNumber, s.ResultStatus, s.TotalObtained, s.TotalMax, s.Percentage, s.GradeCode, s.RankPosition }).ToListAsync(ct))
            .Select(s => new PublicationCardRow(s.PubStudentId, s.PublicationId, s.StudentId, s.StudentName, s.AdmissionNumber, s.RollNumber, s.ResultStatus, s.TotalObtained, s.TotalMax, s.Percentage, s.GradeCode, s.RankPosition)).ToList();

        public Task<TbReportPublicationStudent?> GetCardAsync(string schoolId, long publicationId, string studentId, CancellationToken ct) =>
            _db.ReportPublicationStudents.AsNoTracking().Where(s => s.SchoolId == schoolId && s.PublicationId == publicationId && s.StudentId == studentId).FirstOrDefaultAsync(ct);

        private sealed class MyJoin
        {
            public long PublicationId { get; set; } public long PubStudentId { get; set; } public long SchemeId { get; set; } public string SchemeName { get; set; } = ""; public string ClassName { get; set; } = "";
            public string SectionName { get; set; } = ""; public string Scope { get; set; } = ""; public string? TermName { get; set; } public int Version { get; set; } public DateTime PublishedAt { get; set; }
            public string ResultStatus { get; set; } = ""; public decimal? Percentage { get; set; } public string? GradeCode { get; set; } public int? RankPosition { get; set; }
        }

        private IQueryable<MyJoin> MyCards(string schoolId, string studentId) =>
            from s in _db.ReportPublicationStudents.AsNoTracking()
            join p in _db.ReportPublications.AsNoTracking() on s.PublicationId equals p.PublicationId
            join sch in _db.ReportSchemes.AsNoTracking() on p.SchemeId equals sch.SchemeId
            join sec in _db.Sections.AsNoTracking() on p.SectionId equals sec.SectionId
            join c in _db.Classes.AsNoTracking() on p.ClassId equals c.ClassId
            join t in _db.ReportTerms.AsNoTracking() on p.TermId equals (long?)t.TermId into terms
            from t in terms.DefaultIfEmpty()
            where s.SchoolId == schoolId && s.StudentId == studentId && p.SchoolId == schoolId && p.Status == "PUBLISHED"      // only the CURRENT published version: never a superseded one, never a draft
            select new MyJoin
            {
                PublicationId = p.PublicationId, PubStudentId = s.PubStudentId, SchemeId = p.SchemeId, SchemeName = sch.SchemeName, ClassName = c.ClassName, SectionName = sec.SectionName, Scope = p.Scope,
                TermName = t == null ? null : t.TermName, Version = p.Version, PublishedAt = p.PublishedAt, ResultStatus = s.ResultStatus, Percentage = s.Percentage, GradeCode = s.GradeCode, RankPosition = s.RankPosition
            };

        private static MyCardRow ToMy(MyJoin r) => new(r.PublicationId, r.PubStudentId, r.SchemeId, r.SchemeName, r.ClassName, r.SectionName, r.Scope, r.TermName, r.Version, r.PublishedAt, r.ResultStatus, r.Percentage, r.GradeCode, r.RankPosition);

        public async Task<List<MyCardRow>> GetMyCardsAsync(string schoolId, string studentId, CancellationToken ct) => (await MyCards(schoolId, studentId).OrderByDescending(r => r.PublishedAt).ToListAsync(ct)).Select(ToMy).ToList();
        public async Task<MyCardRow?> GetMyCardAsync(string schoolId, string studentId, long publicationId, CancellationToken ct)
        {
            var r = await MyCards(schoolId, studentId).Where(x => x.PublicationId == publicationId).FirstOrDefaultAsync(ct);
            return r == null ? null : ToMy(r);
        }

        public async Task LockPublicationKeyAsync(long schemeId, long sectionId, string scope, long? termId, CancellationToken ct)
        {
            var key = $"report-publication:{schemeId}:{sectionId}:{scope}:{termId ?? 0}";
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
        public void Add(object entity) => _db.Add(entity);
        public void AddRange(IEnumerable<object> entities) => _db.AddRange(entities);
        public void ClearTracking() => _db.ChangeTracker.Clear();
    }
}
