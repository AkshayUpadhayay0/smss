using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record AssessmentTypeRow(long AssessmentTypeId, string TypeName, string TypeCode, bool IsActive);

    /// <summary>A test with the names the list needs, and how many students have a row / were absent.</summary>
    public record AssessmentRow(
        long AssessmentId, long AcademicYearId, string AcademicYearName, long ClassId, string ClassName, long SectionId, string SectionName, long SubjectId, string SubjectName,
        long AssessmentTypeId, string TypeName, string EmployeeId, string FirstName, string? MiddleName, string LastName, string Title, DateOnly TestDate, decimal MaxMarks, int StatusId,
        int StudentCount, int AbsentCount);

    /// <summary>One stored mark with the student's name (and roll number in the test's academic year).</summary>
    public record AssessmentMarkRow(string StudentId, string FirstName, string? MiddleName, string LastName, string? RollNumber, decimal? ObtainedMarks, bool IsAbsent);

    /// <summary>A student's own result in one test.</summary>
    public record StudentResultRow(long AssessmentId, string Title, string TypeName, string SubjectName, DateOnly TestDate, decimal MaxMarks, decimal? ObtainedMarks, bool IsAbsent);

    /// <summary>Optional filters shared by the lists (dates filter on the test date, inclusive). Inactive (deleted) tests are never returned.</summary>
    public record AssessmentFilter(long? AcademicYearId = null, long? SectionId = null, long? SubjectId = null, string? EmployeeId = null, DateOnly? From = null, DateOnly? To = null);

    // Every method takes the school id: another school's tests, sections and people can never be reached through this repository.
    public interface IAssessmentRepository
    {
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        /// <summary>Transaction-scoped lock for one test, so two saves of its marks queue up instead of racing.</summary>
        Task LockAssessmentAsync(string schoolId, long assessmentId, CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);

        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct);
        Task<(string? LoginId, string? Name)> GetUserAsync(string schoolId, long userId, CancellationToken ct);
        Task<YearRef?> GetYearContainingAsync(string schoolId, DateOnly date, CancellationToken ct);
        Task<YearRef?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct);
        Task<SectionRef?> GetSectionAsync(string schoolId, long sectionId, CancellationToken ct);
        /// <summary>The subject with whether it is mapped (Active) to the class in Class-Subject Mapping.</summary>
        Task<HomeworkSubjectRow?> GetSubjectAsync(string schoolId, long subjectId, long classId, int activeStatusId, CancellationToken ct);
        Task<HomeworkTeacherRow?> GetEmployeeAsync(string schoolId, string employeeId, CancellationToken ct);
        Task<List<AssessmentTypeRow>> GetTypesAsync(CancellationToken ct);
        Task<AssessmentTypeRow?> GetTypeAsync(long assessmentTypeId, CancellationToken ct);
        /// <summary>The ACTIVE roster of a section for a year: an Active enrollment of an Active student in that section. Ordered by roll number then name.</summary>
        Task<List<RosterStudentRow>> GetRosterAsync(string schoolId, long academicYearId, long sectionId, int activeStatusId, CancellationToken ct);
        /// <summary>The section an Active student is enrolled in (Active enrollment) for the year, or null.</summary>
        Task<long?> GetStudentSectionAsync(string schoolId, string studentId, long academicYearId, int activeStatusId, CancellationToken ct);

        Task AddAssessmentAsync(TbClassAssessment assessment, CancellationToken ct);
        /// <summary>The Active test, tracked for update (null when missing, Inactive or in another school).</summary>
        Task<TbClassAssessment?> GetActiveForUpdateAsync(string schoolId, long assessmentId, int activeStatusId, CancellationToken ct);
        /// <summary>Every stored mark of a test, tracked (the whole-roster re-submit updates these rows in place).</summary>
        Task<List<TbStudentAssessmentMark>> GetMarksForUpdateAsync(long assessmentId, CancellationToken ct);
        Task AddMarksAsync(IEnumerable<TbStudentAssessmentMark> marks, CancellationToken ct);
        Task<AssessmentRow?> GetRowAsync(string schoolId, long assessmentId, CancellationToken ct);
        Task<List<AssessmentRow>> ListAsync(string schoolId, AssessmentFilter filter, int activeStatusId, CancellationToken ct);
        Task<List<AssessmentMarkRow>> GetMarkRowsAsync(string schoolId, long assessmentId, long academicYearId, CancellationToken ct);
        /// <summary>The student's own marks in Active tests of the given section and year.</summary>
        Task<List<StudentResultRow>> GetStudentResultsAsync(string schoolId, string studentId, long academicYearId, long sectionId, int activeStatusId, DateOnly? from, DateOnly? to, CancellationToken ct);
    }
}
