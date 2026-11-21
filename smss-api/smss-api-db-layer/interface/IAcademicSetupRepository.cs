using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record OfferingYearRow(long AcademicYearId, string YearName, bool IsCurrent, bool IsClosed);
    public record OfferingClassMaster(long ClassId, string ClassName, short SequenceOrder, int? StatusId);
    public record OfferingSectionMaster(long SectionId, long ClassId, string SectionName, short? MaxStrength, int? StatusId);

    /// <summary>
    /// Which classes and sections a school offers in an academic year (S2). Every method takes the school id; the year must belong to it.
    /// The class / section MASTERS are read here but never changed.
    /// </summary>
    public interface IAcademicSetupRepository
    {
        Task<OfferingYearRow?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct);
        Task<List<OfferingClassMaster>> GetClassMastersAsync(string schoolId, CancellationToken ct);
        Task<List<OfferingSectionMaster>> GetSectionMastersAsync(string schoolId, CancellationToken ct);

        /// <summary>The year's offering rows, tracked so a save can change them.</summary>
        Task<List<TbAcademicYearClass>> GetClassOfferingsAsync(string schoolId, long academicYearId, CancellationToken ct);
        Task<List<TbAcademicYearSection>> GetSectionOfferingsAsync(string schoolId, long academicYearId, CancellationToken ct);

        /// <summary>What is in use in the year (all counts are of ACTIVE rows), keyed by section or class, so a withdrawal can be refused.</summary>
        Task<Dictionary<long, int>> EnrolmentsBySectionAsync(string schoolId, long academicYearId, int activeStatusId, CancellationToken ct);
        Task<HashSet<long>> FeeStructureClassIdsAsync(string schoolId, long academicYearId, CancellationToken ct);
        Task<HashSet<long>> ClassTeacherSectionIdsAsync(string schoolId, long academicYearId, int activeStatusId, CancellationToken ct);
        Task<Dictionary<long, int>> TimetableBySectionAsync(string schoolId, long academicYearId, int activeStatusId, CancellationToken ct);

        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct);
        /// <summary>One lock per school for every multi-row academic-setup save, so two admins cannot interleave. Released with the transaction.</summary>
        Task LockSchoolAsync(string schoolId, CancellationToken ct);
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        Task AddAsync<T>(T entity, CancellationToken ct) where T : class;
        Task SaveChangesAsync(CancellationToken ct);
    }
}
