using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace smss_api_service_layer.dto
{
    public class LoginRequest
    {
        /// <summary>The user's org_user_id, e.g. SUAD001, FIRS261013001 or DPS001ST001 (matched case-insensitively).</summary>
        [Required(ErrorMessage = "Login ID is required."), StringLength(100)]
        public string LoginId { get; set; } = null!;

        [Required(ErrorMessage = "Password is required."), StringLength(200)]
        public string Password { get; set; } = null!;

        public bool RememberMe { get; set; }
    }

    public class RefreshTokenRequest
    {
        [Required, StringLength(200)] public string RefreshToken { get; set; } = null!;
    }

    public class ChangePasswordRequest
    {
        [Required, StringLength(200)] public string CurrentPassword { get; set; } = null!;

        [Required, StringLength(100, MinimumLength = 8, ErrorMessage = "New password must be at least 8 characters.")]
        public string NewPassword { get; set; } = null!;
    }

    public class AuthUserResponse
    {
        public long UserId { get; set; }
        /// <summary>The person's / school's name, for display only.</summary>
        public string DisplayName { get; set; } = null!;
        /// <summary>The login id (org_user_id).</summary>
        public string OrgUserId { get; set; } = null!;
        public string UserType { get; set; } = null!;
        public string? Email { get; set; }
        public string? SchoolId { get; set; }
        public string? SchoolCode { get; set; }
        public string? SchoolName { get; set; }
        public string? LogoUrl { get; set; }
        public List<string> Roles { get; set; } = new();
        public bool IsFirstLogin { get; set; }
    }

    public class LoginResponse
    {
        public string Token { get; set; } = null!;
        public DateTime ExpiresAt { get; set; }
        public string RefreshToken { get; set; } = null!;
        public DateTime RefreshTokenExpiresAt { get; set; }
        public AuthUserResponse User { get; set; } = null!;
    }
}
