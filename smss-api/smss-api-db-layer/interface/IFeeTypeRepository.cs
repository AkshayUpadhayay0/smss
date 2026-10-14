using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    // Every method takes the school id: there is deliberately no way to read or change a fee type without one.
    public interface IFeeTypeRepository
    {
        Task<List<TbFeeTypes>> GetBySchoolAsync(string schoolId);
        Task<TbFeeTypes?> GetByIdAsync(string schoolId, long id, bool track = false);
        Task<bool> CodeExistsAsync(string schoolId, string code, long? excludeId = null);
        Task AddAsync(TbFeeTypes entity);
        Task<int> SaveChangesAsync();
    }
}
