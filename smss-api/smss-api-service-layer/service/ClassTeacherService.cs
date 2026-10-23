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
    public class ClassTeacherService : IClassTeacherService
    {
        private const string ActiveIndex = "uq_tb_cta_one_active_per_section";

        private readonly IClassTeacherRepository _repo;
        private readonly ILogger<ClassTeacherService> _logger;

        public ClassTeacherService(IClassTeacherRepository repo, ILogger<ClassTeacherService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        // ---------------------------------------------------------------- assign / reassign / unassign
        public async Task<ApiResponse<ClassTeacherAssignmentDto>> AssignAsync(string? schoolId, long assignedByUserId, AssignClassTeacherRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ClassTeacherAssignmentDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var ctx = await ResolveAsync(schoolId, req.AcademicYearId!.Value, req.SectionId!.Value, req.ClassId, ct);
                if (ctx.Error != null) return Result<ClassTeacherAssignmentDto>(ctx.Error.Value.Code, ctx.Error.Value.Message, null);
                var (activeId, inactiveId, section) = (ctx.ActiveId, ctx.InactiveId, ctx.Section!);

                var employeeId = req.EmployeeId!.Trim();
                if (!await _repo.IsEligibleTeacherAsync(schoolId, employeeId, activeId, RoleCodes.Teacher, ct))
                    return Result<ClassTeacherAssignmentDto>(400, "This employee can't be a class teacher. They need an active record, employee type Teaching, and an active login with the Teacher role.", null);

                long assignmentId;
                await using (var tx = await _repo.BeginTransactionAsync(ct))
                {
                    await _repo.LockSectionYearAsync(schoolId, req.AcademicYearId.Value, section.SectionId, ct);   // one change per section-year at a time

                    var current = await _repo.GetActiveForUpdateAsync(schoolId, req.AcademicYearId.Value, section.SectionId, activeId, ct);
                    if (current != null && current.EmployeeId == employeeId)
                        return await RowAsync(schoolId, current.AssignmentId, 200, "This employee is already the class teacher of this section.", ct);

                    var now = DateTime.UtcNow;
                    if (current != null)
                    {
                        // history, not deletion: the old row stays, marked Inactive. Saved first, so the one-active unique index is never violated.
                        current.StatusId = inactiveId;
                        current.UpdatedAt = now;
                        await _repo.SaveChangesAsync(ct);
                    }

                    var next = new TbClassTeacherAssignment
                    {
                        SchoolId = schoolId, AcademicYearId = req.AcademicYearId.Value, ClassId = section.ClassId, SectionId = section.SectionId,
                        EmployeeId = employeeId, StatusId = activeId, AssignedBy = assignedByUserId, CreatedAt = now, UpdatedAt = now
                    };
                    await _repo.AddAsync(next, ct);
                    await _repo.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    assignmentId = next.AssignmentId;
                }
                return await RowAsync(schoolId, assignmentId, 200, "Class teacher assigned.", ct);
            }
            catch (DbUpdateException ex) when ((ex.InnerException as PostgresException)?.ConstraintName == ActiveIndex)
            {
                // the database's one-active-row rule caught a race the lock did not (should not happen)
                return Result<ClassTeacherAssignmentDto>(409, "This section's class teacher was just changed by someone else. Please refresh and try again.", null);
            }
            catch (Exception ex) { return Error<ClassTeacherAssignmentDto>(ex, "assigning a class teacher"); }
        }

        public async Task<ApiResponse<ClassTeacherAssignmentDto>> UnassignAsync(string? schoolId, UnassignClassTeacherRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ClassTeacherAssignmentDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var ctx = await ResolveAsync(schoolId, req.AcademicYearId!.Value, req.SectionId!.Value, null, ct);
                if (ctx.Error != null) return Result<ClassTeacherAssignmentDto>(ctx.Error.Value.Code, ctx.Error.Value.Message, null);

                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockSectionYearAsync(schoolId, req.AcademicYearId.Value, req.SectionId.Value, ct);
                var current = await _repo.GetActiveForUpdateAsync(schoolId, req.AcademicYearId.Value, req.SectionId.Value, ctx.ActiveId, ct);
                if (current == null) return Result<ClassTeacherAssignmentDto>(404, "This section has no class teacher to remove.", null);
                current.StatusId = ctx.InactiveId;
                current.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return await RowAsync(schoolId, current.AssignmentId, 200, "Class teacher removed.", ct);
            }
            catch (Exception ex) { return Error<ClassTeacherAssignmentDto>(ex, "removing a class teacher"); }
        }

        // ---------------------------------------------------------------- reads
        public async Task<ApiResponse<ClassTeacherAssignmentDto>> GetCurrentAsync(string? schoolId, long academicYearId, long? classId, long sectionId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ClassTeacherAssignmentDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var ctx = await ResolveAsync(schoolId, academicYearId, sectionId, classId, ct, requireActiveSection: false);
                if (ctx.Error != null) return Result<ClassTeacherAssignmentDto>(ctx.Error.Value.Code, ctx.Error.Value.Message, null);
                var row = await _repo.GetActiveRowAsync(schoolId, academicYearId, sectionId, ctx.ActiveId, ct);
                return row == null ? Result<ClassTeacherAssignmentDto>(200, "This section has no class teacher yet.", null) : Result(200, "Successfully fetched", ToDto(row));
            }
            catch (Exception ex) { return Error<ClassTeacherAssignmentDto>(ex, "fetching a class teacher"); }
        }

        public async Task<ApiResponse<ClassTeacherListDto>> ListAsync(string? schoolId, long academicYearId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ClassTeacherListDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<ClassTeacherListDto>();
                var year = await _repo.GetYearAsync(schoolId, academicYearId, ct);
                if (year == null) return Result<ClassTeacherListDto>(400, "The selected academic year was not found for your school.", null);

                var rows = await _repo.GetSectionAssignmentsAsync(schoolId, academicYearId, activeId.Value, ct);
                var sections = rows.Select(r => new ClassTeacherSectionDto
                {
                    ClassId = r.ClassId, ClassName = r.ClassName, SectionId = r.SectionId, SectionName = r.SectionName,
                    AssignmentId = r.AssignmentId, EmployeeId = r.EmployeeId, EmployeeName = r.EmployeeName, HasClassTeacher = r.AssignmentId != null
                }).ToList();
                return Result(200, "Successfully fetched", new ClassTeacherListDto
                {
                    AcademicYearId = year.AcademicYearId, AcademicYearName = year.YearName, Sections = sections, UnassignedCount = sections.Count(s => !s.HasClassTeacher)
                });
            }
            catch (Exception ex) { return Error<ClassTeacherListDto>(ex, "listing class teachers"); }
        }

        public async Task<ApiResponse<List<ClassTeacherAssignmentDto>>> GetHistoryAsync(string? schoolId, long academicYearId, long sectionId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<ClassTeacherAssignmentDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var ctx = await ResolveAsync(schoolId, academicYearId, sectionId, null, ct, requireActiveSection: false);
                if (ctx.Error != null) return Result<List<ClassTeacherAssignmentDto>>(ctx.Error.Value.Code, ctx.Error.Value.Message, null);
                return Result(200, "Successfully fetched", (await _repo.GetHistoryAsync(schoolId, academicYearId, sectionId, ct)).Select(ToDto).ToList());
            }
            catch (Exception ex) { return Error<List<ClassTeacherAssignmentDto>>(ex, "fetching class teacher history"); }
        }

        public async Task<ApiResponse<List<EligibleTeacherDto>>> GetEligibleTeachersAsync(string? schoolId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<EligibleTeacherDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<List<EligibleTeacherDto>>();
                var rows = await _repo.GetEligibleTeachersAsync(schoolId, activeId.Value, RoleCodes.Teacher, ct);
                return Result(200, "Successfully fetched", rows.Select(r => new EligibleTeacherDto
                {
                    EmployeeId = r.EmployeeId, DesignationName = r.DesignationName,
                    FullName = string.Join(' ', new[] { r.FirstName, r.MiddleName, r.LastName }.Where(p => !string.IsNullOrWhiteSpace(p)))
                }).ToList());
            }
            catch (Exception ex) { return Error<List<EligibleTeacherDto>>(ex, "listing eligible class teachers"); }
        }

        // ---------------------------------------------------------------- the question Attendance asks
        public async Task<ApiResponse<ClassTeacherCheckDto>> IsClassTeacherAsync(string? schoolId, string employeeId, long sectionId, long? academicYearId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ClassTeacherCheckDto>(400, TenantMessages.NoSchool, null);
            if (string.IsNullOrWhiteSpace(employeeId)) return Result<ClassTeacherCheckDto>(400, "employeeId is required.", null);
            try
            {
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<ClassTeacherCheckDto>();

                var year = academicYearId != null ? await _repo.GetYearAsync(schoolId, academicYearId.Value, ct) : await _repo.GetCurrentYearAsync(schoolId, ct);
                if (year == null)
                    return Result<ClassTeacherCheckDto>(academicYearId != null ? 400 : 409, academicYearId != null ? "The selected academic year was not found for your school." : "The school has no current academic year.", null);
                if (await _repo.GetSectionAsync(schoolId, sectionId, ct) == null) return Result<ClassTeacherCheckDto>(404, "Section not found.", null);

                employeeId = employeeId.Trim();
                var row = await _repo.GetActiveRowAsync(schoolId, year.AcademicYearId, sectionId, activeId.Value, ct);
                // the assignment must be the Active one AND the person must still be a usable teacher (not terminated, login still on, still has the role)
                var yes = row != null && row.Assignment.EmployeeId == employeeId && await _repo.IsEligibleTeacherAsync(schoolId, employeeId, activeId.Value, RoleCodes.Teacher, ct);
                return Result(200, "Successfully fetched", new ClassTeacherCheckDto
                {
                    EmployeeId = employeeId, SectionId = sectionId, AcademicYearId = year.AcademicYearId, IsClassTeacher = yes, AssignmentId = yes ? row!.Assignment.AssignmentId : null
                });
            }
            catch (Exception ex) { return Error<ClassTeacherCheckDto>(ex, "checking a class teacher"); }
        }

        public async Task<ApiResponse<ClassTeacherCheckDto>> CheckAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, string employeeId, long sectionId, long? academicYearId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ClassTeacherCheckDto>(400, TenantMessages.NoSchool, null);
            if (!callerIsSchoolAdmin)
            {
                // a teacher may ask about themselves only: the employee id is their own login id
                var own = await _repo.GetUserLoginIdAsync(schoolId, callerUserId, ct);
                if (own == null || !string.Equals(own, employeeId?.Trim(), StringComparison.Ordinal))
                    return Result<ClassTeacherCheckDto>(403, "You can only check your own class teacher assignment.", null);
            }
            return await IsClassTeacherAsync(schoolId, employeeId ?? string.Empty, sectionId, academicYearId, ct);
        }

        // ---------------------------------------------------------------- helpers
        private record Resolved(int ActiveId, int InactiveId, SectionRef? Section, (int Code, string Message)? Error);

        /// <summary>Statuses, the year and the section checked against the caller's school (a class id, when given, must be that section's class).</summary>
        private async Task<Resolved> ResolveAsync(string schoolId, long academicYearId, long sectionId, long? classId, CancellationToken ct, bool requireActiveSection = true)
        {
            var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
            var inactiveId = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType, ct);
            if (activeId == null || inactiveId == null)
            {
                _logger.LogError("Configuration problem: Active / Inactive statuses are missing in lut_status");
                return new Resolved(0, 0, null, (500, "The system is not configured correctly. Please contact support."));
            }
            if (await _repo.GetYearAsync(schoolId, academicYearId, ct) == null)
                return new Resolved(activeId.Value, inactiveId.Value, null, (400, "The selected academic year was not found for your school."));
            var section = await _repo.GetSectionAsync(schoolId, sectionId, ct);
            if (section == null) return new Resolved(activeId.Value, inactiveId.Value, null, (400, "The selected section was not found for your school."));
            if (classId != null && classId != section.ClassId) return new Resolved(activeId.Value, inactiveId.Value, null, (400, "The selected section does not belong to the selected class."));
            if (requireActiveSection && (section.SectionStatusId != activeId || section.ClassStatusId != activeId))
                return new Resolved(activeId.Value, inactiveId.Value, null, (400, "The selected section (or its class) is inactive."));
            return new Resolved(activeId.Value, inactiveId.Value, section, null);
        }

        private async Task<ApiResponse<ClassTeacherAssignmentDto>> RowAsync(string schoolId, long assignmentId, int code, string message, CancellationToken ct)
        {
            var row = await _repo.GetAssignmentRowAsync(schoolId, assignmentId, ct);
            return row == null ? Result<ClassTeacherAssignmentDto>(500, "Something went wrong. Please try again later.", null) : Result(code, message, ToDto(row));
        }

        private static ClassTeacherAssignmentDto ToDto(AssignmentRow r) => new()
        {
            AssignmentId = r.Assignment.AssignmentId, AcademicYearId = r.Assignment.AcademicYearId, ClassId = r.Assignment.ClassId, ClassName = r.ClassName,
            SectionId = r.Assignment.SectionId, SectionName = r.SectionName, EmployeeId = r.Assignment.EmployeeId, EmployeeName = r.EmployeeName,
            StatusName = r.StatusName, AssignedAt = r.Assignment.CreatedAt, UpdatedAt = r.Assignment.UpdatedAt
        };

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
