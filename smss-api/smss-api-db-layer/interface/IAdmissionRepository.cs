using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    // Every method that reads or changes a school's data takes the school id.
    public interface IAdmissionRepository
    {
        Task<List<TbAdmission>> GetAllAsync(string schoolId, CancellationToken ct);
        Task<TbAdmission?> GetByIdAsync(string schoolId, long admissionId, bool track, CancellationToken ct);
        Task AddAdmissionAsync(TbAdmission entity, CancellationToken ct);

        // Display names (this school's own rows only)
        Task<Dictionary<long, string>> GetClassNamesAsync(string schoolId, IReadOnlyCollection<long> ids, CancellationToken ct);
        Task<Dictionary<long, string>> GetAdmissionTypeNamesAsync(string schoolId, IReadOnlyCollection<long> ids, CancellationToken ct);
        Task<Dictionary<long, string>> GetAcademicYearNamesAsync(string schoolId, IReadOnlyCollection<long> ids, CancellationToken ct);
        Task<string?> GetGenderNameAsync(long id, CancellationToken ct);
        Task<string?> GetBloodGroupNameAsync(long id, CancellationToken ct);
        Task<string?> GetStudentCategoryNameAsync(long id, CancellationToken ct);

        // Ownership / existence checks used before accepting ids from a request
        Task<bool> AcademicYearBelongsToSchoolAsync(string schoolId, long id, CancellationToken ct);
        Task<bool> ClassBelongsToSchoolAsync(string schoolId, long id, CancellationToken ct);
        Task<bool> AdmissionTypeBelongsToSchoolAsync(string schoolId, long id, CancellationToken ct);
        /// <summary>This school's admission type with that name (case-insensitive), or null.</summary>
        Task<TbAdmissionTypes?> GetAdmissionTypeByNameAsync(string schoolId, string name, CancellationToken ct);
        Task<bool> SectionBelongsToSchoolAsync(string schoolId, long sectionId, CancellationToken ct);
        Task<bool> GenderExistsAsync(long id, CancellationToken ct);
        Task<bool> BloodGroupExistsAsync(long id, CancellationToken ct);
        Task<bool> StudentCategoryExistsAsync(long id, CancellationToken ct);

        // Statuses
        Task<Dictionary<int, string>> GetAdmissionStatusNamesAsync(CancellationToken ct);       // sid -> name
        Task<int?> GetAdmissionStatusIdAsync(string name, CancellationToken ct);
        Task<int?> GetGeneralStatusIdAsync(string name, CancellationToken ct);                   // "Active" etc.

        // Running numbers (largest existing suffix + 1, per school and year)
        Task<int> GetMaxApplicationSequenceAsync(string schoolId, string prefix, CancellationToken ct);
        Task<int> GetMaxAdmissionNumberSequenceAsync(string schoolId, string prefix, CancellationToken ct);

        // Confirm-admission writes
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        Task AddStudentAsync(TbStudent entity, CancellationToken ct);
        Task AddEnrollmentAsync(TbStudentEnrollment entity, CancellationToken ct);
        Task AddGuardianAsync(TbGuardian entity, CancellationToken ct);
        Task AddStudentGuardianAsync(TbStudentGuardian entity, CancellationToken ct);

        Task<int> SaveChangesAsync(CancellationToken ct);
    }
}
