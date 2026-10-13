using Microsoft.EntityFrameworkCore.Storage;

namespace smss_api_db_layer.@interface
{
    /// <summary>The school's existing master rows, for the one-shot "Import Masters" page. Every method takes the school id.</summary>
    public interface IMasterImportRepository
    {
        Task<Dictionary<string, long>> GetClassIdsByNameAsync(string schoolId, CancellationToken ct);
        /// <summary>Ids of this school's classes that already have at least one section.</summary>
        Task<HashSet<long>> GetClassIdsWithSectionsAsync(string schoolId, CancellationToken ct);
        Task<List<string>> GetSubjectNamesAsync(string schoolId, CancellationToken ct);
        Task<List<string>> GetDesignationNamesAsync(string schoolId, CancellationToken ct);
        Task<List<string>> GetDepartmentNamesAsync(string schoolId, CancellationToken ct);
        Task<List<string>> GetAdmissionTypeNamesAsync(string schoolId, CancellationToken ct);
        Task<List<string>> GetDocumentCodesAsync(string schoolId, CancellationToken ct);
        Task<int?> GetActiveStatusIdAsync(CancellationToken ct);

        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        Task AddRangeAsync<T>(IEnumerable<T> entities, CancellationToken ct) where T : class;
        Task SaveChangesAsync(CancellationToken ct);
    }
}
