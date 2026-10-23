using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record LeaveTypeRow(long LeaveTypeId, string TypeName, string TypeCode, bool AppliesToEmployee, bool AppliesToStudent, bool IsActive);
    public record LeaveEmployeeRow(string EmployeeId, string FirstName, string? MiddleName, string LastName, int StatusId, bool EmploymentBlocksLogin);
    public record LeaveStudentRow(string StudentId, string FirstName, string? MiddleName, string LastName, int StatusId);
    public record LeaveApprovedRange(DateOnly FromDate, DateOnly ToDate, string TypeName);

    /// <summary>A request with everything the list / detail needs. Student rows fill the class / section of their CURRENT enrollment.</summary>
    public record LeaveRow(
        long LeaveRequestId, string PersonId, string FirstName, string? MiddleName, string LastName,
        long LeaveTypeId, string LeaveTypeName, DateOnly FromDate, DateOnly ToDate, string Reason, string Status,
        long AppliedBy, string? AppliedByName, DateTime AppliedAt, long? ApprovedBy, string? ApprovedByName, DateTime? ApprovedAt, string? DecisionReason,
        long? SectionId, string? ClassName, string? SectionName);

    // Every method takes the school id: another school's people and requests can never be reached through this repository.
    public interface ILeaveRepository
    {
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        /// <summary>Transaction-scoped lock for one person (kind "employee" / "student"), so a decision, a cancel and another decision for them queue up.</summary>
        Task LockPersonAsync(string kind, string schoolId, string personId, CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);

        Task<List<LeaveTypeRow>> GetLeaveTypesAsync(CancellationToken ct);
        Task<LeaveTypeRow?> GetLeaveTypeAsync(long leaveTypeId, CancellationToken ct);
        Task<(string? LoginId, string? Name)> GetUserAsync(string schoolId, long userId, CancellationToken ct);
        Task<long?> GetYearIdContainingAsync(string schoolId, DateOnly date, CancellationToken ct);
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct);

        Task<LeaveEmployeeRow?> GetEmployeeAsync(string schoolId, string employeeId, CancellationToken ct);
        Task<LeaveStudentRow?> GetStudentAsync(string schoolId, string studentId, CancellationToken ct);
        /// <summary>The section the student is enrolled in for the year (any enrollment status), or null.</summary>
        Task<long?> GetStudentSectionAsync(string schoolId, string studentId, long academicYearId, CancellationToken ct);
        /// <summary>The sections the employee holds an Active class-teacher assignment for in the year.</summary>
        Task<List<long>> GetClassTeacherSectionIdsAsync(string schoolId, string employeeId, long academicYearId, int activeStatusId, CancellationToken ct);

        // ---- employee requests
        Task AddEmployeeRequestAsync(TbEmployeeLeaveRequest request, CancellationToken ct);
        Task<TbEmployeeLeaveRequest?> GetEmployeeRequestAsync(string schoolId, long id, bool tracked, CancellationToken ct);
        Task<LeaveApprovedRange?> FindApprovedEmployeeOverlapAsync(string schoolId, string employeeId, DateOnly fromDate, DateOnly toDate, long excludeId, CancellationToken ct);
        Task<LeaveRow?> GetEmployeeRowAsync(string schoolId, long id, CancellationToken ct);
        Task<List<LeaveRow>> ListEmployeeRowsAsync(string schoolId, string? employeeId, string? status, CancellationToken ct);

        // ---- student requests
        Task AddStudentRequestAsync(TbStudentLeaveRequest request, CancellationToken ct);
        Task<TbStudentLeaveRequest?> GetStudentRequestAsync(string schoolId, long id, bool tracked, CancellationToken ct);
        Task<LeaveApprovedRange?> FindApprovedStudentOverlapAsync(string schoolId, string studentId, DateOnly fromDate, DateOnly toDate, long excludeId, CancellationToken ct);
        Task<LeaveRow?> GetStudentRowAsync(string schoolId, long id, long? academicYearId, CancellationToken ct);
        /// <summary>Student requests, newest first. <paramref name="sectionIds"/> (null = every section) filters by the student's section in <paramref name="academicYearId"/>.</summary>
        Task<List<LeaveRow>> ListStudentRowsAsync(string schoolId, long? academicYearId, IReadOnlyCollection<long>? sectionIds, string? status, CancellationToken ct);
    }
}
