using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>
    /// Admin-initiated password reset (no email involved). Who may reset whom:
    ///   Super Admin  -> a School Admin (any school);
    ///   School Admin -> any other login of THEIR OWN school (employees, students, guardians ...), never a Super Admin;
    ///   everyone else, and anyone resetting themselves -> refused. A login of another school looks exactly like one that does not exist (404).
    /// The new temporary password is returned once; it is never logged and never written anywhere in plain text.
    /// </summary>
    public class PasswordResetService : IPasswordResetService
    {
        public const int MaxReasonLength = 500;
        private readonly IPasswordResetRepository _repo;
        private readonly ILogger<PasswordResetService> _logger;
        private readonly ILoginThrottle _throttle;

        public PasswordResetService(IPasswordResetRepository repo, ILogger<PasswordResetService> logger, ILoginThrottle throttle)
        {
            _repo = repo;
            _logger = logger;
            _throttle = throttle;
        }

        public async Task<ApiResponse<PasswordResetResultDto>> ResetAsync(PasswordResetCaller caller, ResetPasswordRequest request, CancellationToken ct)
        {
            var loginId = request.LoginId?.Trim();
            var reason = request.Reason?.Trim();
            if (string.IsNullOrEmpty(loginId)) return Result(400, "The login id is required.");
            if (string.IsNullOrEmpty(reason)) return Result(400, "A reason is required.");
            if (reason.Length > MaxReasonLength) return Result(400, $"The reason can be at most {MaxReasonLength} characters.");

            var isSuper = HasRole(caller.Roles, RoleNames.SuperAdmin);
            var isSchoolAdmin = HasRole(caller.Roles, RoleNames.SchoolAdmin) && !string.IsNullOrEmpty(caller.SchoolId);
            if (!isSuper && !isSchoolAdmin)
                return Result(403, "Only a Super Admin or a School Admin can reset a password.");

            try
            {
                // A School Admin searches ONLY their own school, so another school's login can never be told apart from a missing one
                var target = await _repo.FindTargetAsync(loginId, isSuper ? null : caller.SchoolId, ct);
                if (target == null) return Result(404, "No login with that id was found.");

                var user = target.User;
                if (user.UserId == caller.UserId)
                    return Result(403, "You can't reset your own password this way. Use Change Password instead.");

                var targetIsSchoolAdmin = HasRole(target.RoleNames, RoleNames.SchoolAdmin);
                var targetIsSuperAdmin = HasRole(target.RoleNames, RoleNames.SuperAdmin);
                var bySuper = isSuper && targetIsSchoolAdmin;
                var bySchoolAdmin = isSchoolAdmin && !targetIsSuperAdmin && string.Equals(user.SchoolId, caller.SchoolId, StringComparison.Ordinal);
                if (!bySuper && !bySchoolAdmin)
                {
                    // a School Admin-only caller never gets here for another school (the search was limited to their school); a Super Admin does for a login that is not a School Admin
                    return Result(403, isSuper && !isSchoolAdmin
                        ? "A Super Admin can reset the password of a School Admin only."
                        : "You are not allowed to reset this login's password.");
                }

                if (!target.IsActive)
                    return Result(409, "This login is inactive or blocked, so its password can't be reset. Reactivate the account first.");

                var now = DateTime.UtcNow;
                var temporaryPassword = PasswordHelper.GenerateTemporaryPassword();
                user.PasswordHash = PasswordHelper.Hash(temporaryPassword);
                user.IsFirstLogin = true;
                user.UpdatedAt = now;

                await _repo.SaveResetAsync(new TbPasswordResetAudit
                {
                    SchoolId = user.SchoolId,
                    TargetUserId = user.UserId,
                    TargetLoginId = user.OrgUserId ?? loginId,
                    ResetByUserId = caller.UserId,
                    Reason = reason,
                    ResetAt = now
                }, user.UserId, now, ct);

                // The person must be able to sign in with the new password at once, even if earlier wrong guesses had throttled the id
                _throttle.ClearLogin(user.OrgUserId ?? loginId);

                // Ids only: never the password and never the reason text
                _logger.LogInformation("Password reset: user {TargetUserId} by user {ResetByUserId}", user.UserId, caller.UserId);

                return new ApiResponse<PasswordResetResultDto>
                {
                    Status = true, StatusCode = 200, Message = "Password reset. Share the temporary password with the user; it is shown only once.",
                    Data = new PasswordResetResultDto { LoginId = user.OrgUserId ?? loginId, DisplayName = user.DisplayName ?? user.OrgUserId ?? loginId, TemporaryPassword = temporaryPassword }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while resetting a password");
                return Result(500, "Something went wrong. Please try again later.");
            }
        }

        private static bool HasRole(IEnumerable<string> roles, string role) => roles.Any(r => string.Equals(r?.Trim(), role, StringComparison.OrdinalIgnoreCase));

        private static ApiResponse<PasswordResetResultDto> Result(int code, string message) =>
            new() { Status = false, StatusCode = code, Message = message, Data = null };
    }
}
