using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    // Every method takes the school id: another school's concessions can never be reached through this repository.
    public interface IFeeConcessionRepository
    {
        Task<List<TbFeeConcessions>> GetBySchoolAsync(string schoolId);
        Task<TbFeeConcessions?> GetByIdAsync(string schoolId, long id, bool track = false);
        Task<bool> CodeExistsAsync(string schoolId, string code, long? excludeId = null);
        /// <summary>Fee type id -> name for this school's fee types (validates and names the applicable fee head).</summary>
        Task<Dictionary<long, string>> GetFeeTypeNamesAsync(string schoolId);
        Task AddAsync(TbFeeConcessions entity);
        Task<int> SaveChangesAsync();
    }
}
