using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class EmployeeLeaveService : IEmployeeLeaveService
    {
        private readonly ILeaveRepository _repo;
        private readonly ILogger<EmployeeLeaveService> _logger;

        public EmployeeLeaveService(ILeaveRepository repo, ILogger<EmployeeLeaveService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        // ---------------------------------------------------------------- apply
        public async Task<ApiResponse<EmployeeLeaveDto>> ApplyAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, ApplyEmployeeLeaveRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<EmployeeLeaveDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var dateError = LeaveRules.ValidateDates(req.FromDate!.Value, req.ToDate!.Value);
                if (dateError != null) return Result<EmployeeLeaveDto>(400, dateError, null);
                var reason = req.Reason!.Trim();
                if (reason.Length == 0) return Result<EmployeeLeaveDto>(400, "A reason is required.", null);

                var (loginId, _) = await _repo.GetUserAsync(schoolId, callerUserId, ct);
                var named = !string.IsNullOrWhiteSpace(req.EmployeeId);
                var target = named ? req.EmployeeId!.Trim() : loginId;
                if (!callerIsSchoolAdmin && !string.Equals(target, loginId, StringComparison.Ordinal))
                    return Result<EmployeeLeaveDto>(403, "You can only apply for leave for yourself. A School Admin can apply on your behalf.", null);
                if (string.IsNullOrEmpty(target)) return Result<EmployeeLeaveDto>(400, "Choose the employee to apply for.", null);

                var employee = await _repo.GetEmployeeAsync(schoolId, target, ct);
                if (employee == null)
                    return Result<EmployeeLeaveDto>(400, named ? "The selected employee was not found for your school." : "Your account is not linked to an employee record, so choose the employee to apply for.", null);
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<EmployeeLeaveDto>();
                if (employee.StatusId != activeId || employee.EmploymentBlocksLogin)
                    return Result<EmployeeLeaveDto>(400, "This employee is not active, so leave can't be requested for them.", null);

                var type = await _repo.GetLeaveTypeAsync(req.LeaveTypeId!.Value, ct);
                if (type == null || !type.IsActive || !type.AppliesToEmployee) return Result<EmployeeLeaveDto>(400, "The selected leave type is not available for employees.", null);

                var now = DateTime.UtcNow;
                var entity = new TbEmployeeLeaveRequest
                {
                    SchoolId = schoolId, EmployeeId = employee.EmployeeId, LeaveTypeId = type.LeaveTypeId, FromDate = req.FromDate.Value, ToDate = req.ToDate.Value, Reason = reason,
                    Status = LeaveStatuses.Pending, AppliedBy = callerUserId, AppliedAt = now, CreatedAt = now, UpdatedAt = now,
                };
                await _repo.AddEmployeeRequestAsync(entity, ct);
                await _repo.SaveChangesAsync(ct);
                return await ReplyAsync(schoolId, entity.LeaveRequestId, 201, "Leave request submitted.", ct);
            }
            catch (Exception ex) { return Error<EmployeeLeaveDto>(ex, "applying for employee leave"); }
        }

        // ---------------------------------------------------------------- approve / reject
        public async Task<ApiResponse<EmployeeLeaveDto>> DecideAsync(string? schoolId, long callerUserId, bool callerCanApprove, long id, DecideLeaveRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<EmployeeLeaveDto>(400, TenantMessages.NoSchool, null);
            if (!callerCanApprove) return Result<EmployeeLeaveDto>(403, "Only a School Admin or a Principal can decide leave requests.", null);
            try
            {
                var bad = LeaveRules.ParseDecision(req, out var approve, out var reason);
                if (bad != null) return Result<EmployeeLeaveDto>(400, bad, null);

                var peek = await _repo.GetEmployeeRequestAsync(schoolId, id, false, ct);
                if (peek == null) return Result<EmployeeLeaveDto>(404, "Leave request not found.", null);
                var (loginId, _) = await _repo.GetUserAsync(schoolId, callerUserId, ct);
                if (string.Equals(loginId, peek.EmployeeId, StringComparison.Ordinal))
                    return Result<EmployeeLeaveDto>(403, "You can't decide your own leave request.", null);

                try
                {
                    await using var tx = await _repo.BeginTransactionAsync(ct);
                    await _repo.LockPersonAsync("employee", schoolId, peek.EmployeeId, ct);   // decisions and cancels for one person queue up
                    var request = await _repo.GetEmployeeRequestAsync(schoolId, id, true, ct);   // re-read under the lock
                    if (request == null) return Result<EmployeeLeaveDto>(404, "Leave request not found.", null);
                    if (request.Status != LeaveStatuses.Pending)
                        return Result<EmployeeLeaveDto>(409, $"This request is already {request.Status.ToLowerInvariant()}; only a pending request can be decided.", null);

                    if (approve)
                    {
                        var clash = await _repo.FindApprovedEmployeeOverlapAsync(schoolId, request.EmployeeId, request.FromDate, request.ToDate, request.LeaveRequestId, ct);
                        if (clash != null) return Result<EmployeeLeaveDto>(409, OverlapMessage(clash), null);
                    }

                    var now = DateTime.UtcNow;
                    request.Status = approve ? LeaveStatuses.Approved : LeaveStatuses.Rejected;
                    request.ApprovedBy = callerUserId; request.ApprovedAt = now; request.DecisionReason = reason; request.UpdatedAt = now;
                    await _repo.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: LeaveRules.ExclusionViolation })
                {
                    return Result<EmployeeLeaveDto>(409, "This overlaps another approved leave of the same employee.", null);   // the database backstop
                }
                return await ReplyAsync(schoolId, id, 200, approve ? "Leave approved." : "Leave rejected.", ct);
            }
            catch (Exception ex) { return Error<EmployeeLeaveDto>(ex, "deciding employee leave"); }
        }

        // ---------------------------------------------------------------- cancel
        public async Task<ApiResponse<EmployeeLeaveDto>> CancelAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, long id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<EmployeeLeaveDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var peek = await _repo.GetEmployeeRequestAsync(schoolId, id, false, ct);
                if (peek == null) return Result<EmployeeLeaveDto>(404, "Leave request not found.", null);
                var (loginId, _) = await _repo.GetUserAsync(schoolId, callerUserId, ct);
                // the applicant: the employee it is for, or whoever recorded it (a School Admin applying on their behalf)
                var isApplicant = string.Equals(loginId, peek.EmployeeId, StringComparison.Ordinal) || peek.AppliedBy == callerUserId;
                if (!callerIsSchoolAdmin && !isApplicant) return Result<EmployeeLeaveDto>(403, "Only the applicant or a School Admin can cancel a leave request.", null);

                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockPersonAsync("employee", schoolId, peek.EmployeeId, ct);
                var request = await _repo.GetEmployeeRequestAsync(schoolId, id, true, ct);
                if (request == null) return Result<EmployeeLeaveDto>(404, "Leave request not found.", null);
                if (request.Status != LeaveStatuses.Pending)
                    return Result<EmployeeLeaveDto>(409, $"Only a pending request can be cancelled; this one is already {request.Status.ToLowerInvariant()}.", null);
                request.Status = LeaveStatuses.Cancelled; request.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return await ReplyAsync(schoolId, id, 200, "Leave request cancelled.", ct);
            }
            catch (Exception ex) { return Error<EmployeeLeaveDto>(ex, "cancelling employee leave"); }
        }

        // ---------------------------------------------------------------- reads
        public async Task<ApiResponse<List<EmployeeLeaveDto>>> GetMyAsync(string? schoolId, long callerUserId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<EmployeeLeaveDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var (loginId, _) = await _repo.GetUserAsync(schoolId, callerUserId, ct);
                var employee = string.IsNullOrEmpty(loginId) ? null : await _repo.GetEmployeeAsync(schoolId, loginId, ct);
                if (employee == null) return Result<List<EmployeeLeaveDto>>(404, "Your account is not linked to an employee record.", null);
                var rows = await _repo.ListEmployeeRowsAsync(schoolId, employee.EmployeeId, null, ct);
                return Result(200, "Successfully fetched", rows.Select(LeaveRules.ToEmployeeDto).ToList());
            }
            catch (Exception ex) { return Error<List<EmployeeLeaveDto>>(ex, "fetching my leave requests"); }
        }

        public async Task<ApiResponse<List<EmployeeLeaveDto>>> ListAsync(string? schoolId, bool callerCanApprove, string? status, string? employeeId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<EmployeeLeaveDto>>(400, TenantMessages.NoSchool, null);
            if (!callerCanApprove) return Result<List<EmployeeLeaveDto>>(403, "Only a School Admin or a Principal can see all leave requests.", null);
            try
            {
                if (!LeaveRules.TryNormalizeStatus(status, out var normalized, out var error)) return Result<List<EmployeeLeaveDto>>(400, error!, null);
                var rows = await _repo.ListEmployeeRowsAsync(schoolId, string.IsNullOrWhiteSpace(employeeId) ? null : employeeId.Trim(), normalized, ct);
                return Result(200, "Successfully fetched", rows.Select(LeaveRules.ToEmployeeDto).ToList());
            }
            catch (Exception ex) { return Error<List<EmployeeLeaveDto>>(ex, "listing employee leave requests"); }
        }

        // ---------------------------------------------------------------- helpers
        private static string OverlapMessage(LeaveApprovedRange clash) =>
            $"This overlaps an approved {clash.TypeName} for the same employee ({LeaveRules.Range(clash.FromDate, clash.ToDate)}).";

        private async Task<ApiResponse<EmployeeLeaveDto>> ReplyAsync(string schoolId, long id, int code, string message, CancellationToken ct)
        {
            var row = await _repo.GetEmployeeRowAsync(schoolId, id, ct);
            return row == null ? Result<EmployeeLeaveDto>(500, "Something went wrong. Please try again later.", null) : Result(code, message, LeaveRules.ToEmployeeDto(row));
        }

        private ApiResponse<T> ConfigError<T>()
        {
            _logger.LogError("Configuration problem: Active status is missing in lut_status");
            return Result<T>(500, "The system is not configured correctly. Please contact support.", default);
        }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        private ApiResponse<T> Error<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", default);
        }
    }
}
