using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    /// <summary>One student of a class-section for a year (enrollment fields included).</summary>
    public record StudentListRow(string StudentId, string AdmissionNumber, string FirstName, string? MiddleName, string LastName,
        string? RollNumber, int? GenderId, int StatusId, long EnrollmentId, string? ProfilePhotoUrl);

    /// <summary>One student's installments for one academic year, added up (see IStudentRepository.GetFeeAggregatesAsync).</summary>
    public record StudentFeeAggregate(string StudentId, decimal TotalDue, decimal TotalPaid, int OverdueCount);

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
        /// <summary>
        /// ONE grouped query for all the given students: each one's installments of that academic year (every fee head), summed, plus how many are
        /// overdue (still owed and due before <paramref name="today"/>). Students with no fee items at all are simply absent from the result (a student with items but no installments is present with zero totals).
        /// </summary>
        Task<Dictionary<string, StudentFeeAggregate>> GetFeeAggregatesAsync(string schoolId, long academicYearId, IReadOnlyCollection<string> studentIds, DateOnly today, CancellationToken ct);
        Task<TbStudent?> GetStudentAsync(string schoolId, string studentId, CancellationToken ct);
        /// <summary>The student, tracked, for changing their photo.</summary>
        Task<TbStudent?> GetStudentForUpdateAsync(string schoolId, string studentId, CancellationToken ct);
        Task<string?> GetSchoolCodeAsync(string schoolId, CancellationToken ct);
        /// <summary>The student's login id (their own student id, as tb_users.org_user_id), or null when no login exists yet.</summary>
        Task<string?> GetStudentLoginIdAsync(string schoolId, string studentId, CancellationToken ct);
        /// <summary>The login id (org_user_id) of a user of this school; for a student's login it IS their student id.</summary>
        Task<string?> GetUserLoginIdAsync(string schoolId, long userId, CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);
        Task<List<StudentEnrollmentRow>> GetEnrollmentsAsync(string schoolId, string studentId, CancellationToken ct);
        Task<List<StudentGuardianRow>> GetGuardiansAsync(string schoolId, string studentId, CancellationToken ct);

        // display names
        Task<Dictionary<int, string>> GetGeneralStatusNamesAsync(CancellationToken ct);
        Task<Dictionary<long, string>> GetGenderNamesAsync(CancellationToken ct);
        Task<string?> GetBloodGroupNameAsync(long id, CancellationToken ct);
        Task<string?> GetStudentCategoryNameAsync(long id, CancellationToken ct);
        Task<string?> GetAdmissionTypeNameAsync(string schoolId, long id, CancellationToken ct);
    }
}
