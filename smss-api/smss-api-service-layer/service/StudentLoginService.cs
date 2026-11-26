using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        private readonly ILogger<StudentLoginService> _logger;
        private readonly IParentAccountRepository? _accounts;
        private readonly IMessagingRepository? _messaging;
        private readonly IUserActionTokenService? _tokens;
        private readonly IParentMessageDispatcher? _dispatcher;
        private readonly IMyStudentsService? _myStudents;
        private readonly ParentAccountsOptions _opt;

        // The optional services keep older constructors compiling; the DI container always supplies them. Without them no link is sent.
        public StudentLoginService(IStudentLoginRepository repo, INotificationService notify, ILogger<StudentLoginService> logger, IParentAccountRepository? accounts = null, IMessagingRepository? messaging = null,
            IUserActionTokenService? tokens = null, IParentMessageDispatcher? dispatcher = null, IMyStudentsService? myStudents = null, IOptions<ParentAccountsOptions>? options = null)
        {
            _repo = repo; _logger = logger; _accounts = accounts; _messaging = messaging; _tokens = tokens; _dispatcher = dispatcher; _myStudents = myStudents; _opt = options?.Value ?? new ParentAccountsOptions();
        }

        /// <summary>A School Admin: always. A class teacher: only when the student is in a section they are class teacher of (the Class-Teacher module answers, through My Students).</summary>
        private async Task<bool> MayActAsync(string schoolId, string studentId, long actorUserId, bool actorIsSchoolAdmin, CancellationToken ct)
        {
            if (actorIsSchoolAdmin) return true;
            if (_myStudents == null) return false;
            var mine = await _myStudents.GetMyClassAsync(schoolId, actorUserId, ct);
            return mine.Status && mine.Data != null && mine.Data.Sections.Any(s => s.IsClassTeacher && s.Students.Any(st => string.Equals(st.StudentId, studentId, StringComparison.OrdinalIgnoreCase)));
        }

        public async Task<ApiResponse<StudentLoginCreatedDto>> CreateStudentLoginAsync(string schoolId, string studentId, long actorUserId, bool actorIsSchoolAdmin, CancellationToken ct)
        {
            try
            {
                // 404 for another school's student too, so ids from other schools can't be probed
                var student = await _repo.GetStudentAsync(schoolId, studentId, ct);
                if (student == null) return Result(404, "Student not found.", null);
                if (!await MayActAsync(schoolId, student.StudentId, actorUserId, actorIsSchoolAdmin, ct)) return Result(403, "You can create logins only for students of your own class section.", null);

                var school = await _repo.GetSchoolAsync(schoolId, ct);
                if (school == null) return Result(404, "School not found.", null);

                if (await _repo.StudentHasLoginAsync(schoolId, student.StudentId, ct))
                    return Result(409, "This student already has a login.", null);

                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError($"Status '{StatusNames.Active}' ({StatusNames.GeneralType}) is missing in lut_status");

                var roleId = await _repo.GetRoleIdByNameAsync(RoleNames.Student, ct);
                if (roleId == null) return ConfigError($"Role '{RoleNames.Student}' is missing in lut_roles");

                var displayName = FullName(student.FirstName, student.MiddleName, student.LastName);
                var now = DateTime.UtcNow;
                var user = new TbUsers
                {
                    SchoolId = schoolId,
                    UserType = UserTypes.Student,
                    OrgUserId = student.StudentId,   // no id generation: the student already has its id
                    DisplayName = displayName,
                    Email = string.IsNullOrWhiteSpace(student.Email) ? null : student.Email.Trim(),
                    MobileNumber = string.IsNullOrWhiteSpace(student.MobileNumber) ? null : student.MobileNumber.Trim(),
                    PasswordHash = null,   // no password exists until the parent chooses one through the link (there is no guessable temporary password any more)
                    IsFirstLogin = true,
                    StatusId = activeId,
                    CreatedAt = now,
                    UpdatedAt = now,
                    UserRoles = { new TbUserRoles { RoleId = roleId.Value, CreatedAt = now } }
                };
                await _repo.AddUserAsync(user, ct);

                var dto = new StudentLoginCreatedDto { StudentId = student.StudentId, DisplayName = displayName, OrgUserId = student.StudentId };
                await SendLinkAsync(schoolId, school.SchoolName, student, user, actorUserId, dto, ct);
                return Result(201, "Student login created.", dto);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return Result(409, "This student already has a login.", null);   // two clicks at once: the unique (school_id, org_user_id) index let only one through
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error while creating student login");
                return Result(500, "Something went wrong. Please try again later.", null);
            }
        }

        public async Task<ApiResponse<StudentLoginCreatedDto>> ResendLoginLinkAsync(string schoolId, string studentId, long actorUserId, bool actorIsSchoolAdmin, CancellationToken ct)
        {
            try
            {
                var student = await _repo.GetStudentAsync(schoolId, studentId, ct);
                if (student == null) return Result(404, "Student not found.", null);
                if (!await MayActAsync(schoolId, student.StudentId, actorUserId, actorIsSchoolAdmin, ct)) return Result(403, "You can resend logins only for students of your own class section.", null);
                var school = await _repo.GetSchoolAsync(schoolId, ct);
                if (school == null || _accounts == null || _messaging == null) return Result(404, "School not found.", null);

                var userId = await _repo.GetUserIdByLoginIdAsync(schoolId, student.StudentId, ct);
                var user = userId == null ? null : await _accounts.GetUserAsync(userId.Value, false, ct);
                if (user == null || user.UserType != UserTypes.Student) return Result(409, "This student has no login yet.", null);
                if (user.PasswordHash != null) return Result(409, "This student has already chosen a password.", null);
                var count = await _messaging.CountTokensSinceAsync(user.UserId, ActionTokenPurposes.SetPassword, DateTime.UtcNow.AddHours(-24), ct);
                if (count >= _opt.MaxLinksPer24Hours) return Result(429, $"Links have already been sent {count} times in the last 24 hours. Try again later.", null);

                var dto = new StudentLoginCreatedDto { StudentId = student.StudentId, DisplayName = user.DisplayName ?? student.StudentId, OrgUserId = student.StudentId };
                await SendLinkAsync(schoolId, school.SchoolName, student, user, actorUserId, dto, ct);
                return Result(200, "A new link was sent.", dto);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error while resending a student login link");
                return Result(500, "Something went wrong. Please try again later.", null);
            }
        }

        /// <summary>Issues the one-time link and sends it to the PRIMARY guardian's channels. Every shortfall is a message for the person, never an exception; the login itself already exists.</summary>
        private async Task SendLinkAsync(string schoolId, string schoolName, TbStudent student, TbUsers user, long actor, StudentLoginCreatedDto dto, CancellationToken ct)
        {
            try
            {
                if (_accounts == null || _tokens == null || _dispatcher == null) { dto.Message = "The login was created. No link could be sent: the messaging services are not available."; return; }
                var guardian = await _accounts.GetPrimaryGuardianOfStudentAsync(schoolId, student.StudentId, ct);
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct) ?? 0;
                if (guardian == null) { dto.Message = "The login was created, but the student has no primary guardian to send the password link to."; return; }
                var hasWa = !string.IsNullOrWhiteSpace(guardian.WhatsAppNumber);
                var hasEmail = !string.IsNullOrWhiteSpace(guardian.Email) && PhoneNumbers.EmailError(guardian.Email, "x") == null;
                if (guardian.StatusId != activeId || guardian.MergedIntoGuardianId != null) { dto.Message = "The login was created, but the primary guardian is not Active, so no link was sent."; return; }
                if (guardian.ConsentGivenAt == null) { dto.Message = "The login was created, but no parent consent is on record for the primary guardian, so no link was sent. Record the consent, then resend the link."; return; }
                if (!hasWa && !hasEmail) { dto.Message = "The login was created, but the primary guardian has no WhatsApp number or email on file. Add one, then resend the link."; return; }

                var issued = await _tokens.IssueAsync(user.UserId, schoolId, ActionTokenPurposes.SetPassword, actor, ct);
                var link = _tokens.BuildLink(ActionTokenPurposes.SetPassword, issued.Token);
                var guardianName = string.Join(' ', new[] { guardian.FirstName, guardian.LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));
                var studentName = dto.DisplayName;
                var message = new ParentMessage(schoolId, guardian.GuardianId, user.UserId, ParentTemplateCodes.StudentLoginReady, MessagePurposes.StudentLogin,
                    hasWa ? guardian.WhatsAppNumber : null, hasWa ? ParentTemplates.StudentLoginReady(guardianName, studentName, schoolName, student.StudentId, _opt.SetPasswordLinkHours, issued.Token) : null,
                    hasEmail ? guardian.Email : null, hasEmail && link != null ? EmailTemplates.ParentStudentLoginReady(guardianName, studentName, schoolName, student.StudentId, link, _opt.SetPasswordLinkHours) : null, actor);
                var outcomes = await _dispatcher.SendAsync(message, ct);
                dto.Deliveries = outcomes.Select(o => new ParentDeliveryDto { DeliveryId = o.DeliveryId ?? 0, Channel = o.Channel, Status = o.Status, ErrorCode = o.ErrorCode, TemplateCode = message.TemplateCode, Purpose = message.Purpose, SenderKind = "", CreatedAt = DateTime.UtcNow }).ToList();
                dto.LinkSent = outcomes.Any(o => o.Status is "SENT" or "LOGGED");
                dto.Message = dto.LinkSent ? "The login was created and a link to set the password was sent to the parent." : "The login was created, but the link could not be sent. Check the delivery status and resend.";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError("The password link for student {StudentId} could not be sent ({Error})", student.StudentId, ex.GetType().Name);
                dto.LinkSent = false; dto.Message = "The login was created, but the link could not be sent. Resend it from the student's profile.";
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
