using smss_api_db_layer.entity;
using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_db_layer.@interface
{
    public interface IAuthRepository
    {
        Task<TbUsers?> GetUserByLoginIdAsync(string loginId, bool track = false);
        Task<TbUsers?> GetUserByIdAsync(long userId, bool track = false);
        /// <summary>The user's is_first_login flag (read fresh from the database, no tracking), or null when the user no longer exists.</summary>
        Task<bool?> GetFirstLoginFlagAsync(long userId);
        Task<List<LutRole>> GetRolesByUserIdAsync(long userId);
        Task<TbSchools?> GetSchoolByIdAsync(string schoolId);
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType);

        // Refresh tokens
        void AddRefreshToken(TbRefreshTokens token);
        Task<TbRefreshTokens?> GetRefreshTokenByHashAsync(string tokenHash);
        Task RevokeAllRefreshTokensAsync(long userId, DateTime revokedAt);
        Task DeleteExpiredRefreshTokensAsync(long userId, DateTime now);

        Task<int> SaveChangesAsync();
    }
}
