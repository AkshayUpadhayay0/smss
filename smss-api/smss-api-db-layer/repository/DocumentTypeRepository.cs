using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class DocumentTypeRepository : IDocumentTypeRepository
    {
        private readonly dbContext _dbContext;

        public DocumentTypeRepository(dbContext dbContext)
        {
            _dbContext = dbContext;
        }

        // Always filtered by school: there is no way to read another school's rows through this repository.
        public async Task<List<TbDocumentType>> GetAllAsync(string schoolId, CancellationToken ct)
        {
            return await _dbContext.DocumentTypes
                .AsNoTracking()
                .Where(x => x.SchoolId == schoolId)
                .OrderBy(x => x.DocumentName)
                .ToListAsync(ct);
        }

        // Tracked on purpose: used for update/toggle.
        public async Task<TbDocumentType?> GetByIdAsync(long id, string schoolId, CancellationToken ct)
        {
            return await _dbContext.DocumentTypes
                .FirstOrDefaultAsync(x => x.DocumentTypeId == id && x.SchoolId == schoolId, ct);
        }

        public async Task<bool> CodeExistsAsync(string schoolId, string documentCode, long? excludeId, CancellationToken ct)
        {
            return await _dbContext.DocumentTypes
                .AnyAsync(x => x.SchoolId == schoolId
                               && x.DocumentCode == documentCode
                               && (!excludeId.HasValue || x.DocumentTypeId != excludeId.Value), ct);
        }

        public async Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct)
        {
            return await _dbContext.LutStatus
                .AsNoTracking()
                .Where(s => s.Sname == statusName && s.Stype == statusType)
                .Select(s => (int?)s.Sid)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<Dictionary<int, string>> GetStatusNamesAsync(string statusType, CancellationToken ct)
        {
            return await _dbContext.LutStatus
                .AsNoTracking()
                .Where(s => s.Stype == statusType)
                .ToDictionaryAsync(s => s.Sid, s => s.Sname, ct);
        }

        public async Task AddAsync(TbDocumentType entity, CancellationToken ct)
        {
            await _dbContext.DocumentTypes.AddAsync(entity, ct);
        }

        public async Task<int> SaveChangesAsync(CancellationToken ct)
        {
            return await _dbContext.SaveChangesAsync(ct);
        }
    }
}
