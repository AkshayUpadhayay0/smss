using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace smss_api_service_layer.helper
{
    public class JwtTokenGenerator
    {
        private readonly JwtSettings _settings;
        public JwtTokenGenerator(IOptions<JwtSettings> settings) => _settings = settings.Value;

        public (string Token, DateTime ExpiresAtUtc) Generate(
            long userId, string username, string userType, string? schoolId, IEnumerable<string> roles)
        {
            var expires = DateTime.UtcNow.AddMinutes(_settings.ExpiryMinutes);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(ClaimTypes.NameIdentifier, userId.ToString()),
                new(ClaimTypes.Name, username),
                new("user_type", userType)
            };
            if (!string.IsNullOrEmpty(schoolId)) claims.Add(new Claim("school_id", schoolId));
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

            var creds = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key)), SecurityAlgorithms.HmacSha256);

            var jwt = new JwtSecurityToken(_settings.Issuer, _settings.Audience, claims, expires: expires, signingCredentials: creds);
            return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
        }

        // Opaque random token; only its hash is ever stored
        public (string Token, string Hash, DateTime ExpiresAtUtc) GenerateRefreshToken(bool rememberMe)
        {
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            var days = rememberMe ? _settings.RememberMeRefreshTokenDays : _settings.RefreshTokenDays;
            return (token, HashToken(token), DateTime.UtcNow.AddDays(days));
        }

        public static string HashToken(string token) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }
}
