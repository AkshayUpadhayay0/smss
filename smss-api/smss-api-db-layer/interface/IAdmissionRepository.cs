using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    /// <summary>What student ids and notification emails need to know about a school.</summary>
    public record SchoolIdentity(string SchoolName, string? ShortCode);

    /// <summary>The class a section belongs to.</summary>
    public record SectionClassInfo(long ClassId, string ClassName);

    /// <summary>A fee structure picked for automatic assignment.</summary>
    public record FeeStructureRef(long FeeStructureId, string StructureName);

    // Every method that reads or changes a school's data takes the school id.
    public interface IAdmissionRepository
    {
        /// <summary>The school's applications, newest first; each filter is optional.</summary>
        Task<List<TbAdmission>> GetAllAsync(string schoolId, int? statusId, long? classId, long? academicYearId, CancellationToken ct);
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

        Task<SchoolIdentity?> GetSchoolIdentityAsync(string schoolId, CancellationToken ct);
        /// <summary>The class of one of this school's sections (null when the section is not this school's).</summary>
        Task<SectionClassInfo?> GetSectionClassAsync(string schoolId, long sectionId, CancellationToken ct);
        /// <summary>This school's ACTIVE fee structure for the class + academic year, or null when there is none.</summary>
        Task<FeeStructureRef?> FindActiveFeeStructureAsync(string schoolId, long classId, long academicYearId, CancellationToken ct);
        Task AddStudentFeeStructureAsync(TbStudentFeeStructure entity, CancellationToken ct);
        /// <summary>How many students the school has (any status): the next student id is this + 1.</summary>
        Task<int> CountStudentsAsync(string schoolId, CancellationToken ct);

        // Confirm-admission writes
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        Task AddStudentAsync(TbStudent entity, CancellationToken ct);
        Task AddEnrollmentAsync(TbStudentEnrollment entity, CancellationToken ct);
        Task AddGuardianAsync(TbGuardian entity, CancellationToken ct);
        Task AddStudentGuardianAsync(TbStudentGuardian entity, CancellationToken ct);

        /// <summary>Transaction-scoped lock for handing out application numbers, so two submissions for one school never pick the same number (the receipt-number pattern).</summary>
        Task LockApplicationNumberAsync(string schoolId, CancellationToken ct);
        /// <summary>Transaction-scoped lock for one application, so a decision, a conversion or a second conversion of it queue up.</summary>
        Task LockApplicationAsync(string schoolId, long admissionId, CancellationToken ct);
        /// <summary>Does this school have a student with that id?</summary>
        Task<bool> StudentExistsAsync(string schoolId, string studentId, CancellationToken ct);
        /// <summary>Is the student already linked to an application other than the given one?</summary>
        Task<bool> StudentLinkedToAnotherApplicationAsync(string studentId, long exceptAdmissionId, CancellationToken ct);
        /// <summary>The student's enrollment for the academic year (any status), or null.</summary>
        Task<long?> GetEnrollmentIdAsync(string schoolId, string studentId, long academicYearId, CancellationToken ct);

        Task<int> SaveChangesAsync(CancellationToken ct);
        /// <summary>Forgets every pending / failed entity so a retry after a failed save starts from a clean context.</summary>
        void ClearTracking();
    }
}
