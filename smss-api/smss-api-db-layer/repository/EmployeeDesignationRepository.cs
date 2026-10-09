using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class EmployeeDesignationRepository : IEmployeeDesignationRepository
    {
        private readonly dbContext _db;
        public EmployeeDesignationRepository(dbContext db) => _db = db;

        public Task<List<TbEmployeeDesignations>> GetBySchoolAsync(string schoolId) =>
            _db.EmployeeDesignations.AsNoTracking()
                .Where(x => x.SchoolId == schoolId)
                .OrderBy(x => x.DesignationName)
                .ToListAsync();

        public Task<TbEmployeeDesignations?> GetByIdAsync(string schoolId, long id, bool track = false)
        {
            IQueryable<TbEmployeeDesignations> q = _db.EmployeeDesignations.Where(x => x.SchoolId == schoolId && x.DesignationId == id);
            if (!track) q = q.AsNoTracking();
            return q.FirstOrDefaultAsync();
        }

        public Task<bool> ExistsAsync(string schoolId, string name, long? excludeId = null) =>
            _db.EmployeeDesignations.AnyAsync(x => x.SchoolId == schoolId && x.DesignationName == name
                                         && (excludeId == null || x.DesignationId != excludeId));

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

        public async Task AddAsync(TbEmployeeDesignations entity) => await _db.EmployeeDesignations.AddAsync(entity);

        public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
    }
}
