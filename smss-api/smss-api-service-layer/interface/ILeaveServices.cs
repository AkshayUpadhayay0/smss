using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId is the caller's own school from the token; null / empty answers 400. The role booleans are decided by the controller from the token.
    public interface ILeaveTypeService
    {
        /// <summary>Active leave types, optionally only those for "employee" or "student" applicants.</summary>
        Task<ApiResponse<List<LeaveTypeDto>>> GetAsync(string? appliesTo, CancellationToken ct);
    }

    public interface IEmployeeLeaveService
    {
        /// <summary>A Pending request. The caller applies for themselves; a School Admin may name any employee.</summary>
        Task<ApiResponse<EmployeeLeaveDto>> ApplyAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, ApplyEmployeeLeaveRequest req, CancellationToken ct);
        /// <summary>Approve (409 when it overlaps an Approved leave of the same employee) or reject (needs a reason). Pending requests only.</summary>
        Task<ApiResponse<EmployeeLeaveDto>> DecideAsync(string? schoolId, long callerUserId, bool callerCanApprove, long id, DecideLeaveRequest req, CancellationToken ct);
        /// <summary>The employee, whoever applied, or a School Admin; Pending requests only.</summary>
        Task<ApiResponse<EmployeeLeaveDto>> CancelAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, long id, CancellationToken ct);
        Task<ApiResponse<List<EmployeeLeaveDto>>> GetMyAsync(string? schoolId, long callerUserId, CancellationToken ct);
        Task<ApiResponse<List<EmployeeLeaveDto>>> ListAsync(string? schoolId, bool callerCanApprove, string? status, string? employeeId, CancellationToken ct);
    }

    public interface IStudentLeaveService
    {
        /// <summary>A Pending request recorded on the student's behalf by a School Admin or the student's current class teacher.</summary>
        Task<ApiResponse<StudentLeaveDto>> ApplyAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, ApplyStudentLeaveRequest req, CancellationToken ct);
        Task<ApiResponse<StudentLeaveDto>> DecideAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, long id, DecideLeaveRequest req, CancellationToken ct);
        Task<ApiResponse<StudentLeaveDto>> CancelAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, long id, CancellationToken ct);
        /// <summary>A School Admin: every request (optionally one section). A class teacher: only their own section(s).</summary>
        Task<ApiResponse<List<StudentLeaveDto>>> ListAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, long? sectionId, string? status, CancellationToken ct);
    }
}
