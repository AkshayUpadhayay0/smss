using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.context;
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
        Task<List<AcademicYearUsage>> GetUsageAsync(string schoolId, CancellationToken ct = default);
        /// <summary>An ACTIVE year of the school whose dates overlap [start, end] (inclusive), other than excludeId.</summary>
        Task<TbAcademicYears?> FindOverlapAsync(string schoolId, DateOnly start, DateOnly end, int activeStatusId, long? excludeId);
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);
        /// <summary>Offers every existing class and section of the school in this year (used when it is the school's first year).</summary>
        Task OfferAllAsync(string schoolId, long academicYearId, CancellationToken ct = default);
        Task<int> SaveChangesAsync();
    }
}
