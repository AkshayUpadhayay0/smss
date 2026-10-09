using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class AdmissionTypeRepository : IAdmissionTypeRepository
    {
        private readonly dbContext _db;
        public AdmissionTypeRepository(dbContext db) => _db = db;

        public Task<List<TbAdmissionTypes>> GetBySchoolAsync(string schoolId) =>
            _db.AdmissionTypes.AsNoTracking()
                .Where(x => x.SchoolId == schoolId)
                .OrderBy(x => x.TypeName)
                .ToListAsync();

        public Task<TbAdmissionTypes?> GetByIdAsync(string schoolId, long id, bool track = false)
        {
            IQueryable<TbAdmissionTypes> q = _db.AdmissionTypes.Where(x => x.SchoolId == schoolId && x.AdmissionTypeId == id);
            if (!track) q = q.AsNoTracking();
            return q.FirstOrDefaultAsync();
        }

        public Task<bool> ExistsAsync(string schoolId, string name, long? excludeId = null) =>
            _db.AdmissionTypes.AnyAsync(x => x.SchoolId == schoolId && x.TypeName == name
                                         && (excludeId == null || x.AdmissionTypeId != excludeId));

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

        public async Task AddAsync(TbAdmissionTypes entity) => await _db.AdmissionTypes.AddAsync(entity);

        public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
    }
}
