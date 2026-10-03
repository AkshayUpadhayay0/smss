using smss_api_db_layer.entity;
using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_db_layer.@interface
{
    public interface IAuthRepository
    {
        Task<TbUsers?> GetUserByUsernameAsync(string username, bool track = false);
        Task<TbUsers?> GetUserByIdAsync(long userId, bool track = false);
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
