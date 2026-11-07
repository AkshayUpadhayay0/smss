using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class ReportMarksRepository : IReportMarksRepository
    {
        private readonly dbContext _db;
        public ReportMarksRepository(dbContext db) => _db = db;

        public Task<int?> ActiveStatusIdAsync(CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == "Active" && s.Stype == "general status").Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public Task<TbAcademicYears?> CurrentYearAsync(string schoolId, int activeStatusId, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.IsCurrent && y.StatusId == activeStatusId).FirstOrDefaultAsync(ct);

        public Task<TbAcademicYears?> YearAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.AcademicYearId == academicYearId).FirstOrDefaultAsync(ct);

        public Task<string?> UserLoginIdAsync(string schoolId, long userId, CancellationToken ct) =>
            _db.Users.AsNoTracking().Where(u => u.UserId == userId && u.SchoolId == schoolId).Select(u => u.OrgUserId).FirstOrDefaultAsync(ct);

        // ------------------------------------------------------------------ one exam subject / section
        public Task<ExamSubjectInfo?> GetExamSubjectAsync(string schoolId, long examSubjectId, CancellationToken ct) =>
            (from es in _db.ReportExamSubjects.AsNoTracking()
             join ex in _db.ReportExams.AsNoTracking() on es.ExamId equals ex.ExamId
             join t in _db.ReportTerms.AsNoTracking() on ex.TermId equals t.TermId
             join sch in _db.ReportSchemes.AsNoTracking() on es.SchemeId equals sch.SchemeId
             join ss in _db.ReportSchemeSubjects.AsNoTracking() on es.SchemeSubjectId equals ss.SchemeSubjectId
             join sub in _db.Subjects.AsNoTracking() on ss.SubjectId equals sub.SubjectId
             join c in _db.Classes.AsNoTracking() on es.ClassId equals c.ClassId
             where es.ExamSubjectId == examSubjectId && es.SchoolId == schoolId && sch.SchoolId == schoolId
             select new ExamSubjectInfo(es.ExamSubjectId, ex.ExamId, ex.ExamName, t.TermId, t.TermName, t.StartDate, t.EndDate, t.Sequence,
                 sch.SchemeId, sch.SchemeName, sch.AcademicYearId, sch.StatusId, sch.EvaluationMode, sch.GradeScaleId, sch.AttendanceSource,
                 ss.SchemeSubjectId, sub.SubjectId, sub.SubjectName, ss.LabelOverride, ss.DisplayOrder, c.ClassId, c.ClassName, es.EntryMode,
                 es.EntryMode == "MARKS" ? (es.MaxMarks ?? ex.DefaultMaxMarks) : (decimal?)null))
            .FirstOrDefaultAsync(ct);

        public Task<SectionInfo?> GetSectionAsync(string schoolId, long sectionId, int activeStatusId, CancellationToken ct) =>
            (from s in _db.Sections.AsNoTracking()
             join c in _db.Classes.AsNoTracking() on s.ClassId equals c.ClassId
             where s.SectionId == sectionId && s.SchoolId == schoolId && c.SchoolId == schoolId
             select new SectionInfo(s.SectionId, c.ClassId, c.ClassName, s.SectionName)).FirstOrDefaultAsync(ct);

        public async Task<List<SkillInfo>> GetSkillsAsync(long schemeSubjectId, CancellationToken ct) =>
            (await _db.ReportSubjectSkills.AsNoTracking().Where(k => k.SchemeSubjectId == schemeSubjectId).OrderBy(k => k.Sequence)
                .Select(k => new { k.SkillId, k.SchemeSubjectId, k.SkillName, k.Sequence }).ToListAsync(ct))
            .Select(k => new SkillInfo(k.SkillId, k.SchemeSubjectId, k.SkillName, k.Sequence)).ToList();

        public async Task<List<BandInfo>> GetBandsAsync(string schoolId, IReadOnlyCollection<long> scaleIds, CancellationToken ct)
        {
            if (scaleIds.Count == 0) return new();
            var ids = scaleIds.ToList();
            return (await _db.GradeScaleBands.AsNoTracking().Where(b => b.SchoolId == schoolId && ids.Contains(b.GradeScaleId)).OrderBy(b => b.GradeScaleId).ThenBy(b => b.Sequence)
                    .Select(b => new { b.GradeScaleId, b.GradeCode, b.Label, b.Sequence }).ToListAsync(ct))
                .Select(b => new BandInfo(b.GradeScaleId, b.GradeCode, b.Label, b.Sequence)).ToList();
        }

        // ------------------------------------------------------------------ eligibility inputs
        public Task<bool> IsMappedAsync(string schoolId, long classId, long subjectId, int activeStatusId, CancellationToken ct) =>
            _db.ClassSubjects.AsNoTracking().AnyAsync(m => m.SchoolId == schoolId && m.ClassId == classId && m.SubjectId == subjectId && m.StatusId == activeStatusId, ct);

        public async Task<HashSet<long>> ClassTeacherSectionIdsAsync(string schoolId, string employeeId, long academicYearId, int activeStatusId, CancellationToken ct) =>
            (await _db.ClassTeacherAssignments.AsNoTracking()
                .Where(a => a.SchoolId == schoolId && a.AcademicYearId == academicYearId && a.EmployeeId == employeeId && a.StatusId == activeStatusId)
                .Select(a => a.SectionId).ToListAsync(ct)).ToHashSet();

        public async Task<HashSet<(long SectionId, long SubjectId)>> TeachingPairsAsync(string schoolId, string employeeId, long academicYearId, int activeStatusId, CancellationToken ct) =>
            (await _db.TimetableEntries.AsNoTracking()
                .Where(e => e.SchoolId == schoolId && e.EmployeeId == employeeId && e.AcademicYearId == academicYearId && e.StatusId == activeStatusId)
                .Select(e => new { e.SectionId, e.SubjectId }).Distinct().ToListAsync(ct)).Select(e => (e.SectionId, e.SubjectId)).ToHashSet();

        // ------------------------------------------------------------------ roster and entries
        public async Task<List<RosterStudent>> GetRosterAsync(string schoolId, long academicYearId, long sectionId, int activeStatusId, CancellationToken ct)
        {
            var rows = await (from e in _db.StudentEnrollments.AsNoTracking()
                              join s in _db.Students.AsNoTracking() on e.StudentId equals s.StudentId
                              where e.SchoolId == schoolId && s.SchoolId == schoolId && e.AcademicYearId == academicYearId && e.ClassSectionId == sectionId
                                    && e.StatusId == activeStatusId && s.StatusId == activeStatusId
                              select new { s.StudentId, s.AdmissionNumber, s.FirstName, s.MiddleName, s.LastName, e.RollNumber }).ToListAsync(ct);
            return rows.Select(r => new RosterStudent(r.StudentId, r.AdmissionNumber, r.FirstName, r.MiddleName, r.LastName, r.RollNumber)).ToList();
        }

        public async Task<List<RosterStudent>> GetStudentsAsync(string schoolId, IReadOnlyCollection<string> studentIds, CancellationToken ct)
        {
            if (studentIds.Count == 0) return new();
            var ids = studentIds.ToList();
            var rows = await _db.Students.AsNoTracking().Where(s => s.SchoolId == schoolId && ids.Contains(s.StudentId))
                .Select(s => new { s.StudentId, s.AdmissionNumber, s.FirstName, s.MiddleName, s.LastName }).ToListAsync(ct);
            return rows.Select(r => new RosterStudent(r.StudentId, r.AdmissionNumber, r.FirstName, r.MiddleName, r.LastName, null)).ToList();
        }

        public async Task<List<TbReportMark>> GetMarksAsync(string schoolId, long examSubjectId, long sectionId, IReadOnlyCollection<string> rosterIds, bool track, CancellationToken ct)
        {
            var ids = rosterIds.ToList();
            var q = _db.ReportMarks.Where(m => m.SchoolId == schoolId && m.ExamSubjectId == examSubjectId && (m.SectionId == sectionId || ids.Contains(m.StudentId)));
            return await (track ? q : q.AsNoTracking()).ToListAsync(ct);
        }

        public Task<TbReportEntryStatus?> GetStatusAsync(long examSubjectId, long sectionId, bool forUpdate, CancellationToken ct) =>
            forUpdate
                ? _db.ReportEntryStatuses.FromSqlInterpolated($"SELECT * FROM public.tb_report_entry_status WHERE exam_subject_id = {examSubjectId} AND section_id = {sectionId} FOR UPDATE").AsTracking().FirstOrDefaultAsync(ct)
                : _db.ReportEntryStatuses.AsNoTracking().Where(s => s.ExamSubjectId == examSubjectId && s.SectionId == sectionId).FirstOrDefaultAsync(ct);

        public Task<TbReportTermSection?> GetTermSectionAsync(long termId, long sectionId, bool track, CancellationToken ct)
        {
            var q = _db.ReportTermSections.Where(t => t.TermId == termId && t.SectionId == sectionId);
            return (track ? q : q.AsNoTracking()).FirstOrDefaultAsync(ct);
        }

        public async Task LockTermSectionKeyAsync(long termId, long sectionId, CancellationToken ct)
        {
            var key = $"report-term-section:{termId}:{sectionId}";
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }

        public async Task<List<StatusSummary>> GetStatusesForSectionAsync(string schoolId, long sectionId, IReadOnlyCollection<long> examSubjectIds, CancellationToken ct)
        {
            if (examSubjectIds.Count == 0) return new();
            var ids = examSubjectIds.ToList();
            return (await _db.ReportEntryStatuses.AsNoTracking().Where(s => s.SchoolId == schoolId && s.SectionId == sectionId && ids.Contains(s.ExamSubjectId))
                    .Select(s => new { s.ExamSubjectId, s.SectionId, s.Status, s.Version }).ToListAsync(ct))
                .Select(s => new StatusSummary(s.ExamSubjectId, s.SectionId, s.Status, s.Version)).ToList();
        }

        public async Task<List<TbReportEntryStatus>> GetStatusesForUpdateAsync(string schoolId, long sectionId, IReadOnlyCollection<long> examSubjectIds, CancellationToken ct)
        {
            if (examSubjectIds.Count == 0) return new();
            var ids = examSubjectIds.ToArray();
            return await _db.ReportEntryStatuses
                .FromSqlInterpolated($"SELECT * FROM public.tb_report_entry_status WHERE school_id = {schoolId} AND section_id = {sectionId} AND exam_subject_id = ANY({ids}) ORDER BY exam_subject_id FOR UPDATE")
                .AsTracking().ToListAsync(ct);
        }

        public async Task<List<EntryCountRow>> GetEntryCountsAsync(string schoolId, long sectionId, IReadOnlyCollection<string> studentIds, IReadOnlyCollection<long> examSubjectIds, CancellationToken ct)
        {
            if (examSubjectIds.Count == 0) return new();
            var es = examSubjectIds.ToList(); var st = studentIds.ToList();
            return (await _db.ReportMarks.AsNoTracking().Where(m => m.SchoolId == schoolId && es.Contains(m.ExamSubjectId) && st.Contains(m.StudentId))
                    .GroupBy(m => new { m.ExamSubjectId, m.EntryState }).Select(g => new { g.Key.ExamSubjectId, g.Key.EntryState, N = g.Count() }).ToListAsync(ct))
                .Select(g => new EntryCountRow(g.ExamSubjectId, g.EntryState, g.N)).ToList();
        }

        public async Task<List<(long ExamSubjectId, string StudentId, int Count)>> GetStudentEntryCountsAsync(string schoolId, IReadOnlyCollection<string> studentIds, IReadOnlyCollection<long> examSubjectIds, CancellationToken ct)
        {
            if (examSubjectIds.Count == 0 || studentIds.Count == 0) return new();
            var es = examSubjectIds.ToList(); var st = studentIds.ToList();
            return (await _db.ReportMarks.AsNoTracking().Where(m => m.SchoolId == schoolId && es.Contains(m.ExamSubjectId) && st.Contains(m.StudentId))
                    .GroupBy(m => new { m.ExamSubjectId, m.StudentId }).Select(g => new { g.Key.ExamSubjectId, g.Key.StudentId, N = g.Count() }).ToListAsync(ct))
                .Select(g => (g.ExamSubjectId, g.StudentId, g.N)).ToList();
        }

        public async Task<List<(long ExamSubjectId, long SchemeSubjectId)>> TermExamSubjectsAsync(long termId, long classId, CancellationToken ct) =>
            (await (from es in _db.ReportExamSubjects.AsNoTracking()
                    join ex in _db.ReportExams.AsNoTracking() on es.ExamId equals ex.ExamId
                    where ex.TermId == termId && es.ClassId == classId
                    select new { es.ExamSubjectId, es.SchemeSubjectId }).ToListAsync(ct)).Select(x => (x.ExamSubjectId, x.SchemeSubjectId)).ToList();

        // ------------------------------------------------------------------ the "what may I enter" list
        public async Task<List<ContextRow>> GetContextRowsAsync(string schoolId, long academicYearId, int activeStatusId, CancellationToken ct) =>
            (await (from es in _db.ReportExamSubjects.AsNoTracking()
                    join ex in _db.ReportExams.AsNoTracking() on es.ExamId equals ex.ExamId
                    join t in _db.ReportTerms.AsNoTracking() on ex.TermId equals t.TermId
                    join sch in _db.ReportSchemes.AsNoTracking() on es.SchemeId equals sch.SchemeId
                    join ss in _db.ReportSchemeSubjects.AsNoTracking() on es.SchemeSubjectId equals ss.SchemeSubjectId
                    join sub in _db.Subjects.AsNoTracking() on ss.SubjectId equals sub.SubjectId
                    join c in _db.Classes.AsNoTracking() on es.ClassId equals c.ClassId
                    join sec in _db.Sections.AsNoTracking() on es.ClassId equals sec.ClassId
                    where sch.SchoolId == schoolId && sch.AcademicYearId == academicYearId && sch.StatusId == activeStatusId && sec.StatusId == activeStatusId && sec.SchoolId == schoolId
                    select new
                    {
                        sch.SchemeId, sch.SchemeName, sch.EvaluationMode, t.TermId, t.TermName, TermSeq = t.Sequence, ex.ExamId, ex.ExamName, ExamSeq = ex.Sequence,
                        es.ExamSubjectId, ss.SchemeSubjectId, sub.SubjectId, SubjectName = ss.LabelOverride ?? sub.SubjectName, ss.DisplayOrder, c.ClassId, c.ClassName, ClassOrder = c.SequenceOrder,
                        sec.SectionId, sec.SectionName, es.EntryMode, Max = es.EntryMode == "MARKS" ? (es.MaxMarks ?? ex.DefaultMaxMarks) : (decimal?)null
                    }).ToListAsync(ct))
            .Select(r => new ContextRow(r.SchemeId, r.SchemeName, r.EvaluationMode, r.TermId, r.TermName, r.TermSeq, r.ExamId, r.ExamName, r.ExamSeq, r.ExamSubjectId, r.SchemeSubjectId, r.SubjectId,
                r.SubjectName, r.DisplayOrder, r.ClassId, r.ClassName, r.ClassOrder, r.SectionId, r.SectionName, r.EntryMode, r.Max)).ToList();

        public async Task<List<StatusSummary>> GetStatusesForYearAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            (await (from s in _db.ReportEntryStatuses.AsNoTracking()
                    join es in _db.ReportExamSubjects.AsNoTracking() on s.ExamSubjectId equals es.ExamSubjectId
                    join sch in _db.ReportSchemes.AsNoTracking() on es.SchemeId equals sch.SchemeId
                    where s.SchoolId == schoolId && sch.AcademicYearId == academicYearId
                    select new { s.ExamSubjectId, s.SectionId, s.Status, s.Version }).ToListAsync(ct))
            .Select(s => new StatusSummary(s.ExamSubjectId, s.SectionId, s.Status, s.Version)).ToList();

        public async Task<List<TermSectionSummary>> GetTermSectionsForYearAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            (await (from ts in _db.ReportTermSections.AsNoTracking()
                    join sch in _db.ReportSchemes.AsNoTracking() on ts.SchemeId equals sch.SchemeId
                    where ts.SchoolId == schoolId && sch.AcademicYearId == academicYearId
                    select new { ts.TermId, ts.SectionId, ts.Status, ts.LockedAt, ts.UnlockReason }).ToListAsync(ct))
            .Select(t => new TermSectionSummary(t.TermId, t.SectionId, t.Status, t.LockedAt, t.UnlockReason)).ToList();

        public async Task<HashSet<MappedPair>> GetMappedPairsAsync(string schoolId, int activeStatusId, CancellationToken ct) =>
            (await _db.ClassSubjects.AsNoTracking().Where(m => m.SchoolId == schoolId && m.StatusId == activeStatusId).Select(m => new { m.ClassId, m.SubjectId }).ToListAsync(ct))
            .Select(m => new MappedPair(m.ClassId, m.SubjectId)).ToHashSet();

        public async Task<Dictionary<long, int>> GetSkillCountsAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            await (from k in _db.ReportSubjectSkills.AsNoTracking()
                   join sch in _db.ReportSchemes.AsNoTracking() on k.SchemeId equals sch.SchemeId
                   where k.SchoolId == schoolId && sch.AcademicYearId == academicYearId
                   group k by k.SchemeSubjectId into g
                   select new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);

        // ------------------------------------------------------------------ class review
        public Task<SchemeHeader?> GetSchemeAsync(string schoolId, long schemeId, CancellationToken ct) =>
            _db.ReportSchemes.AsNoTracking().Where(s => s.SchemeId == schemeId && s.SchoolId == schoolId)
                .Select(s => new SchemeHeader(s.SchemeId, s.SchemeName, s.AcademicYearId, s.StatusId, s.EvaluationMode, s.GradeScaleId, s.AttendanceSource)).FirstOrDefaultAsync(ct);

        public Task<TermHeader?> GetTermAsync(string schoolId, long termId, CancellationToken ct) =>
            (from t in _db.ReportTerms.AsNoTracking()
             join s in _db.ReportSchemes.AsNoTracking() on t.SchemeId equals s.SchemeId
             where t.TermId == termId && s.SchoolId == schoolId
             select new TermHeader(t.TermId, t.TermName, t.Sequence, t.StartDate, t.EndDate, s.SchemeId, s.SchemeName, s.AcademicYearId, s.StatusId, s.EvaluationMode, s.AttendanceSource, s.GradeScaleId))
            .FirstOrDefaultAsync(ct);

        public async Task<List<StructureRow>> GetStructureAsync(string schoolId, long schemeId, long classId, CancellationToken ct) =>
            (await (from es in _db.ReportExamSubjects.AsNoTracking()
                    join ex in _db.ReportExams.AsNoTracking() on es.ExamId equals ex.ExamId
                    join t in _db.ReportTerms.AsNoTracking() on ex.TermId equals t.TermId
                    join ss in _db.ReportSchemeSubjects.AsNoTracking() on es.SchemeSubjectId equals ss.SchemeSubjectId
                    join sub in _db.Subjects.AsNoTracking() on ss.SubjectId equals sub.SubjectId
                    where es.SchemeId == schemeId && es.SchoolId == schoolId && es.ClassId == classId
                    select new
                    {
                        t.TermId, t.TermName, TermSeq = t.Sequence, t.StartDate, t.EndDate, ex.ExamId, ex.ExamName, ExamSeq = ex.Sequence, es.ExamSubjectId, ss.SchemeSubjectId,
                        sub.SubjectName, ss.LabelOverride, ss.DisplayOrder, es.EntryMode, Max = es.EntryMode == "MARKS" ? (es.MaxMarks ?? ex.DefaultMaxMarks) : (decimal?)null
                    }).ToListAsync(ct))
            .Select(r => new StructureRow(r.TermId, r.TermName, r.TermSeq, r.StartDate, r.EndDate, r.ExamId, r.ExamName, r.ExamSeq, r.ExamSubjectId, r.SchemeSubjectId, r.SubjectName, r.LabelOverride, r.DisplayOrder, r.EntryMode, r.Max)).ToList();

        public Task<bool> ClassInSchemeAsync(long schemeId, long classId, CancellationToken ct) =>
            _db.ReportSchemeClasses.AsNoTracking().AnyAsync(c => c.SchemeId == schemeId && c.ClassId == classId, ct);

        public Task<long?> LastTermIdAsync(long schemeId, CancellationToken ct) =>
            _db.ReportTerms.AsNoTracking().Where(t => t.SchemeId == schemeId).OrderByDescending(t => t.Sequence).Select(t => (long?)t.TermId).FirstOrDefaultAsync(ct);

        public async Task<List<TermSectionSummary>> GetTermSectionsAsync(long schemeId, long sectionId, CancellationToken ct) =>
            (await _db.ReportTermSections.AsNoTracking().Where(t => t.SchemeId == schemeId && t.SectionId == sectionId)
                .Select(t => new { t.TermId, t.SectionId, t.Status, t.LockedAt, t.UnlockReason }).ToListAsync(ct))
            .Select(t => new TermSectionSummary(t.TermId, t.SectionId, t.Status, t.LockedAt, t.UnlockReason)).ToList();

        public async Task<List<AreaInfo>> GetActivityAreasAsync(string schoolId, int activeStatusId, CancellationToken ct) =>
            (await _db.ReportActivityAreas.AsNoTracking().Where(a => a.SchoolId == schoolId && a.StatusId == activeStatusId).OrderBy(a => a.Category).ThenBy(a => a.Sequence).ThenBy(a => a.AreaName)
                .Select(a => new { a.AreaId, a.Category, a.AreaName, a.Sequence, a.RatingScaleId }).ToListAsync(ct))
            .Select(a => new AreaInfo(a.AreaId, a.Category, a.AreaName, a.Sequence, a.RatingScaleId)).ToList();

        public Task<List<TbReportStudentActivityGrade>> GetActivityGradesAsync(long termId, long sectionId, bool track, CancellationToken ct)
        {
            var q = _db.ReportActivityGrades.Where(g => g.TermId == termId && g.SectionId == sectionId);
            return (track ? q : q.AsNoTracking()).ToListAsync(ct);
        }

        public Task<List<TbReportTermStudent>> GetTermStudentsAsync(long termId, long sectionId, bool track, CancellationToken ct)
        {
            var q = _db.ReportTermStudents.Where(g => g.TermId == termId && g.SectionId == sectionId);
            return (track ? q : q.AsNoTracking()).ToListAsync(ct);
        }

        public Task<List<TbReportYearStudent>> GetYearStudentsAsync(long schemeId, long sectionId, bool track, CancellationToken ct)
        {
            var q = _db.ReportYearStudents.Where(g => g.SchemeId == schemeId && g.SectionId == sectionId);
            return (track ? q : q.AsNoTracking()).ToListAsync(ct);
        }

        // ------------------------------------------------------------------ the footprint
        public async Task<EntryFootprint> GetFootprintAsync(string schoolId, long schemeId, CancellationToken ct)
        {
            var fp = new EntryFootprint();
            // exam subjects that have entries: any mark, or a grid that has been submitted / accepted
            var grids = await (from es in _db.ReportExamSubjects.AsNoTracking()
                               join ex in _db.ReportExams.AsNoTracking() on es.ExamId equals ex.ExamId
                               where es.SchemeId == schemeId && es.SchoolId == schoolId
                                     && (_db.ReportMarks.Any(m => m.ExamSubjectId == es.ExamSubjectId)
                                         || _db.ReportEntryStatuses.Any(s => s.ExamSubjectId == es.ExamSubjectId && s.Status != "DRAFT"))
                               select new { es.ExamSubjectId, es.SchemeSubjectId, es.ClassId, ex.ExamId, ex.TermId }).ToListAsync(ct);
            foreach (var g in grids) { fp.ExamSubjectIds.Add(g.ExamSubjectId); fp.SchemeSubjectIds.Add(g.SchemeSubjectId); fp.ClassIds.Add(g.ClassId); fp.ExamIds.Add(g.ExamId); fp.TermIds.Add(g.TermId); }
            // terms that carry class-teacher inputs or a lock row
            var termRows = await _db.ReportTermSections.AsNoTracking().Where(t => t.SchemeId == schemeId && t.SchoolId == schoolId).Select(t => new { t.TermId, t.ClassId, t.Status }).ToListAsync(ct);
            foreach (var t in termRows) { fp.TermIds.Add(t.TermId); fp.ClassIds.Add(t.ClassId); if (t.Status == "LOCKED") fp.LockedTermIds.Add(t.TermId); }
            var inputs = await _db.ReportTermStudents.AsNoTracking().Where(t => t.SchemeId == schemeId && t.SchoolId == schoolId).Select(t => t.TermId).Distinct().ToListAsync(ct);
            foreach (var t in inputs) fp.TermIds.Add(t);
            var grades = await _db.ReportActivityGrades.AsNoTracking().Where(t => t.SchemeId == schemeId && t.SchoolId == schoolId).Select(t => t.TermId).Distinct().ToListAsync(ct);
            foreach (var t in grades) fp.TermIds.Add(t);
            return fp;
        }

        // ------------------------------------------------------------------ writes
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
        public Task<List<TbReportSubjectRemark>> GetSubjectRemarksAsync(long termId, long schemeSubjectId, IReadOnlyCollection<string> studentIds, bool track, CancellationToken ct)
        {
            var ids = studentIds.ToList();
            var q = _db.ReportSubjectRemarks.Where(r => r.TermId == termId && r.SchemeSubjectId == schemeSubjectId && ids.Contains(r.StudentId));
            return (track ? q : q.AsNoTracking()).ToListAsync(ct);
        }

        public void Add(object entity) => _db.Add(entity);
        public void AddRange(IEnumerable<object> entities) => _db.AddRange(entities);
        public void Remove(object entity) => _db.Remove(entity);
        public void RemoveRange(IEnumerable<object> entities) => _db.RemoveRange(entities);
        public void ClearTracking() => _db.ChangeTracker.Clear();
    }
}
