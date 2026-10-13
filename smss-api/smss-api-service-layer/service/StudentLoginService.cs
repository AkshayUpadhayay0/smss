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
    public class StudentLoginService : IStudentLoginService
    {
        private readonly IStudentLoginRepository _repo;
        private readonly INotificationService _notify;
        private readonly ILogger<StudentLoginService> _logger;

        public StudentLoginService(IStudentLoginRepository repo, INotificationService notify, ILogger<StudentLoginService> logger)
        {
            _repo = repo;
            _notify = notify;
            _logger = logger;
        }

        public async Task<ApiResponse<StudentLoginCreatedDto>> CreateStudentLoginAsync(string schoolId, string studentId, CancellationToken ct)
        {
            try
            {
                // 404 for another school's student too, so ids from other schools can't be probed
                var student = await _repo.GetStudentAsync(schoolId, studentId, ct);
                if (student == null) return Result(404, "Student not found.", null);

                var school = await _repo.GetSchoolAsync(schoolId, ct);
                if (school == null) return Result(404, "School not found.", null);

                if (await _repo.StudentHasLoginAsync(schoolId, student.StudentId, ct))
                    return Result(409, "This student already has a login.", null);

                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError($"Status '{StatusNames.Active}' ({StatusNames.GeneralType}) is missing in lut_status");

                var roleId = await _repo.GetRoleIdByNameAsync(RoleNames.Student, ct);
                if (roleId == null) return ConfigError($"Role '{RoleNames.Student}' is missing in lut_roles");

                // No id generation here: the student already has its id (assigned when the student record was created)
                var loginId = student.StudentId;
                var tempPassword = StudentLoginHelper.BuildTemporaryPassword(student.FirstName, student.DateOfBirth);
                var displayName = FullName(student.FirstName, student.MiddleName, student.LastName);

                var now = DateTime.UtcNow;
                var user = new TbUsers
                {
                    SchoolId = schoolId,
                    UserType = UserTypes.Student,
                    OrgUserId = loginId,
                    DisplayName = displayName,
                    Email = string.IsNullOrWhiteSpace(student.Email) ? null : student.Email.Trim(),
                    MobileNumber = string.IsNullOrWhiteSpace(student.MobileNumber) ? null : student.MobileNumber.Trim(),
                    PasswordHash = PasswordHelper.Hash(tempPassword),
                    IsFirstLogin = true,
                    StatusId = activeId,
                    CreatedAt = now,
                    UpdatedAt = now,
                    UserRoles = { new TbUserRoles { RoleId = roleId.Value, CreatedAt = now } }
                };

                await _repo.AddUserAsync(user, ct);

                // The student's own email first, else the primary guardian's (same resolver as every other notification)
                var guardianEmail = string.IsNullOrWhiteSpace(user.Email) ? await TryGetGuardianEmailAsync(schoolId, studentId, ct) : null;
                var sentTo = await _notify.SendAsync(user.Email, guardianEmail,
                    EmailTemplates.StudentCredentials(displayName, school.SchoolName, loginId, tempPassword, _notify.LoginUrl),
                    $"student login credentials for {loginId}", ct);

                return Result(201, "Student login created successfully.", new StudentLoginCreatedDto
                {
                    StudentId = student.StudentId,
                    DisplayName = displayName,
                    OrgUserId = loginId,
                    TemporaryPassword = tempPassword,
                    EmailSent = sentTo != null,
                    EmailSentTo = sentTo
                });
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Two clicks at once: the unique (school_id, org_user_id) index let only one through
                return Result(409, "This student already has a login.", null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while creating student login");
                return Result(500, "Something went wrong. Please try again later.", null);
            }
        }

        private async Task<string?> TryGetGuardianEmailAsync(string schoolId, string studentId, CancellationToken ct)
        {
            try { return await _repo.GetPrimaryGuardianEmailAsync(schoolId, studentId, ct); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not look up the guardian email for student {StudentId}", studentId);
                return null;
            }
        }

        private static string FullName(string first, string? middle, string? last) =>
            string.Join(' ', new[] { first, middle, last }.Where(p => !string.IsNullOrWhiteSpace(p)));

        private ApiResponse<StudentLoginCreatedDto> ConfigError(string detail)
        {
            _logger.LogError("Configuration problem: {Detail}", detail);
            return Result(500, "The system is not configured correctly. Please contact support.", null);
        }

        private static ApiResponse<StudentLoginCreatedDto> Result(int code, string message, StudentLoginCreatedDto? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };
    }
}
