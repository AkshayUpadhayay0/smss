using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class ClassSubjectRepository : IClassSubjectRepository
    {
        private readonly dbContext _db;
        public ClassSubjectRepository(dbContext db) => _db = db;

        private sealed class JoinedRow
        {
            public TbClassSubjects Mapping { get; set; } = null!;
            public string ClassName { get; set; } = null!;
            public string SubjectName { get; set; } = null!;
            public string? SubjectCode { get; set; }
        }

        // Joined on the ids AND the school, so a mapping can never surface another school's class or subject name
        private IQueryable<JoinedRow> Joined(string schoolId) =>
            from m in _db.ClassSubjects.AsNoTracking()
            join c in _db.Classes.AsNoTracking() on m.ClassId equals c.ClassId
            join s in _db.Subjects.AsNoTracking() on m.SubjectId equals s.SubjectId
            where m.SchoolId == schoolId && c.SchoolId == schoolId && s.SchoolId == schoolId
            select new JoinedRow { Mapping = m, ClassName = c.ClassName, SubjectName = s.SubjectName, SubjectCode = s.SubjectCode };

        private static ClassSubjectWithNames ToModel(JoinedRow r) => new(r.Mapping, r.ClassName, r.SubjectName, r.SubjectCode);

        public async Task<List<ClassSubjectWithNames>> GetByClassAsync(string schoolId, long classId, long academicYearId)
        {
            var rows = await Joined(schoolId)
                .Where(x => x.Mapping.ClassId == classId && x.Mapping.AcademicYearId == academicYearId)
                .OrderBy(x => x.SubjectName)
                .ToListAsync();
            return rows.Select(ToModel).ToList();
        }

        public async Task<ClassSubjectWithNames?> GetWithNamesByIdAsync(string schoolId, long id)
        {
            var row = await Joined(schoolId).Where(x => x.Mapping.ClassSubjectId == id).FirstOrDefaultAsync();
            return row == null ? null : ToModel(row);
        }

        public Task<TbClassSubjects?> GetByIdAsync(string schoolId, long id) =>
            _db.ClassSubjects.Where(m => m.SchoolId == schoolId && m.ClassSubjectId == id).FirstOrDefaultAsync();

        public Task<List<TbClassSubjects>> GetTrackedByClassAsync(string schoolId, long classId, long academicYearId) =>
            _db.ClassSubjects.Where(m => m.SchoolId == schoolId && m.ClassId == classId && m.AcademicYearId == academicYearId).ToListAsync();

        public Task<TbClasses?> GetClassAsync(string schoolId, long classId) =>
            _db.Classes.AsNoTracking().Where(c => c.SchoolId == schoolId && c.ClassId == classId).FirstOrDefaultAsync();

        public Task<int> CountSubjectsAsync(string schoolId, IReadOnlyCollection<long> subjectIds) =>
            _db.Subjects.CountAsync(s => s.SchoolId == schoolId && subjectIds.Contains(s.SubjectId));

        public Task<bool> ExistsAsync(long academicYearId, long classId, long subjectId) =>
            _db.ClassSubjects.AnyAsync(m => m.AcademicYearId == academicYearId && m.ClassId == classId && m.SubjectId == subjectId);

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType) =>
            _db.LutStatus.AsNoTracking()
                .Where(s => s.Sname == statusName && s.Stype == statusType)
                .Select(s => (int?)s.Sid)
                .FirstOrDefaultAsync();

        public async Task AddAsync(TbClassSubjects entity) => await _db.ClassSubjects.AddAsync(entity);
        public void Remove(TbClassSubjects entity) => _db.ClassSubjects.Remove(entity);
        public void RemoveRange(IEnumerable<TbClassSubjects> entities) => _db.ClassSubjects.RemoveRange(entities);

        public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

        // ---------------- S3
        public Task<TbAcademicYears?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.AcademicYearId == academicYearId).FirstOrDefaultAsync(ct);

        public Task<TbAcademicYears?> GetCurrentYearAsync(string schoolId, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.IsCurrent).FirstOrDefaultAsync(ct);

        public Task<bool> IsClassOfferedAsync(string schoolId, long academicYearId, long classId, int activeStatusId, CancellationToken ct) =>
            _db.AcademicYearClasses.AsNoTracking().AnyAsync(o => o.SchoolId == schoolId && o.AcademicYearId == academicYearId && o.ClassId == classId && o.StatusId == activeStatusId, ct);

        public async Task<List<SubjectSectionBrief>> OfferedSectionsAsync(string schoolId, long academicYearId, long classId, int activeStatusId, CancellationToken ct) =>
            await (from o in _db.AcademicYearSections.AsNoTracking()
                   join s in _db.Sections.AsNoTracking() on o.SectionId equals s.SectionId
                   where o.SchoolId == schoolId && s.SchoolId == schoolId && o.AcademicYearId == academicYearId && o.ClassId == classId && o.StatusId == activeStatusId
                   orderby s.SectionName
                   select new SubjectSectionBrief(s.SectionId, s.SectionName)).ToListAsync(ct);

        public Task<List<TbSubjects>> AllSubjectsAsync(string schoolId, CancellationToken ct) =>
            _db.Subjects.AsNoTracking().Where(s => s.SchoolId == schoolId).OrderBy(s => s.SubjectName).ToListAsync(ct);

        public async Task<SubjectUsageSet> GetUsageAsync(string schoolId, long academicYearId, long classId, int activeStatusId, CancellationToken ct)
        {
            var tt = await _db.TimetableEntries.AsNoTracking().Where(e => e.SchoolId == schoolId && e.AcademicYearId == academicYearId && e.ClassId == classId && e.StatusId == activeStatusId)
                .GroupBy(e => new { e.SectionId, e.SubjectId }).Select(g => new { g.Key.SectionId, g.Key.SubjectId, N = g.Count() }).ToListAsync(ct);
            var hw = await _db.Homework.AsNoTracking().Where(e => e.SchoolId == schoolId && e.AcademicYearId == academicYearId && e.ClassId == classId && e.StatusId == activeStatusId)
                .GroupBy(e => new { e.SectionId, e.SubjectId }).Select(g => new { g.Key.SectionId, g.Key.SubjectId, N = g.Count() }).ToListAsync(ct);
            var ts = await _db.ClassAssessments.AsNoTracking().Where(e => e.SchoolId == schoolId && e.AcademicYearId == academicYearId && e.ClassId == classId && e.StatusId == activeStatusId)
                .GroupBy(e => new { e.SectionId, e.SubjectId }).Select(g => new { g.Key.SectionId, g.Key.SubjectId, N = g.Count() }).ToListAsync(ct);
            var mk = await (from m in _db.ReportMarks.AsNoTracking()
                            join ss in _db.ReportSchemeSubjects.AsNoTracking() on m.SchemeSubjectId equals ss.SchemeSubjectId
                            join sch in _db.ReportSchemes.AsNoTracking() on ss.SchemeId equals sch.SchemeId
                            where m.SchoolId == schoolId && sch.AcademicYearId == academicYearId && ss.ClassId == classId
                            group m by new { m.SectionId, ss.SubjectId } into g
                            select new { g.Key.SectionId, g.Key.SubjectId, N = g.Count() }).ToListAsync(ct);
            var exams = await (from ss in _db.ReportSchemeSubjects.AsNoTracking()
                               join sch in _db.ReportSchemes.AsNoTracking() on ss.SchemeId equals sch.SchemeId
                               where ss.SchoolId == schoolId && ss.ClassId == classId && sch.AcademicYearId == academicYearId && sch.StatusId == activeStatusId
                               group ss by ss.SubjectId into g
                               select new { SubjectId = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.SubjectId, x => x.N, ct);

            var keys = tt.Select(x => (x.SectionId, x.SubjectId)).Concat(hw.Select(x => (x.SectionId, x.SubjectId))).Concat(ts.Select(x => (x.SectionId, x.SubjectId))).Concat(mk.Select(x => (x.SectionId, x.SubjectId))).Distinct();
            int Of<T>(IEnumerable<T> src, Func<T, (long, long)> k, (long, long) key, Func<T, int> n) => src.Where(x => k(x) == key).Sum(n);
            var rows = keys.Select(key => new SubjectUseRow(key.SectionId, key.SubjectId,
                Of(tt, x => (x.SectionId, x.SubjectId), key, x => x.N), Of(hw, x => (x.SectionId, x.SubjectId), key, x => x.N),
                Of(ts, x => (x.SectionId, x.SubjectId), key, x => x.N), Of(mk, x => (x.SectionId, x.SubjectId), key, x => x.N))).ToList();
            return new SubjectUsageSet { PerSection = rows, ExamSetups = exams };
        }

        public Task<List<TbSectionSubjectOverride>> GetOverridesTrackedAsync(string schoolId, long academicYearId, long classId, CancellationToken ct) =>
            _db.SectionSubjectOverrides.Where(o => o.SchoolId == schoolId && o.AcademicYearId == academicYearId && o.ClassId == classId).ToListAsync(ct);

        public async Task AddOverrideAsync(TbSectionSubjectOverride entity, CancellationToken ct) => await _db.SectionSubjectOverrides.AddAsync(entity, ct);

        public async Task LockSchoolAsync(string schoolId, CancellationToken ct)
        {
            var key = "acad-setup:" + schoolId;
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }

        public Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);
    }
}
