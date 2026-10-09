using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    // Every method takes the school id: there is deliberately no way to read or change a year without one.
    public interface IAcademicYearRepository
    {
        Task<List<TbAcademicYears>> GetBySchoolAsync(string schoolId);
        Task<TbAcademicYears?> GetByIdAsync(string schoolId, long id, bool track = false);
        Task<List<TbAcademicYears>> GetCurrentYearsAsync(string schoolId);   // tracked
        Task<bool> ExistsAsync(string schoolId, string yearName, long? excludeId = null);
        Task<bool> AnyAsync(string schoolId);
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType);
        Task<List<(int Sid, string Name)>> GetGeneralStatusesAsync(string statusType);
        Task AddAsync(TbAcademicYears year);
        Task<int> SaveChangesAsync();
    }
}
