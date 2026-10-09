using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public interface IDocumentTypeRepository
    {
        Task<List<TbDocumentType>> GetAllAsync(string schoolId, CancellationToken ct);
        Task<TbDocumentType?> GetByIdAsync(long id, string schoolId, CancellationToken ct);
        Task<bool> CodeExistsAsync(string schoolId, string documentCode, long? excludeId, CancellationToken ct);
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct);
        Task<Dictionary<int, string>> GetStatusNamesAsync(string statusType, CancellationToken ct);
        Task AddAsync(TbDocumentType entity, CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);
    }
}
