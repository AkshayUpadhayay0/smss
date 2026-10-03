using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_service_layer.helper
{
    public class JwtSettings
    {
        public const string SectionName = "Jwt";

        public string Issuer { get; set; } = "smss-api";
        public string Audience { get; set; } = "smss-web";
        public string Key { get; set; } = string.Empty;            // min 32 chars; override via user-secrets / env var in real deployments
        public int ExpiryMinutes { get; set; } = 15;                // access token lifetime (short; the refresh token keeps the user signed in)
        public int RefreshTokenDays { get; set; } = 1;              // normal login
        public int RememberMeRefreshTokenDays { get; set; } = 14;   // "remember me"
    }
}
