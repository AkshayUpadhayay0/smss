using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    /// <summary>One student of a class-section for a year (enrollment fields included).</summary>
    public record StudentListRow(long StudentId, string AdmissionNumber, string FirstName, string? MiddleName, string LastName,
        string? RollNumber, int? GenderId, int StatusId, long EnrollmentId, string? ProfilePhotoUrl);

    /// <summary>A student's enrollment joined to its year, class and section.</summary>
    public record StudentEnrollmentRow(long EnrollmentId, string? RollNumber, int StatusId, long AcademicYearId, string AcademicYearName,
        DateOnly AcademicYearStart, bool AcademicYearIsCurrent, string ClassName, string SectionName);

    public record StudentGuardianRow(long GuardianId, string FirstName, string? LastName, string Relationship, string MobileNumber,
        string? Email, bool IsPrimary, bool IsEmergencyContact, bool CanPickupStudent);

    // Every method takes the school id: there is no way to read another school's students through this repository.
    public interface IStudentRepository
    {
        Task<bool> AcademicYearBelongsToSchoolAsync(string schoolId, long academicYearId, CancellationToken ct);
        Task<bool> SectionBelongsToSchoolAsync(string schoolId, long sectionId, CancellationToken ct);

        Task<List<StudentListRow>> GetEnrolledStudentsAsync(string schoolId, long academicYearId, long sectionId, CancellationToken ct);
        Task<TbStudent?> GetStudentAsync(string schoolId, long studentId, CancellationToken ct);
        /// <summary>The student, tracked, for changing their photo.</summary>
        Task<TbStudent?> GetStudentForUpdateAsync(string schoolId, long studentId, CancellationToken ct);
        Task<string?> GetSchoolCodeAsync(string schoolId, CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);
        Task<List<StudentEnrollmentRow>> GetEnrollmentsAsync(string schoolId, long studentId, CancellationToken ct);
        Task<List<StudentGuardianRow>> GetGuardiansAsync(string schoolId, long studentId, CancellationToken ct);

        // display names
        Task<Dictionary<int, string>> GetGeneralStatusNamesAsync(CancellationToken ct);
        Task<Dictionary<long, string>> GetGenderNamesAsync(CancellationToken ct);
        Task<string?> GetBloodGroupNameAsync(long id, CancellationToken ct);
        Task<string?> GetStudentCategoryNameAsync(long id, CancellationToken ct);
        Task<string?> GetAdmissionTypeNameAsync(string schoolId, long id, CancellationToken ct);
    }
}
