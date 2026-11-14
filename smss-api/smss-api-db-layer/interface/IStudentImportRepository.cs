using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record ImportClassRow(long ClassId, string ClassName, short SequenceOrder);
    public record ImportSectionRow(long SectionId, long ClassId, string SectionName);
    public record ImportGenderRow(int GenderId, string GenderName, string GenderCode);
    public record ImportRollRow(long SectionId, string RollNumber);
    /// <summary>An existing student with the mobile numbers of their guardians, for the "possible duplicate person" warning.</summary>
    public record ImportPersonRow(string StudentId, string FirstName, string LastName, DateTime DateOfBirth, IReadOnlyList<string> GuardianMobiles);

    /// <summary>Set-based reads and one bulk write for the student Excel import: a fixed number of queries however many rows the file has.</summary>
    public interface IStudentImportRepository
    {
        Task<int?> GetActiveStatusIdAsync(CancellationToken ct);
        Task<LutAdmissionType?> GetAdmissionTypeByNameAsync(string name, CancellationToken ct);
        Task<TbAcademicYears?> GetCurrentYearAsync(string schoolId, int activeStatusId, CancellationToken ct);
        Task<string?> GetSchoolShortCodeAsync(string schoolId, CancellationToken ct);
        Task<List<ImportClassRow>> ListClassesAsync(string schoolId, int activeStatusId, CancellationToken ct);
        Task<List<ImportSectionRow>> ListSectionsAsync(string schoolId, int activeStatusId, CancellationToken ct);
        Task<List<ImportGenderRow>> ListGendersAsync(CancellationToken ct);
        Task<HashSet<string>> ExistingAdmissionNumbersAsync(string schoolId, IReadOnlyCollection<string> numbers, CancellationToken ct);
        Task<List<ImportRollRow>> ExistingRollsAsync(string schoolId, long academicYearId, IReadOnlyCollection<long> sectionIds, CancellationToken ct);
        Task<List<ImportPersonRow>> FindPeopleBornOnAsync(string schoolId, IReadOnlyCollection<DateTime> dates, CancellationToken ct);
        Task<int> CountStudentsAsync(string schoolId, CancellationToken ct);
        Task<int> MaxAdmissionNumberSequenceAsync(string schoolId, string prefix, CancellationToken ct);
        /// <summary>Serialises imports (and their admission-number allocation) for one school until the transaction ends.</summary>
        Task LockSchoolImportAsync(string schoolId, CancellationToken ct);

        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        void AddRange(IEnumerable<object> entities);
        Task<int> SaveChangesAsync(CancellationToken ct);
        void ClearTracking();
    }
}
