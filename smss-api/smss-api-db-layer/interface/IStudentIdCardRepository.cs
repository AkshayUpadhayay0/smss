namespace smss_api_db_layer.@interface
{
    /// <summary>The school facts printed on a card (from tb_schools; city / state names resolved from the LGD lookups).</summary>
    public record IdCardSchoolRow(string SchoolId, string SchoolCode, string SchoolName, string? LogoUrl,
        string? AddressLine1, string? AddressLine2, string? CityName, string? StateName, string? Pincode, string? MobileNumber);

    public record IdCardYearRow(long AcademicYearId, string YearName);

    /// <summary>One student's enrollment for the current year, with everything a card shows. Status ids are raw; the service compares them to "Active".</summary>
    public record IdCardStudentRow(string StudentId, string AdmissionNumber, string FirstName, string? MiddleName, string LastName,
        string? RollNumber, string? ProfilePhotoUrl, int StudentStatusId, int EnrollmentStatusId, string ClassName, string SectionName,
        DateTime DateOfBirth, int? BloodGroupId, string? MobileNumber, string? AddressLine1, string? AddressLine2, int? CountryId, int? StateId, int? DistrictId, int? CityId, string? Pincode);

    /// <summary>One guardian of a student, as stored (relationship values are upper-case, e.g. FATHER / MOTHER).</summary>
    public record IdCardGuardianRow(string StudentId, string Name, string Relationship, string Mobile, bool IsPrimary);

    /// <summary>Read-only. Every method takes the school id: another school's rows can never come back.</summary>
    public interface IStudentIdCardRepository
    {
        Task<IdCardSchoolRow?> GetSchoolAsync(string schoolId, CancellationToken ct);
        /// <summary>The school's current academic year (is_current), or null when none is set.</summary>
        Task<IdCardYearRow?> GetCurrentYearAsync(string schoolId, CancellationToken ct);
        Task<bool> SectionBelongsToSchoolAsync(string schoolId, long sectionId, CancellationToken ct);
        /// <summary>Every student with an enrollment in the section for that year (any status), so the service can list the ones it leaves out.</summary>
        Task<List<IdCardStudentRow>> GetSectionStudentsAsync(string schoolId, long academicYearId, long sectionId, CancellationToken ct);
        /// <summary>One student's enrollment for that year; null when the student is not in this school or has no enrollment that year.</summary>
        Task<IdCardStudentRow?> GetStudentAsync(string schoolId, long academicYearId, string studentId, CancellationToken ct);
        Task<bool> StudentExistsAsync(string schoolId, string studentId, CancellationToken ct);
        /// <summary>All guardians of the given students (primary first). Students with no guardian are simply absent.</summary>
        Task<List<IdCardGuardianRow>> GetGuardiansAsync(string schoolId, IReadOnlyCollection<string> studentIds, CancellationToken ct);
        /// <summary>Blood group names by id (a tiny global lookup).</summary>
        Task<Dictionary<int, string>> GetBloodGroupNamesAsync(CancellationToken ct);
        /// <summary>City names for the given students' addresses, keyed by (country, state, district, city) ids.</summary>
        Task<Dictionary<(int, int, int, int), string>> GetCityNamesAsync(IReadOnlyCollection<int> cityIds, CancellationToken ct);
        /// <summary>Id of the lut_status row "Active" of type "general status" (resolved by name, never hardcoded); null if the lookup is missing.</summary>
        Task<int?> GetActiveStatusIdAsync(CancellationToken ct);
    }
}
