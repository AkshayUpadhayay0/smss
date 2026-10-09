using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class AcademicYearRepository : IAcademicYearRepository
    {
        private readonly dbContext _db;
        public AcademicYearRepository(dbContext db) => _db = db;

        public Task<List<TbAcademicYears>> GetBySchoolAsync(string schoolId) =>
            _db.AcademicYears.AsNoTracking()
                .Where(y => y.SchoolId == schoolId)
                .OrderByDescending(y => y.StartDate)
                .ToListAsync();

        public Task<TbAcademicYears?> GetByIdAsync(string schoolId, long id, bool track = false)
        {
            IQueryable<TbAcademicYears> q = _db.AcademicYears.Where(y => y.SchoolId == schoolId && y.AcademicYearId == id);
            if (!track) q = q.AsNoTracking();
            return q.FirstOrDefaultAsync();
        }

        public Task<List<TbAcademicYears>> GetCurrentYearsAsync(string schoolId) =>
            _db.AcademicYears.Where(y => y.SchoolId == schoolId && y.IsCurrent).ToListAsync();

        public Task<bool> ExistsAsync(string schoolId, string yearName, long? excludeId = null) =>
            _db.AcademicYears.AnyAsync(y => y.SchoolId == schoolId && y.YearName == yearName
                                            && (excludeId == null || y.AcademicYearId != excludeId));

        public Task<bool> AnyAsync(string schoolId) => _db.AcademicYears.AnyAsync(y => y.SchoolId == schoolId);

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType) =>
            _db.LutStatus.AsNoTracking()
                .Where(s => s.Sname == statusName && s.Stype == statusType)
                .Select(s => (int?)s.Sid)
                .FirstOrDefaultAsync();

        public async Task<List<(int Sid, string Name)>> GetGeneralStatusesAsync(string statusType)
        {
            var rows = await _db.LutStatus.AsNoTracking()
                .Where(s => s.Stype == statusType)
                .Select(s => new { s.Sid, s.Sname })
                .ToListAsync();
            return rows.Select(r => (r.Sid, r.Sname)).ToList();
        }

        public async Task AddAsync(TbAcademicYears year) => await _db.AcademicYears.AddAsync(year);

        public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
    }
}
