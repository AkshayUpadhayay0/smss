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
    public class StudentLeaveService : IStudentLeaveService
    {
        private readonly ILeaveRepository _repo;
        private readonly IClassTeacherService _classTeachers;
        private readonly ILogger<StudentLeaveService> _logger;

        public StudentLeaveService(ILeaveRepository repo, IClassTeacherService classTeachers, ILogger<StudentLeaveService> logger)
        {
            _repo = repo;
            _classTeachers = classTeachers;
            _logger = logger;
        }

        // ---------------------------------------------------------------- apply (recorded on the student's behalf)
        public async Task<ApiResponse<StudentLeaveDto>> ApplyAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, ApplyStudentLeaveRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<StudentLeaveDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var dateError = LeaveRules.ValidateDates(req.FromDate!.Value, req.ToDate!.Value);
                if (dateError != null) return Result<StudentLeaveDto>(400, dateError, null);
                var reason = req.Reason!.Trim();
                if (reason.Length == 0) return Result<StudentLeaveDto>(400, "A reason is required.", null);

                var studentId = req.StudentId!.Trim();
                var student = await _repo.GetStudentAsync(schoolId, studentId, ct);
                if (student == null) return Result<StudentLeaveDto>(400, "The selected student was not found for your school.", null);
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<StudentLeaveDto>();
                if (student.StatusId != activeId) return Result<StudentLeaveDto>(400, "This student is not active, so leave can't be requested for them.", null);

                var type = await _repo.GetLeaveTypeAsync(req.LeaveTypeId!.Value, ct);
                if (type == null || !type.IsActive || !type.AppliesToStudent) return Result<StudentLeaveDto>(400, "The selected leave type is not available for students.", null);

                var (allowed, yearId) = await MayHandleAsync(schoolId, callerUserId, callerIsSchoolAdmin, studentId, ct);
                if (!allowed) return Result<StudentLeaveDto>(403, "Only a School Admin or the student's class teacher can record a leave request for a student.", null);

                var now = DateTime.UtcNow;
                var entity = new TbStudentLeaveRequest
                {
                    SchoolId = schoolId, StudentId = student.StudentId, LeaveTypeId = type.LeaveTypeId, FromDate = req.FromDate.Value, ToDate = req.ToDate.Value, Reason = reason,
                    Status = LeaveStatuses.Pending, AppliedBy = callerUserId, AppliedAt = now, CreatedAt = now, UpdatedAt = now,
                };
                await _repo.AddStudentRequestAsync(entity, ct);
                await _repo.SaveChangesAsync(ct);
                return await ReplyAsync(schoolId, entity.LeaveRequestId, yearId, 201, "Leave request recorded.", ct);
            }
            catch (Exception ex) { return Error<StudentLeaveDto>(ex, "applying for student leave"); }
        }

        // ---------------------------------------------------------------- approve / reject
        public async Task<ApiResponse<StudentLeaveDto>> DecideAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, long id, DecideLeaveRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<StudentLeaveDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var bad = LeaveRules.ParseDecision(req, out var approve, out var reason);
                if (bad != null) return Result<StudentLeaveDto>(400, bad, null);

                var peek = await _repo.GetStudentRequestAsync(schoolId, id, false, ct);
                if (peek == null) return Result<StudentLeaveDto>(404, "Leave request not found.", null);
                var (allowed, yearId) = await MayHandleAsync(schoolId, callerUserId, callerIsSchoolAdmin, peek.StudentId, ct);
                if (!allowed) return Result<StudentLeaveDto>(403, "Only a School Admin or the student's class teacher can decide this leave request.", null);

                try
                {
                    await using var tx = await _repo.BeginTransactionAsync(ct);
                    await _repo.LockPersonAsync("student", schoolId, peek.StudentId, ct);
                    var request = await _repo.GetStudentRequestAsync(schoolId, id, true, ct);
                    if (request == null) return Result<StudentLeaveDto>(404, "Leave request not found.", null);
                    if (request.Status != LeaveStatuses.Pending)
                        return Result<StudentLeaveDto>(409, $"This request is already {request.Status.ToLowerInvariant()}; only a pending request can be decided.", null);

                    if (approve)
                    {
                        var clash = await _repo.FindApprovedStudentOverlapAsync(schoolId, request.StudentId, request.FromDate, request.ToDate, request.LeaveRequestId, ct);
                        if (clash != null) return Result<StudentLeaveDto>(409, $"This overlaps an approved {clash.TypeName} for the same student ({LeaveRules.Range(clash.FromDate, clash.ToDate)}).", null);
                    }

                    var now = DateTime.UtcNow;
                    request.Status = approve ? LeaveStatuses.Approved : LeaveStatuses.Rejected;
                    request.ApprovedBy = callerUserId; request.ApprovedAt = now; request.DecisionReason = reason; request.UpdatedAt = now;
                    await _repo.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: LeaveRules.ExclusionViolation })
                {
                    return Result<StudentLeaveDto>(409, "This overlaps another approved leave of the same student.", null);   // the database backstop
                }
                return await ReplyAsync(schoolId, id, yearId, 200, approve ? "Leave approved." : "Leave rejected.", ct);
            }
            catch (Exception ex) { return Error<StudentLeaveDto>(ex, "deciding student leave"); }
        }

        // ---------------------------------------------------------------- cancel
        public async Task<ApiResponse<StudentLeaveDto>> CancelAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, long id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<StudentLeaveDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var peek = await _repo.GetStudentRequestAsync(schoolId, id, false, ct);
                if (peek == null) return Result<StudentLeaveDto>(404, "Leave request not found.", null);
                var (allowed, yearId) = await MayHandleAsync(schoolId, callerUserId, callerIsSchoolAdmin, peek.StudentId, ct);
                if (!allowed) return Result<StudentLeaveDto>(403, "Only a School Admin or the student's class teacher can cancel this leave request.", null);

                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockPersonAsync("student", schoolId, peek.StudentId, ct);
                var request = await _repo.GetStudentRequestAsync(schoolId, id, true, ct);
                if (request == null) return Result<StudentLeaveDto>(404, "Leave request not found.", null);
                if (request.Status != LeaveStatuses.Pending)
                    return Result<StudentLeaveDto>(409, $"Only a pending request can be cancelled; this one is already {request.Status.ToLowerInvariant()}.", null);
                request.Status = LeaveStatuses.Cancelled; request.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return await ReplyAsync(schoolId, id, yearId, 200, "Leave request cancelled.", ct);
            }
            catch (Exception ex) { return Error<StudentLeaveDto>(ex, "cancelling student leave"); }
        }

        // ---------------------------------------------------------------- queue
        public async Task<ApiResponse<List<StudentLeaveDto>>> ListAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, long? sectionId, string? status, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<StudentLeaveDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                if (!LeaveRules.TryNormalizeStatus(status, out var normalized, out var error)) return Result<List<StudentLeaveDto>>(400, error!, null);
                var yearId = await _repo.GetYearIdContainingAsync(schoolId, SchoolClock.Today(), ct);

                IReadOnlyCollection<long>? sectionIds;
                if (callerIsSchoolAdmin)
                    sectionIds = sectionId == null ? null : new[] { sectionId.Value };
                else
                {
                    // a class teacher sees the queue of their own section(s) only; anyone who is not staff sees nothing
                    var (loginId, _) = await _repo.GetUserAsync(schoolId, callerUserId, ct);
                    var employee = string.IsNullOrEmpty(loginId) ? null : await _repo.GetEmployeeAsync(schoolId, loginId, ct);
                    if (employee == null) return Result<List<StudentLeaveDto>>(403, "Only a School Admin or a class teacher can see student leave requests.", null);
                    if (yearId == null) return Result(200, "Successfully fetched", new List<StudentLeaveDto>());

                    if (sectionId != null)
                    {
                        if (!await IsClassTeacherAsync(schoolId, employee.EmployeeId, sectionId.Value, yearId.Value, ct))
                            return Result<List<StudentLeaveDto>>(403, "You can only see the leave requests of the section you are the class teacher of.", null);
                        sectionIds = new[] { sectionId.Value };
                    }
                    else
                    {
                        var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                        if (activeId == null) return ConfigError<List<StudentLeaveDto>>();
                        var mine = new List<long>();
                        foreach (var sid in await _repo.GetClassTeacherSectionIdsAsync(schoolId, employee.EmployeeId, yearId.Value, activeId.Value, ct))
                            if (await IsClassTeacherAsync(schoolId, employee.EmployeeId, sid, yearId.Value, ct)) mine.Add(sid);   // drops sections of a no-longer-eligible teacher
                        sectionIds = mine;
                    }
                }

                var rows = await _repo.ListStudentRowsAsync(schoolId, yearId, sectionIds, normalized, ct);
                return Result(200, "Successfully fetched", rows.Select(LeaveRules.ToStudentDto).ToList());
            }
            catch (Exception ex) { return Error<List<StudentLeaveDto>>(ex, "listing student leave requests"); }
        }

        // ---------------------------------------------------------------- the student's own view
        public async Task<ApiResponse<List<StudentLeaveDto>>> GetMyAsync(string? schoolId, long callerUserId, string? status, DateOnly? from, DateOnly? to, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<StudentLeaveDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                if (!LeaveRules.TryNormalizeStatus(status, out var normalized, out var error)) return Result<List<StudentLeaveDto>>(400, error!, null);
                if (from != null && to != null && to < from) return Result<List<StudentLeaveDto>>(400, "'to' cannot be before 'from'.", null);
                // the caller's login id IS their student id; a login that is not a student record has nothing to show
                var (loginId, _) = await _repo.GetUserAsync(schoolId, callerUserId, ct);
                var student = string.IsNullOrEmpty(loginId) ? null : await _repo.GetStudentAsync(schoolId, loginId, ct);
                if (student == null) return Result<List<StudentLeaveDto>>(403, "Your account is not linked to a student record.", null);
                var yearId = await _repo.GetYearIdContainingAsync(schoolId, SchoolClock.Today(), ct);
                var rows = await _repo.ListStudentOwnRowsAsync(schoolId, student.StudentId, yearId, normalized, from, to, ct);
                return Result(200, "Successfully fetched", rows.Select(LeaveRules.ToStudentDto).ToList());
            }
            catch (Exception ex) { return Error<List<StudentLeaveDto>>(ex, "listing a student's own leave requests"); }
        }

        // ---------------------------------------------------------------- helpers
        /// <summary>
        /// May the caller act on this student's leave: a School Admin, or the Active class teacher of the section the student is in during the
        /// school's current academic year. Also returns that year (null when none covers today) for the section details of the reply.
        /// </summary>
        private async Task<(bool Allowed, long? YearId)> MayHandleAsync(string schoolId, long callerUserId, bool callerIsSchoolAdmin, string studentId, CancellationToken ct)
        {
            var yearId = await _repo.GetYearIdContainingAsync(schoolId, SchoolClock.Today(), ct);
            if (callerIsSchoolAdmin) return (true, yearId);
            if (yearId == null) return (false, null);
            var sectionId = await _repo.GetStudentSectionAsync(schoolId, studentId, yearId.Value, ct);
            if (sectionId == null) return (false, yearId);
            var (loginId, _) = await _repo.GetUserAsync(schoolId, callerUserId, ct);
            if (string.IsNullOrEmpty(loginId)) return (false, yearId);
            return (await IsClassTeacherAsync(schoolId, loginId, sectionId.Value, yearId.Value, ct), yearId);
        }

        private async Task<bool> IsClassTeacherAsync(string schoolId, string employeeId, long sectionId, long academicYearId, CancellationToken ct)
        {
            var answer = await _classTeachers.IsClassTeacherAsync(schoolId, employeeId, sectionId, academicYearId, ct);
            return answer.Status && answer.Data?.IsClassTeacher == true;
        }

        private async Task<ApiResponse<StudentLeaveDto>> ReplyAsync(string schoolId, long id, long? yearId, int code, string message, CancellationToken ct)
        {
            var row = await _repo.GetStudentRowAsync(schoolId, id, yearId, ct);
            return row == null ? Result<StudentLeaveDto>(500, "Something went wrong. Please try again later.", null) : Result(code, message, LeaveRules.ToStudentDto(row));
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
