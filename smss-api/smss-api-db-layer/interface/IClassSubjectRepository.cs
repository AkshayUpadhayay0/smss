using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    // Every method takes the school id: there is deliberately no way to read or change a mapping without one.
    public interface IClassSubjectRepository
    {
        Task<List<ClassSubjectWithNames>> GetByClassAsync(string schoolId, long classId);
        Task<ClassSubjectWithNames?> GetWithNamesByIdAsync(string schoolId, long id);
        Task<TbClassSubjects?> GetByIdAsync(string schoolId, long id);   // tracked
        /// <summary>Tracked mappings of one class, for the bulk replace.</summary>
        Task<List<TbClassSubjects>> GetTrackedByClassAsync(string schoolId, long classId);
        /// <summary>The class, only if it belongs to this school.</summary>
        Task<TbClasses?> GetClassAsync(string schoolId, long classId);
        /// <summary>How many of these subject ids belong to this school.</summary>
        Task<int> CountSubjectsAsync(string schoolId, IReadOnlyCollection<long> subjectIds);
        Task<bool> ExistsAsync(long classId, long subjectId);
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType);
        Task AddAsync(TbClassSubjects entity);
        void Remove(TbClassSubjects entity);
        void RemoveRange(IEnumerable<TbClassSubjects> entities);
        Task<int> SaveChangesAsync();
    }
}
