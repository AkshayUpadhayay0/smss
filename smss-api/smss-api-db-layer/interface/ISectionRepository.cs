using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    // Every method takes the school id: there is deliberately no way to read or change a section without one.
    public interface ISectionRepository
    {
        Task<List<SectionWithClass>> GetBySchoolAsync(string schoolId, long? classId = null);
        Task<SectionWithClass?> GetWithClassByIdAsync(string schoolId, long id);
        Task<TbSections?> GetByIdAsync(string schoolId, long id, bool track = false);
        /// <summary>The class, only if it belongs to this school.</summary>
        Task<TbClasses?> GetClassAsync(string schoolId, long classId);
        Task<bool> ExistsAsync(long classId, string sectionName, long? excludeId = null);
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType);
        Task<List<(int Sid, string Name)>> GetGeneralStatusesAsync(string statusType);
        Task AddAsync(TbSections entity);
        Task<int> SaveChangesAsync();
    }
}
