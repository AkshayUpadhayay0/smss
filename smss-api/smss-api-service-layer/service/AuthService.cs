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
        private const string InvalidCredentials = "Invalid username or password.";
        private const string SessionExpired = "Your session has expired. Please sign in again.";
        private const string InactiveAccount = "Your account is inactive. Please contact support.";

        // Two tabs can refresh with the same token at nearly the same moment; the loser of that
        // race is rejected but must not be treated as token theft.
        private static readonly TimeSpan ReuseGracePeriod = TimeSpan.FromSeconds(10);

        // Verified against when the username doesn't exist, so response time doesn't reveal valid usernames
        private static readonly string DummyHash = PasswordHelper.Hash("smss-dummy-password");

        private readonly IAuthRepository _repo;
        private readonly JwtTokenGenerator _jwt;
        private readonly ILogger<AuthService> _logger;

        public AuthService(IAuthRepository repo, JwtTokenGenerator jwt, ILogger<AuthService> logger)
        {
            _repo = repo;
            _jwt = jwt;
            _logger = logger;
        }

        public async Task<ApiResponse<object>> LoginAsync(LoginRequest req)
        {
            try
            {
                var username = req.Username.Trim();
                var user = await _repo.GetUserByUsernameAsync(username, track: true);

                if (user == null || string.IsNullOrEmpty(user.PasswordHash))
                {
                    PasswordHelper.Verify(req.Password, DummyHash);
                    return Fail(401, InvalidCredentials);
                }

                if (!PasswordHelper.Verify(req.Password, user.PasswordHash))
                    return Fail(401, InvalidCredentials);

                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType);
                if (activeId == null)
                    return ConfigError("Active status missing from lut_status");

                var school = await GetSchoolAsync(user);

                // Credentials were correct, so it's safe to tell the user why they can't get in
                if (!IsActive(user, school, activeId.Value))
                    return Fail(403, InactiveAccount);

                var now = DateTime.UtcNow;
                await _repo.DeleteExpiredRefreshTokensAsync(user.UserId, now);

                user.LastLoginAt = now;
                var response = await IssueTokensAsync(user, school, req.RememberMe);

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

                // Rotate: the presented token is single-use
                var response = await IssueTokensAsync(user, school, stored.RememberMe, rotateFrom: stored);

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
                var response = await IssueTokensAsync(user, school, rememberMe: false);

                return Ok(200, "Password changed successfully", response);
            }
            catch (Exception ex) { return Error(ex, "changing password"); }
        }

        // ---------------- helpers ----------------

        // Builds the access + refresh pair, stores the refresh hash and saves (together with any pending user changes)
        private async Task<LoginResponse> IssueTokensAsync(TbUsers user, TbSchools? school, bool rememberMe, TbRefreshTokens? rotateFrom = null)
        {
            var roles = (await _repo.GetRolesByUserIdAsync(user.UserId)).Select(r => r.RoleName).ToList();
            var (accessToken, accessExpires) = _jwt.Generate(user.UserId, user.Username!, user.UserType, user.SchoolId, roles);
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
            Username = user.Username!,
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
