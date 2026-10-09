using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    // Every method takes the school id: there is deliberately no way to read or change a class without one.
    public interface IClassRepository
    {
        Task<List<TbClasses>> GetBySchoolAsync(string schoolId);
        Task<TbClasses?> GetByIdAsync(string schoolId, long id, bool track = false);
        Task<bool> ExistsAsync(string schoolId, string className, long? excludeId = null);
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType);
        Task<List<(int Sid, string Name)>> GetGeneralStatusesAsync(string statusType);
        Task AddAsync(TbClasses entity);
        Task<int> SaveChangesAsync();
    }
}
