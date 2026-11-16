using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    // Every method takes the school id: another school's tax types can never be reached through this repository.
    public interface ITaxTypeRepository
    {
        Task<List<TbTaxType>> GetBySchoolAsync(string schoolId, bool includeInactive, CancellationToken ct);
        Task<TbTaxType?> GetByIdAsync(string schoolId, long id, bool track, CancellationToken ct);
        /// <summary>Name or code already used by another type of this school (case-insensitive). Returns "name", "code" or null.</summary>
        Task<string?> FindDuplicateAsync(string schoolId, string name, string code, long? excludeId, CancellationToken ct);
        Task AddAsync(TbTaxType entity, CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);
    }
}
