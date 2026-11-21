using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class AcademicSetupRepository : IAcademicSetupRepository
    {
        private readonly dbContext _db;
        public AcademicSetupRepository(dbContext db) => _db = db;

        public Task<OfferingYearRow?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.AcademicYearId == academicYearId)
                .Select(y => new OfferingYearRow(y.AcademicYearId, y.YearName, y.IsCurrent, y.ClosedAt != null)).FirstOrDefaultAsync(ct);

        public Task<List<OfferingClassMaster>> GetClassMastersAsync(string schoolId, CancellationToken ct) =>
            _db.Classes.AsNoTracking().Where(c => c.SchoolId == schoolId).OrderBy(c => c.SequenceOrder).ThenBy(c => c.ClassName)
                .Select(c => new OfferingClassMaster(c.ClassId, c.ClassName, c.SequenceOrder, c.StatusId)).ToListAsync(ct);

        public Task<List<OfferingSectionMaster>> GetSectionMastersAsync(string schoolId, CancellationToken ct) =>
            _db.Sections.AsNoTracking().Where(s => s.SchoolId == schoolId).OrderBy(s => s.SectionName)
                .Select(s => new OfferingSectionMaster(s.SectionId, s.ClassId, s.SectionName, s.MaxStrength, s.StatusId)).ToListAsync(ct);

        public Task<List<TbAcademicYearClass>> GetClassOfferingsAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            _db.AcademicYearClasses.Where(o => o.SchoolId == schoolId && o.AcademicYearId == academicYearId).ToListAsync(ct);

        public Task<List<TbAcademicYearSection>> GetSectionOfferingsAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            _db.AcademicYearSections.Where(o => o.SchoolId == schoolId && o.AcademicYearId == academicYearId).ToListAsync(ct);

        public async Task<Dictionary<long, int>> EnrolmentsBySectionAsync(string schoolId, long academicYearId, int activeStatusId, CancellationToken ct) =>
            (await _db.StudentEnrollments.AsNoTracking().Where(e => e.SchoolId == schoolId && e.AcademicYearId == academicYearId && e.StatusId == activeStatusId)
                .GroupBy(e => e.ClassSectionId).Select(g => new { SectionId = g.Key, N = g.Count() }).ToListAsync(ct)).ToDictionary(x => x.SectionId, x => x.N);

        public async Task<HashSet<long>> FeeStructureClassIdsAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            (await _db.FeeStructures.AsNoTracking().Where(f => f.SchoolId == schoolId && f.AcademicYearId == academicYearId && f.IsActive).Select(f => f.ClassId).Distinct().ToListAsync(ct)).ToHashSet();

        public async Task<HashSet<long>> ClassTeacherSectionIdsAsync(string schoolId, long academicYearId, int activeStatusId, CancellationToken ct) =>
            (await _db.ClassTeacherAssignments.AsNoTracking().Where(a => a.SchoolId == schoolId && a.AcademicYearId == academicYearId && a.StatusId == activeStatusId).Select(a => a.SectionId).Distinct().ToListAsync(ct)).ToHashSet();

        public async Task<Dictionary<long, int>> TimetableBySectionAsync(string schoolId, long academicYearId, int activeStatusId, CancellationToken ct) =>
            (await _db.TimetableEntries.AsNoTracking().Where(t => t.SchoolId == schoolId && t.AcademicYearId == academicYearId && t.StatusId == activeStatusId)
                .GroupBy(t => t.SectionId).Select(g => new { SectionId = g.Key, N = g.Count() }).ToListAsync(ct)).ToDictionary(x => x.SectionId, x => x.N);

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == statusName && s.Stype == statusType).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public async Task LockSchoolAsync(string schoolId, CancellationToken ct)
        {
            var key = "acad-setup:" + schoolId;
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);
        public async Task AddAsync<T>(T entity, CancellationToken ct) where T : class => await _db.Set<T>().AddAsync(entity, ct);
        public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
    }
}
