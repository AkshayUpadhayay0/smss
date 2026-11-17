using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class FeeStructureRepository : IFeeStructureRepository
    {
        private readonly dbContext _db;
        public FeeStructureRepository(dbContext db) => _db = db;

        public async Task<List<FeeStructureListRow>> GetListAsync(string schoolId, long? academicYearId, long? classId)
        {
            var q = _db.FeeStructures.AsNoTracking().Where(s => s.SchoolId == schoolId);
            if (academicYearId != null) q = q.Where(s => s.AcademicYearId == academicYearId);
            if (classId != null) q = q.Where(s => s.ClassId == classId);

            // Both joins are also filtered by school, so a row can never pull in another school's names
            var rows = await (
                from s in q
                join y in _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId) on s.AcademicYearId equals y.AcademicYearId
                join c in _db.Classes.AsNoTracking().Where(c => c.SchoolId == schoolId) on s.ClassId equals c.ClassId
                orderby y.StartDate descending, c.SequenceOrder
                select new
                {
                    Structure = s,
                    y.YearName,
                    c.ClassName,
                    LineCount = _db.FeeStructureDetails.Count(d => d.FeeStructureId == s.FeeStructureId)
                }).ToListAsync();

            return rows.Select(r => new FeeStructureListRow
            {
                Structure = r.Structure, AcademicYearName = r.YearName, ClassName = r.ClassName, LineCount = r.LineCount
            }).ToList();
        }

        public Task<TbFeeStructures?> GetByIdAsync(string schoolId, long id, bool track = false)
        {
            IQueryable<TbFeeStructures> q = _db.FeeStructures.Where(s => s.SchoolId == schoolId && s.FeeStructureId == id);
            if (!track) q = q.AsNoTracking();
            return q.FirstOrDefaultAsync();
        }

        public Task<TbFeeStructures?> GetWithDetailsForUpdateAsync(string schoolId, long id) =>
            _db.FeeStructures.Include(s => s.Details).Where(s => s.SchoolId == schoolId && s.FeeStructureId == id).FirstOrDefaultAsync();

        public async Task<List<FeeStructureLineRow>> GetLinesAsync(long feeStructureId)
        {
            var rows = await (
                from d in _db.FeeStructureDetails.AsNoTracking()
                join t in _db.FeeTypes.AsNoTracking() on d.FeeTypeId equals t.FeeTypeId
                join f in _db.FeeFrequencies.AsNoTracking() on d.FrequencyId equals f.FrequencyId
                where d.FeeStructureId == feeStructureId
                orderby t.FeeTypeName
                select new { d.FeeStructureDetailId, d.FeeTypeId, t.FeeTypeName, t.FeeTypeCode, d.FrequencyId, f.FrequencyName, d.Amount, d.DueDay, d.EffectiveFrom }).ToListAsync();

            return rows.Select(r => new FeeStructureLineRow
            {
                FeeStructureDetailId = r.FeeStructureDetailId, FeeTypeId = r.FeeTypeId, FeeTypeName = r.FeeTypeName, FeeTypeCode = r.FeeTypeCode,
                FrequencyId = r.FrequencyId, FrequencyName = r.FrequencyName, Amount = r.Amount, DueDay = r.DueDay, EffectiveFrom = r.EffectiveFrom
            }).ToList();
        }

        public async Task<(DateOnly Start, DateOnly End, bool IsCurrent)?> GetYearRangeAsync(string schoolId, long academicYearId)
        {
            var y = await _db.AcademicYears.AsNoTracking().Where(a => a.SchoolId == schoolId && a.AcademicYearId == academicYearId)
                .Select(a => new { a.StartDate, a.EndDate, a.IsCurrent }).FirstOrDefaultAsync();
            return y == null ? null : (y.StartDate, y.EndDate, y.IsCurrent);
        }

        public async Task<HashSet<long>> GetFeeTypeIdsWithDuesAsync(string schoolId, long feeStructureId) =>
            (await (from d in _db.StudentFeeDues.AsNoTracking()
                    join i in _db.StudentFeeItems.AsNoTracking() on d.StudentFeeItemId equals i.StudentFeeItemId
                    join a in _db.StudentFeeStructures.AsNoTracking() on new { i.StudentId, i.AcademicYearId } equals new { a.StudentId, a.AcademicYearId }
                    where d.SchoolId == schoolId && a.SchoolId == schoolId && a.FeeStructureId == feeStructureId
                    select i.FeeTypeId).Distinct().ToListAsync()).ToHashSet();

        public Task<bool> HasAssignedStudentsAsync(string schoolId, long feeStructureId) =>
            _db.StudentFeeStructures.AnyAsync(a => a.SchoolId == schoolId && a.FeeStructureId == feeStructureId);

        public Task<List<TbStudentFeeDue>> GetLineDuesAsync(string schoolId, long feeStructureId, long feeTypeId) =>
            (from d in _db.StudentFeeDues.AsNoTracking()   // read fresh from the database: the payment check must see the latest amount_paid (RemoveDues attaches what it deletes)
             join i in _db.StudentFeeItems on d.StudentFeeItemId equals i.StudentFeeItemId
             join a in _db.StudentFeeStructures on new { i.StudentId, i.AcademicYearId } equals new { a.StudentId, a.AcademicYearId }
             where d.SchoolId == schoolId && a.SchoolId == schoolId && a.FeeStructureId == feeStructureId && i.FeeTypeId == feeTypeId
             select d).ToListAsync();

        public async Task<List<LineRemovalFact>> GetLineRemovalFactsAsync(string schoolId, long feeStructureId, long feeTypeId)
        {
            var rows = await (
                from i in _db.StudentFeeItems.AsNoTracking()
                join a in _db.StudentFeeStructures.AsNoTracking() on new { i.StudentId, i.AcademicYearId } equals new { a.StudentId, a.AcademicYearId }
                where i.SchoolId == schoolId && a.SchoolId == schoolId && a.FeeStructureId == feeStructureId && i.FeeTypeId == feeTypeId
                join d in _db.StudentFeeDues.AsNoTracking() on i.StudentFeeItemId equals d.StudentFeeItemId into ds
                from d in ds.DefaultIfEmpty()
                select new { i.StudentFeeItemId, Due = d }).ToListAsync();
            return rows.Select(r => new LineRemovalFact(r.StudentFeeItemId, r.Due == null ? null : r.Due.DueDate, r.Due == null ? 0 : r.Due.AmountDue, r.Due == null ? 0 : r.Due.AmountPaid)).ToList();
        }

        public void AddRemovalLog(TbFeeLineRemovalLog row) => _db.FeeLineRemovalLogs.Add(row);

        public async Task RemoveDuesAsync(IReadOnlyCollection<long> dueIds)
        {
            if (dueIds.Count == 0) return;
            var ids = dueIds.ToArray();
            _db.StudentFeeDues.RemoveRange(await _db.StudentFeeDues.Where(d => ids.Contains(d.FeeDueId)).ToListAsync());
        }

        public async Task LockStructureSavesAsync(string schoolId)
        {
            var key = "feestruct:" + schoolId;
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))");
        }

        public Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransactionAsync() => _db.Database.BeginTransactionAsync();

        public Task<string?> GetAcademicYearNameAsync(string schoolId, long academicYearId) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.AcademicYearId == academicYearId)
                .Select(y => (string?)y.YearName).FirstOrDefaultAsync();

        public Task<string?> GetClassNameAsync(string schoolId, long classId) =>
            _db.Classes.AsNoTracking().Where(c => c.SchoolId == schoolId && c.ClassId == classId)
                .Select(c => (string?)c.ClassName).FirstOrDefaultAsync();

        public Task<bool> StructureExistsAsync(string schoolId, long academicYearId, long classId) =>
            _db.FeeStructures.AnyAsync(s => s.SchoolId == schoolId && s.AcademicYearId == academicYearId && s.ClassId == classId);

        public Task<Dictionary<long, bool>> GetFeeTypeActiveFlagsAsync(string schoolId, IReadOnlyCollection<long> ids) =>
            _db.FeeTypes.AsNoTracking().Where(t => t.SchoolId == schoolId && ids.Contains(t.FeeTypeId)).ToDictionaryAsync(t => t.FeeTypeId, t => t.IsActive);

        public async Task<HashSet<long>> GetActiveFrequencyIdsAsync(IReadOnlyCollection<long> ids) =>
            (await _db.FeeFrequencies.AsNoTracking().Where(f => f.IsActive && ids.Contains(f.FrequencyId)).Select(f => f.FrequencyId).ToListAsync()).ToHashSet();

        public Task<List<LutFeeFrequency>> GetFrequenciesAsync() =>
            _db.FeeFrequencies.AsNoTracking().Where(f => f.IsActive).OrderBy(f => f.FrequencyId).ToListAsync();

        public async Task AddAsync(TbFeeStructures entity) => await _db.FeeStructures.AddAsync(entity);
        public void RemoveDetails(IEnumerable<TbFeeStructureDetails> details) => _db.FeeStructureDetails.RemoveRange(details);
        public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
    }
}
