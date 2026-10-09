using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    // Every method takes the school id: there is deliberately no way to read or change a department without one.
    public interface IEmployeeDepartmentRepository
    {
        Task<List<TbEmployeeDepartments>> GetBySchoolAsync(string schoolId);
        Task<TbEmployeeDepartments?> GetByIdAsync(string schoolId, long id, bool track = false);
        Task<bool> ExistsAsync(string schoolId, string name, long? excludeId = null);
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType);
        Task<List<(int Sid, string Name)>> GetGeneralStatusesAsync(string statusType);
        Task AddAsync(TbEmployeeDepartments entity);
        Task<int> SaveChangesAsync();
    }
}
