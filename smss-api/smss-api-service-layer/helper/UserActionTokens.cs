using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace smss_api_service_layer.helper
{
    public static class ActionTokenPurposes
    {
        public const string SetPassword = "SET_PASSWORD";
        public const string ResetPassword = "RESET_PASSWORD";
        public static bool IsKnown(string? p) => p is SetPassword or ResetPassword;
    }

    /// <summary>
    /// The one-time link token: 32 random bytes (256 bits) as base64url (43 characters). Only its SHA-256 hash is ever stored; the token itself exists in memory long enough to be put in a message
    /// and is never written to a log, the delivery log or the database.
    /// </summary>
    public static class UserActionTokens
    {
        public const int TokenLength = 43;
        private static readonly Regex Shape = new("^[A-Za-z0-9_-]{43}$", RegexOptions.Compiled);

        public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>Lower-case hex SHA-256 of the token (what tb_user_action_tokens.token_hash holds).</summary>
        public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

        /// <summary>False for anything that cannot be a token we issued (wrong length or characters): such input is refused before it is even hashed.</summary>
        public static bool LooksValid(string? token) => token != null && Shape.IsMatch(token);

        /// <summary>The link a message carries. The token is in the QUERY of a page that immediately posts it in a request body and removes it from the address bar; the API itself never receives it in a URL.</summary>
        public static string BuildLink(string webBaseUrl, string purpose, string token)
        {
            var path = purpose == ActionTokenPurposes.ResetPassword ? "/parent/reset-password" : "/parent/set-password";
            return webBaseUrl.TrimEnd('/') + path + "?t=" + token;
        }
    }
}
