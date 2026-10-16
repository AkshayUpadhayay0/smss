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
                select new { d.FeeStructureDetailId, d.FeeTypeId, t.FeeTypeName, t.FeeTypeCode, d.FrequencyId, f.FrequencyName, d.Amount, d.DueDay }).ToListAsync();

            return rows.Select(r => new FeeStructureLineRow
            {
                FeeStructureDetailId = r.FeeStructureDetailId, FeeTypeId = r.FeeTypeId, FeeTypeName = r.FeeTypeName, FeeTypeCode = r.FeeTypeCode,
                FrequencyId = r.FrequencyId, FrequencyName = r.FrequencyName, Amount = r.Amount, DueDay = r.DueDay
            }).ToList();
        }

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
