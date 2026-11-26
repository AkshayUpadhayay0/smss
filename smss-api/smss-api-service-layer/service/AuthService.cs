using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class AuthService : IAuthService
    {
        private const string InvalidCredentials = "Invalid login ID or password.";
        private const string SessionExpired = "Your session has expired. Please sign in again.";
        private const string InactiveAccount = "Your account is inactive. Please contact support.";

        // Two tabs can refresh with the same token at nearly the same moment; the loser of that
        // race is rejected but must not be treated as token theft.
        private static readonly TimeSpan ReuseGracePeriod = TimeSpan.FromSeconds(10);

        // Verified against when the login id doesn't exist, so response time doesn't reveal valid login ids
        private static readonly string DummyHash = PasswordHelper.Hash("smss-dummy-password");

        private readonly IAuthRepository _repo;
        private readonly JwtTokenGenerator _jwt;
        private readonly ILogger<AuthService> _logger;
        private readonly ILoginThrottle _throttle;
        private readonly IParentAccountRepository? _accounts;

        public AuthService(IAuthRepository repo, JwtTokenGenerator jwt, ILogger<AuthService> logger, ILoginThrottle throttle, IParentAccountRepository? accounts = null)
        {
            _repo = repo;
            _jwt = jwt;
            _logger = logger;
            _throttle = throttle;
            _accounts = accounts;
        }

        /// <summary>
        /// A Guardian login may sign in only while its guardian record is Active and has not been merged into another (a merged guardian's login moves to the survivor). Returns the guardian id for the token,
        /// or null when this login must be refused. Fails closed: with no way to look the guardian up, a Guardian login is refused. Other user types return 0 (not a guardian).
        /// </summary>
        private async Task<long?> GuardianIdForAsync(TbUsers user, int activeId)
        {
            if (user.UserType != UserTypes.Guardian) return 0;
            if (_accounts == null) return null;
            var info = await _accounts.GetGuardianByUserIdAsync(user.UserId, CancellationToken.None);
            return info != null && info.StatusId == activeId && info.MergedIntoGuardianId == null ? info.GuardianId : null;
        }

        /// <summary>
        /// A Student login is refused once the student is Inactive, or has LEFT or been TRANSFERRED_OUT, or has passed out of a year that is no longer current (LeavingRules.StudentLoginBlocked). Checked at sign-in AND
        /// at refresh. Other user types are not affected. With no way to look the student up (no account repository: only in unit-test wiring) or no student row, nothing is blocked.
        /// </summary>
        private async Task<bool> StudentLoginBlockedAsync(TbUsers user)
        {
            if (user.UserType != UserTypes.Student || _accounts == null || string.IsNullOrWhiteSpace(user.SchoolId) || string.IsNullOrWhiteSpace(user.OrgUserId)) return false;
            var state = await _accounts.GetStudentLoginStateAsync(user.SchoolId, user.OrgUserId, CancellationToken.None);
            return state != null && LeavingRules.StudentLoginBlocked(state.StudentActive, state.Outcome, state.YearIsCurrent);
        }

        public async Task<ApiResponse<object>> LoginAsync(LoginRequest req, string? clientKey = null)
        {
            try
            {
                // The login credential is tb_users.org_user_id (SUAD001, FIRS261013001, DPS001ST001); display_name is only a label
                var loginId = req.LoginId.Trim();
                var client = clientKey ?? ClientAddress.Unknown;

                // Too many recent failures for this id or this address: answer before looking at the id or the password, so a throttled reply is the same for known and unknown ids
                var wait = _throttle.RetryAfter(loginId, client);
                if (wait != null)
                {
                    var seconds = (int)Math.Ceiling(wait.Value.TotalSeconds);
                    var minutes = Math.Max(1, (int)Math.Ceiling(wait.Value.TotalMinutes));
                    return new ApiResponse<object>
                    {
                        Status = false, StatusCode = 429, Data = new LoginThrottledDto { RetryAfterSeconds = seconds },
                        Message = $"Too many failed sign-in attempts. Please wait {minutes} minute{(minutes == 1 ? "" : "s")} and try again."
                    };
                }

                var user = await _repo.GetUserByLoginIdAsync(loginId, track: true);

                if (user == null || string.IsNullOrEmpty(user.PasswordHash))
                {
                    PasswordHelper.Verify(req.Password, DummyHash);
                    _throttle.RecordFailure(loginId, client);
                    return Fail(401, InvalidCredentials);
                }

                if (!PasswordHelper.Verify(req.Password, user.PasswordHash))
                {
                    _throttle.RecordFailure(loginId, client);
                    return Fail(401, InvalidCredentials);
                }

                _throttle.RecordSuccess(loginId);   // the right password: this id starts from zero again

                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType);
                if (activeId == null)
                    return ConfigError("Active status missing from lut_status");

                var school = await GetSchoolAsync(user);

                // Credentials were correct, so it's safe to tell the user why they can't get in
                if (!IsActive(user, school, activeId.Value))
                    return Fail(403, InactiveAccount);
                var guardianId = await GuardianIdForAsync(user, activeId.Value);
                if (guardianId == null) return Fail(403, InactiveAccount);
                if (await StudentLoginBlockedAsync(user)) return Fail(403, LeavingRules.LoginBlockedMessage);

                var now = DateTime.UtcNow;
                await _repo.DeleteExpiredRefreshTokensAsync(user.UserId, now);

                user.LastLoginAt = now;
                var response = await IssueTokensAsync(user, school, req.RememberMe, guardianId: guardianId);

                return Ok(200, "Login successful", response);
            }
            catch (Exception ex) { return Error(ex, "logging in"); }
        }

        public async Task<ApiResponse<object>> RefreshAsync(RefreshTokenRequest req)
        {
            try
            {
                var stored = await _repo.GetRefreshTokenByHashAsync(JwtTokenGenerator.HashToken(req.RefreshToken));
                if (stored == null) return Fail(401, SessionExpired);

                var now = DateTime.UtcNow;

                if (stored.RevokedAt != null)
                {
                    // A rotated-out token is being replayed: assume it leaked and end every session for this user
                    if (now - stored.RevokedAt.Value > ReuseGracePeriod)
                    {
                        _logger.LogWarning("Refresh token reuse detected for user {UserId}; revoking all sessions", stored.UserId);
                        await _repo.RevokeAllRefreshTokensAsync(stored.UserId, now);
                    }
                    return Fail(401, SessionExpired);
                }

                if (stored.ExpiresAt <= now) return Fail(401, SessionExpired);

                var user = await _repo.GetUserByIdAsync(stored.UserId, track: true);
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType);
                if (user == null || activeId == null) return Fail(401, SessionExpired);

                var school = await GetSchoolAsync(user);
                if (!IsActive(user, school, activeId.Value))
                {
                    await _repo.RevokeAllRefreshTokensAsync(user.UserId, now);
                    return Fail(403, InactiveAccount);
                }
                var guardianId = await GuardianIdForAsync(user, activeId.Value);
                if (guardianId == null)
                {
                    await _repo.RevokeAllRefreshTokensAsync(user.UserId, now);   // a merged / inactive guardian's sessions end
                    return Fail(403, InactiveAccount);
                }
                if (await StudentLoginBlockedAsync(user))
                {
                    await _repo.RevokeAllRefreshTokensAsync(user.UserId, now);   // a student who left: the session ends at the next refresh
                    return Fail(403, LeavingRules.LoginBlockedMessage);
                }

                // Rotate: the presented token is single-use
                var response = await IssueTokensAsync(user, school, stored.RememberMe, rotateFrom: stored, guardianId: guardianId);

                return Ok(200, "Token refreshed", response);
            }
            catch (Exception ex) { return Error(ex, "refreshing token"); }
        }

        // Idempotent: unknown or already-revoked tokens still succeed so the client can always clear its session
        public async Task<ApiResponse<object>> LogoutAsync(RefreshTokenRequest req)
        {
            try
            {
                var stored = await _repo.GetRefreshTokenByHashAsync(JwtTokenGenerator.HashToken(req.RefreshToken));
                if (stored != null && stored.RevokedAt == null)
                {
                    stored.RevokedAt = DateTime.UtcNow;
                    await _repo.SaveChangesAsync();
                }
                return Ok(200, "Logged out", new { });
            }
            catch (Exception ex) { return Error(ex, "logging out"); }
        }

        public async Task<ApiResponse<object>> GetCurrentUserAsync(long userId)
        {
            try
            {
                var user = await _repo.GetUserByIdAsync(userId);
                if (user == null) return Fail(404, "User not found");

                var school = await GetSchoolAsync(user);
                var roles = (await _repo.GetRolesByUserIdAsync(userId)).Select(r => r.RoleName).ToList();
                return Ok(200, "Successfully fetched", ToUserResponse(user, school, roles));
            }
            catch (Exception ex) { return Error(ex, "fetching current user"); }
        }

        // Ends every other session and returns a fresh token pair for the current one
        public async Task<ApiResponse<object>> ChangePasswordAsync(long userId, ChangePasswordRequest req)
        {
            try
            {
                var user = await _repo.GetUserByIdAsync(userId, track: true);
                if (user == null || string.IsNullOrEmpty(user.PasswordHash)) return Fail(404, "User not found");

                if (!PasswordHelper.Verify(req.CurrentPassword, user.PasswordHash))
                    return Fail(400, "Current password is incorrect.");

                if (req.CurrentPassword == req.NewPassword)
                    return Fail(400, "New password must be different from the current password.");

                var now = DateTime.UtcNow;
                user.PasswordHash = PasswordHelper.Hash(req.NewPassword);
                user.IsFirstLogin = false;
                user.UpdatedAt = now;

                await _repo.RevokeAllRefreshTokensAsync(userId, now);
                var school = await GetSchoolAsync(user);
                var activeForGuardian = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType);
                var guardianOfUser = activeForGuardian == null ? null : await GuardianIdForAsync(user, activeForGuardian.Value);
                if (guardianOfUser == null) return Fail(403, InactiveAccount);
                if (await StudentLoginBlockedAsync(user)) return Fail(403, LeavingRules.LoginBlockedMessage);
                var response = await IssueTokensAsync(user, school, rememberMe: false, guardianId: guardianOfUser);

                return Ok(200, "Password changed successfully", response);
            }
            catch (Exception ex) { return Error(ex, "changing password"); }
        }

        // ---------------- helpers ----------------

        // Builds the access + refresh pair, stores the refresh hash and saves (together with any pending user changes)
        private async Task<LoginResponse> IssueTokensAsync(TbUsers user, TbSchools? school, bool rememberMe, TbRefreshTokens? rotateFrom = null, long? guardianId = null)
        {
            var roles = (await _repo.GetRolesByUserIdAsync(user.UserId)).Select(r => r.RoleName).ToList();
            var (accessToken, accessExpires) = _jwt.Generate(user.UserId, user.DisplayName ?? user.OrgUserId ?? string.Empty, user.UserType, user.SchoolId, roles, guardianId is > 0 ? guardianId : null);
            var refresh = _jwt.GenerateRefreshToken(rememberMe);

            var now = DateTime.UtcNow;
            if (rotateFrom != null)
            {
                rotateFrom.RevokedAt = now;
                rotateFrom.ReplacedByTokenHash = refresh.Hash;
            }

            _repo.AddRefreshToken(new TbRefreshTokens
            {
                UserId = user.UserId,
                TokenHash = refresh.Hash,
                RememberMe = rememberMe,
                ExpiresAt = refresh.ExpiresAtUtc,
                CreatedAt = now
            });
            await _repo.SaveChangesAsync();

            return new LoginResponse
            {
                Token = accessToken,
                ExpiresAt = accessExpires,
                RefreshToken = refresh.Token,
                RefreshTokenExpiresAt = refresh.ExpiresAtUtc,
                User = ToUserResponse(user, school, roles)
            };
        }

        private async Task<TbSchools?> GetSchoolAsync(TbUsers user) =>
            string.IsNullOrEmpty(user.SchoolId) ? null : await _repo.GetSchoolByIdAsync(user.SchoolId);

        private static bool IsActive(TbUsers user, TbSchools? school, int activeId) =>
            user.StatusId == activeId && (school == null || school.SchoolStatusId == activeId);

        private static AuthUserResponse ToUserResponse(TbUsers user, TbSchools? school, List<string> roles) => new()
        {
            UserId = user.UserId,
            DisplayName = user.DisplayName ?? user.OrgUserId ?? string.Empty,
            OrgUserId = user.OrgUserId ?? string.Empty,
            UserType = user.UserType,
            Email = user.Email,
            SchoolId = user.SchoolId,
            SchoolCode = school?.SchoolCode,
            SchoolName = school?.SchoolName,
            LogoUrl = school?.LogoUrl,
            Roles = roles,
            IsFirstLogin = user.IsFirstLogin
        };

        private static ApiResponse<object> Ok(int code, string message, object data) =>
            new() { Status = true, StatusCode = code, Message = message, Data = data };

        private static ApiResponse<object> Fail(int code, string message) =>
            new() { Status = false, StatusCode = code, Message = message };

        private ApiResponse<object> Error(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Fail(500, "Something went wrong. Please try again later.");
        }

        private ApiResponse<object> ConfigError(string detail)
        {
            _logger.LogError("Configuration problem: {Detail}", detail);
            return Fail(500, "The system is not configured correctly. Please contact support.");
        }
    }
}
