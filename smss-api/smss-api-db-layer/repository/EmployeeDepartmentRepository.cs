using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class EmployeeDepartmentRepository : IEmployeeDepartmentRepository
    {
        private readonly dbContext _db;
        public EmployeeDepartmentRepository(dbContext db) => _db = db;

        public Task<List<TbEmployeeDepartments>> GetBySchoolAsync(string schoolId) =>
            _db.EmployeeDepartments.AsNoTracking()
                .Where(x => x.SchoolId == schoolId)
                .OrderBy(x => x.DepartmentName)
                .ToListAsync();

        public Task<TbEmployeeDepartments?> GetByIdAsync(string schoolId, long id, bool track = false)
        {
            IQueryable<TbEmployeeDepartments> q = _db.EmployeeDepartments.Where(x => x.SchoolId == schoolId && x.DepartmentId == id);
            if (!track) q = q.AsNoTracking();
            return q.FirstOrDefaultAsync();
        }

        public Task<bool> ExistsAsync(string schoolId, string name, long? excludeId = null) =>
            _db.EmployeeDepartments.AnyAsync(x => x.SchoolId == schoolId && x.DepartmentName == name
                                         && (excludeId == null || x.DepartmentId != excludeId));

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

        public async Task AddAsync(TbEmployeeDepartments entity) => await _db.EmployeeDepartments.AddAsync(entity);

        public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
    }
}
