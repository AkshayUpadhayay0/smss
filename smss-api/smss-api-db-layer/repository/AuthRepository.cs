using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_db_layer.repository
{
    public class AuthRepository : IAuthRepository
    {
        private readonly dbContext _dbContext;
        public AuthRepository(dbContext dbContext) => _dbContext = dbContext;

        // Case-insensitive match so "SCH2026001" and "sch2026001" both work
        public async Task<TbUsers?> GetUserByUsernameAsync(string username, bool track = false)
        {
            var key = username.ToLower();
            IQueryable<TbUsers> query = _dbContext.Users.Where(u => u.OrgUserId != null && u.OrgUserId.ToLower() == key);
            if (!track) query = query.AsNoTracking();
            return await query.FirstOrDefaultAsync();
        }

        public async Task<TbUsers?> GetUserByIdAsync(long userId, bool track = false)
        {
            IQueryable<TbUsers> query = _dbContext.Users.Where(u => u.UserId == userId);
            if (!track) query = query.AsNoTracking();
            return await query.FirstOrDefaultAsync();
        }

        public async Task<List<LutRole>> GetRolesByUserIdAsync(long userId)
        {
            return await (from ur in _dbContext.UserRoles.AsNoTracking()
                          join r in _dbContext.LutRoles.AsNoTracking() on ur.RoleId equals r.RoleId
                          where ur.UserId == userId
                          select r).ToListAsync();
        }

        public async Task<TbSchools?> GetSchoolByIdAsync(string schoolId)
        {
            return await _dbContext.Schools.AsNoTracking().FirstOrDefaultAsync(s => s.SchoolId == schoolId);
        }

        public async Task<int?> GetStatusIdByNameAsync(string statusName, string statusType)
        {
            return await _dbContext.LutStatus
                .AsNoTracking()
                .Where(s => s.Sname == statusName && s.Stype == statusType)
                .Select(s => (int?)s.Sid)
                .FirstOrDefaultAsync();
        }

        public void AddRefreshToken(TbRefreshTokens token) => _dbContext.RefreshTokens.Add(token);

        // Tracked: the service rotates/revokes the returned row
        public async Task<TbRefreshTokens?> GetRefreshTokenByHashAsync(string tokenHash)
        {
            return await _dbContext.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
        }

        public async Task RevokeAllRefreshTokensAsync(long userId, DateTime revokedAt)
        {
            await _dbContext.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, revokedAt));
        }

        // Housekeeping so the table doesn't grow forever
        public async Task DeleteExpiredRefreshTokensAsync(long userId, DateTime now)
        {
            await _dbContext.RefreshTokens
                .Where(t => t.UserId == userId && t.ExpiresAt < now)
                .ExecuteDeleteAsync();
        }

        public Task<int> SaveChangesAsync()=> _dbContext.SaveChangesAsync();
    }
}
