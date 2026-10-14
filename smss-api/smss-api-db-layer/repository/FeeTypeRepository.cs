using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class FeeTypeRepository : IFeeTypeRepository
    {
        private readonly dbContext _db;
        public FeeTypeRepository(dbContext db) => _db = db;

        public Task<List<TbFeeTypes>> GetBySchoolAsync(string schoolId) =>
            _db.FeeTypes.AsNoTracking().Where(x => x.SchoolId == schoolId).OrderBy(x => x.FeeTypeName).ToListAsync();

        public Task<TbFeeTypes?> GetByIdAsync(string schoolId, long id, bool track = false)
        {
            IQueryable<TbFeeTypes> q = _db.FeeTypes.Where(x => x.SchoolId == schoolId && x.FeeTypeId == id);
            if (!track) q = q.AsNoTracking();
            return q.FirstOrDefaultAsync();
        }

        public Task<bool> CodeExistsAsync(string schoolId, string code, long? excludeId = null) =>
            _db.FeeTypes.AnyAsync(x => x.SchoolId == schoolId && x.FeeTypeCode == code && (excludeId == null || x.FeeTypeId != excludeId));

        public async Task AddAsync(TbFeeTypes entity) => await _db.FeeTypes.AddAsync(entity);
        public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
    }
}
