using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record HomeworkTeacherRow(string EmployeeId, string FirstName, string? MiddleName, string LastName, int StatusId, string EmployeeType, bool EmploymentBlocksLogin);
    /// <summary>MappedToClass: the subject is mapped to the class in Class-Subject Mapping with an Active mapping.</summary>
    public record HomeworkSubjectRow(long SubjectId, string SubjectName, int? StatusId, bool MappedToClass);
    public record HomeworkSchoolRow(string SchoolCode);

    /// <summary>A homework row with the names the list needs.</summary>
    public record HomeworkRow(
        long HomeworkId, long AcademicYearId, string AcademicYearName, long ClassId, string ClassName, long SectionId, string SectionName, long SubjectId, string SubjectName,
        string EmployeeId, string FirstName, string? MiddleName, string LastName, string Title, string? Description, DateOnly AssignedDate, DateOnly DueDate,
        string? AttachmentPath, int StatusId);

    /// <summary>Optional filters shared by the lists. Dates filter on the DUE date, inclusive. Inactive (deleted) homework is never returned.</summary>
    public record HomeworkFilter(long? AcademicYearId = null, long? SectionId = null, long? SubjectId = null, string? EmployeeId = null, DateOnly? From = null, DateOnly? To = null);

    // Every method takes the school id: another school's homework, sections and people can never be reached through this repository.
    public interface IHomeworkRepository
    {
        Task<int> SaveChangesAsync(CancellationToken ct);
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct);
        Task<(string? LoginId, string? Name)> GetUserAsync(string schoolId, long userId, CancellationToken ct);
        Task<HomeworkSchoolRow?> GetSchoolAsync(string schoolId, CancellationToken ct);
        /// <summary>The academic year whose dates contain <paramref name="date"/> (the current year first if they overlap), or null.</summary>
        Task<YearRef?> GetYearContainingAsync(string schoolId, DateOnly date, CancellationToken ct);
        Task<YearRef?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct);
        Task<SectionRef?> GetSectionAsync(string schoolId, long sectionId, CancellationToken ct);
        Task<HomeworkSubjectRow?> GetSubjectAsync(string schoolId, long subjectId, long classId, int activeStatusId, CancellationToken ct);
        Task<HomeworkTeacherRow?> GetEmployeeAsync(string schoolId, string employeeId, CancellationToken ct);
        /// <summary>The section an Active student is enrolled in (Active enrollment) for the year, or null.</summary>
        Task<long?> GetStudentSectionAsync(string schoolId, string studentId, long academicYearId, int activeStatusId, CancellationToken ct);

        Task AddAsync(TbHomework homework, CancellationToken ct);
        /// <summary>The Active homework, tracked for update (null when missing, Inactive or in another school).</summary>
        Task<TbHomework?> GetActiveForUpdateAsync(string schoolId, long homeworkId, int activeStatusId, CancellationToken ct);
        Task<HomeworkRow?> GetRowAsync(string schoolId, long homeworkId, CancellationToken ct);
        Task<List<HomeworkRow>> ListAsync(string schoolId, HomeworkFilter filter, int activeStatusId, CancellationToken ct);
    }
}
