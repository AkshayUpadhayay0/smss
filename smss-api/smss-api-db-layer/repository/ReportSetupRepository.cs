using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class ReportSetupRepository : IReportSetupRepository
    {
        private readonly dbContext _db;
        public ReportSetupRepository(dbContext db) => _db = db;

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == statusName && s.Stype == statusType).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
        public void Add(object entity) => _db.Add(entity);
        public void AddRange(IEnumerable<object> entities) => _db.AddRange(entities);
        public void Remove(object entity) => _db.Remove(entity);
        public void RemoveRange(IEnumerable<object> entities) => _db.RemoveRange(entities);

        private static IQueryable<T> Maybe<T>(IQueryable<T> q, bool track) where T : class => track ? q : q.AsNoTracking();

        // ------------------------------------------------------------------ grade scales
        public async Task<List<ScaleListRow>> ListScalesAsync(string schoolId, bool includeInactive, int activeStatusId, CancellationToken ct)
        {
            var rows = await (
                from s in _db.GradeScales.AsNoTracking()
                join st in _db.LutStatus.AsNoTracking() on s.StatusId equals st.Sid
                where s.SchoolId == schoolId && (includeInactive || s.StatusId == activeStatusId)
                select new
                {
                    Scale = s, st.Sname,
                    Bands = _db.GradeScaleBands.Count(b => b.GradeScaleId == s.GradeScaleId),
                    Schemes = _db.ReportSchemes.Count(x => x.SchoolId == schoolId && x.GradeScaleId == s.GradeScaleId && x.StatusId == activeStatusId),
                    Areas = _db.ReportActivityAreas.Count(a => a.SchoolId == schoolId && a.RatingScaleId == s.GradeScaleId && a.StatusId == activeStatusId)
                }).ToListAsync(ct);
            return rows.OrderBy(r => r.Scale.ScaleName, StringComparer.OrdinalIgnoreCase).Select(r => new ScaleListRow(r.Scale, r.Sname, r.Bands, r.Schemes, r.Areas)).ToList();
        }

        public Task<TbGradeScale?> GetScaleAsync(string schoolId, long scaleId, bool track, CancellationToken ct) =>
            Maybe(_db.GradeScales.Where(s => s.SchoolId == schoolId && s.GradeScaleId == scaleId), track).FirstOrDefaultAsync(ct);

        public Task<List<TbGradeScaleBand>> GetBandsAsync(string schoolId, long scaleId, bool track, CancellationToken ct) =>
            Maybe(_db.GradeScaleBands.Where(b => b.SchoolId == schoolId && b.GradeScaleId == scaleId), track).OrderBy(b => b.Sequence).ToListAsync(ct);

        public Task<bool> ScaleNameTakenAsync(string schoolId, string name, long? exceptId, int activeStatusId, CancellationToken ct)
        {
            var lower = name.ToLower();
            return _db.GradeScales.AnyAsync(s => s.SchoolId == schoolId && s.StatusId == activeStatusId && s.ScaleName.ToLower() == lower && (exceptId == null || s.GradeScaleId != exceptId), ct);
        }

        public Task<TbGradeScale?> FindScaleByPresetAsync(string schoolId, string presetCode, int activeStatusId, CancellationToken ct) =>
            _db.GradeScales.AsNoTracking().Where(s => s.SchoolId == schoolId && s.StatusId == activeStatusId && s.SourcePresetCode == presetCode).OrderBy(s => s.GradeScaleId).FirstOrDefaultAsync(ct);

        public async Task<(int Schemes, int Areas)> ScaleUsageAsync(string schoolId, long scaleId, int activeStatusId, CancellationToken ct) =>
            (await _db.ReportSchemes.CountAsync(x => x.SchoolId == schoolId && x.GradeScaleId == scaleId && x.StatusId == activeStatusId, ct),
             await _db.ReportActivityAreas.CountAsync(a => a.SchoolId == schoolId && a.RatingScaleId == scaleId && a.StatusId == activeStatusId, ct));

        // ------------------------------------------------------------------ lookups
        public Task<TbAcademicYears?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.AcademicYearId == academicYearId).FirstOrDefaultAsync(ct);

        public async Task<Dictionary<long, string>> ClassNamesAsync(string schoolId, IReadOnlyCollection<long> classIds, int activeStatusId, CancellationToken ct)
        {
            var ids = classIds.ToArray();
            return await _db.Classes.AsNoTracking().Where(c => c.SchoolId == schoolId && ids.Contains(c.ClassId) && c.StatusId == activeStatusId).ToDictionaryAsync(c => c.ClassId, c => c.ClassName, ct);
        }

        public async Task<List<MappedSubject>> MappedSubjectsAsync(string schoolId, IReadOnlyCollection<long> classIds, int activeStatusId, CancellationToken ct)
        {
            var ids = classIds.ToArray();
            return await (
                from m in _db.ClassSubjects.AsNoTracking()
                join s in _db.Subjects.AsNoTracking() on m.SubjectId equals s.SubjectId
                where m.SchoolId == schoolId && s.SchoolId == schoolId && ids.Contains(m.ClassId) && m.StatusId == activeStatusId && s.StatusId == activeStatusId
                orderby s.SubjectName
                select new MappedSubject(m.ClassId, s.SubjectId, s.SubjectName)).ToListAsync(ct);
        }

        public async Task<Dictionary<long, string>> SubjectNamesAsync(string schoolId, IReadOnlyCollection<long> subjectIds, CancellationToken ct)
        {
            var ids = subjectIds.ToArray();
            return await _db.Subjects.AsNoTracking().Where(s => s.SchoolId == schoolId && ids.Contains(s.SubjectId)).ToDictionaryAsync(s => s.SubjectId, s => s.SubjectName, ct);
        }

        public async Task<Dictionary<long, string>> ScaleNamesAsync(string schoolId, IReadOnlyCollection<long> scaleIds, CancellationToken ct)
        {
            var ids = scaleIds.ToArray();
            return await _db.GradeScales.AsNoTracking().Where(s => s.SchoolId == schoolId && ids.Contains(s.GradeScaleId)).ToDictionaryAsync(s => s.GradeScaleId, s => s.ScaleName, ct);
        }

        // ------------------------------------------------------------------ schemes
        public async Task<List<SchemeListRow>> ListSchemesAsync(string schoolId, long? academicYearId, bool includeInactive, int activeStatusId, CancellationToken ct)
        {
            var rows = await (
                from s in _db.ReportSchemes.AsNoTracking()
                join y in _db.AcademicYears.AsNoTracking() on s.AcademicYearId equals y.AcademicYearId
                join st in _db.LutStatus.AsNoTracking() on s.StatusId equals st.Sid
                where s.SchoolId == schoolId && (academicYearId == null || s.AcademicYearId == academicYearId) && (includeInactive || s.StatusId == activeStatusId)
                select new
                {
                    Scheme = s, y.YearName, st.Sname, y.StartDate,
                    Terms = _db.ReportTerms.Count(t => t.SchemeId == s.SchemeId),
                    Exams = _db.ReportExams.Count(e => e.SchemeId == s.SchemeId),
                    Subjects = _db.ReportSchemeSubjects.Count(x => x.SchemeId == s.SchemeId)
                }).ToListAsync(ct);
            return rows.OrderByDescending(r => r.StartDate).ThenBy(r => r.Scheme.SchemeName, StringComparer.OrdinalIgnoreCase)
                .Select(r => new SchemeListRow(r.Scheme, r.YearName, r.Sname, r.Terms, r.Exams, r.Subjects)).ToList();
        }

        public Task<TbReportScheme?> GetSchemeAsync(string schoolId, long schemeId, bool track, CancellationToken ct) =>
            Maybe(_db.ReportSchemes.Where(s => s.SchoolId == schoolId && s.SchemeId == schemeId), track).FirstOrDefaultAsync(ct);

        public async Task<SchemeGraph?> LoadGraphAsync(string schoolId, long schemeId, bool track, CancellationToken ct)
        {
            var scheme = await GetSchemeAsync(schoolId, schemeId, track, ct);
            if (scheme == null) return null;
            return new SchemeGraph
            {
                Scheme = scheme,
                Classes = await Maybe(_db.ReportSchemeClasses.Where(x => x.SchemeId == schemeId && x.SchoolId == schoolId), track).ToListAsync(ct),
                Terms = await Maybe(_db.ReportTerms.Where(x => x.SchemeId == schemeId && x.SchoolId == schoolId), track).OrderBy(x => x.Sequence).ToListAsync(ct),
                Exams = await Maybe(_db.ReportExams.Where(x => x.SchemeId == schemeId && x.SchoolId == schoolId), track).OrderBy(x => x.TermId).ThenBy(x => x.Sequence).ToListAsync(ct),
                ClassDates = await Maybe(_db.ReportExamClassDates.Where(x => x.SchemeId == schemeId && x.SchoolId == schoolId), track).ToListAsync(ct),
                Subjects = await Maybe(_db.ReportSchemeSubjects.Where(x => x.SchemeId == schemeId && x.SchoolId == schoolId), track).OrderBy(x => x.ClassId).ThenBy(x => x.DisplayOrder).ThenBy(x => x.SchemeSubjectId).ToListAsync(ct),
                Cells = await Maybe(_db.ReportExamSubjects.Where(x => x.SchemeId == schemeId && x.SchoolId == schoolId), track).ToListAsync(ct),
                Skills = await Maybe(_db.ReportSubjectSkills.Where(x => x.SchemeId == schemeId && x.SchoolId == schoolId), track).OrderBy(x => x.SchemeSubjectId).ThenBy(x => x.Sequence).ToListAsync(ct),
            };
        }

        public Task<bool> SchemeNameTakenAsync(string schoolId, long academicYearId, string name, long? exceptId, int activeStatusId, CancellationToken ct)
        {
            var lower = name.ToLower();
            return _db.ReportSchemes.AnyAsync(s => s.SchoolId == schoolId && s.AcademicYearId == academicYearId && s.StatusId == activeStatusId && s.SchemeName.ToLower() == lower && (exceptId == null || s.SchemeId != exceptId), ct);
        }

        public async Task<List<ClassConflict>> ClassConflictsAsync(string schoolId, long academicYearId, IReadOnlyCollection<long> classIds, long? exceptSchemeId, int activeStatusId, CancellationToken ct)
        {
            var ids = classIds.ToArray();
            return await (
                from c in _db.ReportSchemeClasses.AsNoTracking()
                join s in _db.ReportSchemes.AsNoTracking() on c.SchemeId equals s.SchemeId
                where c.SchoolId == schoolId && c.AcademicYearId == academicYearId && ids.Contains(c.ClassId) && s.StatusId == activeStatusId && (exceptSchemeId == null || c.SchemeId != exceptSchemeId)
                select new ClassConflict(c.ClassId, s.SchemeId, s.SchemeName)).ToListAsync(ct);
        }

        // ------------------------------------------------------------------ activity areas
        public async Task<List<AreaListRow>> ListAreasAsync(string schoolId, bool includeInactive, int activeStatusId, CancellationToken ct)
        {
            var rows = await (
                from a in _db.ReportActivityAreas.AsNoTracking()
                join sc in _db.GradeScales.AsNoTracking() on a.RatingScaleId equals sc.GradeScaleId
                join st in _db.LutStatus.AsNoTracking() on a.StatusId equals st.Sid
                where a.SchoolId == schoolId && (includeInactive || a.StatusId == activeStatusId)
                select new AreaListRow(a, sc.ScaleName, st.Sname)).ToListAsync(ct);
            return rows.OrderBy(r => r.Area.Category).ThenBy(r => r.Area.Sequence).ThenBy(r => r.Area.AreaName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public Task<TbReportActivityArea?> GetAreaAsync(string schoolId, long areaId, bool track, CancellationToken ct) =>
            Maybe(_db.ReportActivityAreas.Where(a => a.SchoolId == schoolId && a.AreaId == areaId), track).FirstOrDefaultAsync(ct);

        public Task<bool> AreaNameTakenAsync(string schoolId, string category, string name, long? exceptId, int activeStatusId, CancellationToken ct)
        {
            var lower = name.ToLower();
            return _db.ReportActivityAreas.AnyAsync(a => a.SchoolId == schoolId && a.StatusId == activeStatusId && a.Category == category && a.AreaName.ToLower() == lower && (exceptId == null || a.AreaId != exceptId), ct);
        }
    }
}
