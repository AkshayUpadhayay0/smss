using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    // Every method takes the school id: there is deliberately no way to read or change a designation without one.
    public interface IEmployeeDesignationRepository
    {
        Task<List<TbEmployeeDesignations>> GetBySchoolAsync(string schoolId);
        Task<TbEmployeeDesignations?> GetByIdAsync(string schoolId, long id, bool track = false);
        Task<bool> ExistsAsync(string schoolId, string name, long? excludeId = null);
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType);
        Task<List<(int Sid, string Name)>> GetGeneralStatusesAsync(string statusType);
        Task AddAsync(TbEmployeeDesignations entity);
        Task<int> SaveChangesAsync();
    }
}
