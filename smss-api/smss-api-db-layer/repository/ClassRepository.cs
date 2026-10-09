using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class ClassRepository : IClassRepository
    {
        private readonly dbContext _db;
        public ClassRepository(dbContext db) => _db = db;

        public Task<List<TbClasses>> GetBySchoolAsync(string schoolId) =>
            _db.Classes.AsNoTracking()
                .Where(c => c.SchoolId == schoolId)
                .OrderBy(c => c.SequenceOrder).ThenBy(c => c.ClassName)
                .ToListAsync();

        public Task<TbClasses?> GetByIdAsync(string schoolId, long id, bool track = false)
        {
            IQueryable<TbClasses> q = _db.Classes.Where(c => c.SchoolId == schoolId && c.ClassId == id);
            if (!track) q = q.AsNoTracking();
            return q.FirstOrDefaultAsync();
        }

        public Task<bool> ExistsAsync(string schoolId, string className, long? excludeId = null) =>
            _db.Classes.AnyAsync(c => c.SchoolId == schoolId && c.ClassName == className
                                      && (excludeId == null || c.ClassId != excludeId));

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

        public async Task AddAsync(TbClasses entity) => await _db.Classes.AddAsync(entity);

        public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
    }
}
