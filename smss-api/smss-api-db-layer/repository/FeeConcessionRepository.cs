using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class FeeConcessionRepository : IFeeConcessionRepository
    {
        private readonly dbContext _db;
        public FeeConcessionRepository(dbContext db) => _db = db;

        public Task<List<TbFeeConcessions>> GetBySchoolAsync(string schoolId) =>
            _db.FeeConcessions.AsNoTracking().Where(x => x.SchoolId == schoolId).OrderBy(x => x.ConcessionName).ToListAsync();

        public Task<TbFeeConcessions?> GetByIdAsync(string schoolId, long id, bool track = false)
        {
            IQueryable<TbFeeConcessions> q = _db.FeeConcessions.Where(x => x.SchoolId == schoolId && x.ConcessionId == id);
            if (!track) q = q.AsNoTracking();
            return q.FirstOrDefaultAsync();
        }

        public Task<bool> CodeExistsAsync(string schoolId, string code, long? excludeId = null) =>
            _db.FeeConcessions.AnyAsync(x => x.SchoolId == schoolId && x.ConcessionCode == code && (excludeId == null || x.ConcessionId != excludeId));

        public Task<Dictionary<long, string>> GetFeeTypeNamesAsync(string schoolId) =>
            _db.FeeTypes.AsNoTracking().Where(x => x.SchoolId == schoolId).ToDictionaryAsync(x => x.FeeTypeId, x => x.FeeTypeName);

        public async Task AddAsync(TbFeeConcessions entity) => await _db.FeeConcessions.AddAsync(entity);
        public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
    }
}
