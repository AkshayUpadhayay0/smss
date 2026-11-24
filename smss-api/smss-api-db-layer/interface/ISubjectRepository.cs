using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    // Every method takes the school id: there is deliberately no way to read or change a subject without one.
    public interface ISubjectRepository
    {
        Task<List<TbSubjects>> GetBySchoolAsync(string schoolId);
        Task<TbSubjects?> GetByIdAsync(string schoolId, long id, bool track = false);
        Task<bool> ExistsAsync(string schoolId, string name, long? excludeId = null);
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType);
        Task<List<(int Sid, string Name)>> GetGeneralStatusesAsync(string statusType);
        Task AddAsync(TbSubjects entity);
        /// <summary>What still uses the subject in an OPEN academic year (empty = safe to deactivate).</summary>
        Task<List<string>> GetUseAsync(string schoolId, long subjectId);
        Task<int> SaveChangesAsync();
    }
}
