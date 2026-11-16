using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class TaxTypeRepository : ITaxTypeRepository
    {
        private readonly dbContext _db;
        public TaxTypeRepository(dbContext db) => _db = db;

        public Task<List<TbTaxType>> GetBySchoolAsync(string schoolId, bool includeInactive, CancellationToken ct) =>
            _db.TaxTypes.AsNoTracking().Where(x => x.SchoolId == schoolId && (includeInactive || x.IsActive)).OrderBy(x => x.TaxName).ToListAsync(ct);

        public Task<TbTaxType?> GetByIdAsync(string schoolId, long id, bool track, CancellationToken ct)
        {
            IQueryable<TbTaxType> q = _db.TaxTypes.Where(x => x.SchoolId == schoolId && x.TaxTypeId == id);
            if (!track) q = q.AsNoTracking();
            return q.FirstOrDefaultAsync(ct);
        }

        public async Task<string?> FindDuplicateAsync(string schoolId, string name, string code, long? excludeId, CancellationToken ct)
        {
            var n = name.ToLower(); var c = code.ToLower();
            var rows = await _db.TaxTypes.AsNoTracking()
                .Where(x => x.SchoolId == schoolId && (excludeId == null || x.TaxTypeId != excludeId) && (x.TaxName.ToLower() == n || x.TaxCode.ToLower() == c))
                .Select(x => new { x.TaxName, x.TaxCode }).ToListAsync(ct);
            if (rows.Any(x => x.TaxCode.ToLower() == c)) return "code";
            if (rows.Any(x => x.TaxName.ToLower() == n)) return "name";
            return null;
        }

        public async Task AddAsync(TbTaxType entity, CancellationToken ct) => await _db.TaxTypes.AddAsync(entity, ct);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
    }
}
