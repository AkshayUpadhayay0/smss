using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record YearRef(long AcademicYearId, string YearName);
    public record SectionRef(long SectionId, long ClassId, string SectionName, string ClassName, short ClassSequenceOrder, int? SectionStatusId, int? ClassStatusId);
    public record AssignmentRow(TbClassTeacherAssignment Assignment, string ClassName, string SectionName, string EmployeeName, string StatusName);
    public record SectionAssignmentRow(long SectionId, long ClassId, string SectionName, string ClassName, short ClassSequenceOrder,
        long? AssignmentId, string? EmployeeId, string? EmployeeName);
    public record EligibleTeacherRow(string EmployeeId, string FirstName, string? MiddleName, string LastName, string DesignationName);

    // Every method takes the school id: another school's sections, employees and assignments can never be reached through this repository.
    public interface IClassTeacherRepository
    {
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        /// <summary>Transaction-scoped lock for one section-year, so two assign / reassign calls on it queue up instead of racing.</summary>
        Task LockSectionYearAsync(string schoolId, long academicYearId, long sectionId, CancellationToken ct);

        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct);
        Task<YearRef?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct);
        Task<YearRef?> GetCurrentYearAsync(string schoolId, CancellationToken ct);
        Task<SectionRef?> GetSectionAsync(string schoolId, long sectionId, CancellationToken ct);

        /// <summary>
        /// Employees who can be a class teacher: an Active record, employee_type Teaching, an employment status that does not block login, AND an
        /// Active login holding the given role (by role_code) whose role is itself Active.
        /// </summary>
        Task<List<EligibleTeacherRow>> GetEligibleTeachersAsync(string schoolId, int activeStatusId, string teacherRoleCode, CancellationToken ct);
        Task<bool> IsEligibleTeacherAsync(string schoolId, string employeeId, int activeStatusId, string teacherRoleCode, CancellationToken ct);

        /// <summary>The Active assignment of a section-year, TRACKED (to deactivate it).</summary>
        Task<TbClassTeacherAssignment?> GetActiveForUpdateAsync(string schoolId, long academicYearId, long sectionId, int activeStatusId, CancellationToken ct);
        Task AddAsync(TbClassTeacherAssignment assignment, CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);

        Task<AssignmentRow?> GetAssignmentRowAsync(string schoolId, long assignmentId, CancellationToken ct);
        Task<AssignmentRow?> GetActiveRowAsync(string schoolId, long academicYearId, long sectionId, int activeStatusId, CancellationToken ct);
        /// <summary>Every Active section of an Active class with its Active class teacher, or none.</summary>
        Task<List<SectionAssignmentRow>> GetSectionAssignmentsAsync(string schoolId, long academicYearId, int activeStatusId, CancellationToken ct);
        Task<List<AssignmentRow>> GetHistoryAsync(string schoolId, long academicYearId, long sectionId, CancellationToken ct);

        /// <summary>The login id (employee id) of a user, or null.</summary>
        Task<string?> GetUserLoginIdAsync(string schoolId, long userId, CancellationToken ct);
    }
}
