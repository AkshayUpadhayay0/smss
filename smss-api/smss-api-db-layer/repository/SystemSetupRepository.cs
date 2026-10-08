using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_db_layer.repository
{
    public class SystemSetupRepository : ISystemSetupRepository
    {
        private readonly dbContext _dbContext;
        public SystemSetupRepository(dbContext dbContext) => _dbContext = dbContext;

        public async Task<bool> SuperAdminExistsAsync()
        {
            return await _dbContext.UserRoles
                .Join(_dbContext.LutRoles, ur => ur.RoleId, r => r.RoleId, (ur, r) => r)
                .AnyAsync(r => r.RoleName == "Super Admin");
        }

        public async Task<bool> UsernameOrEmailExistsAsync(string username, string? email)
        {
            return await _dbContext.Users.AnyAsync(u =>
                u.Username == username || (email != null && u.Email == email));
        }

        public async Task<long?> GetRoleIdByNameAsync(string roleName)
        {
            return await _dbContext.LutRoles
                .AsNoTracking()
                .Where(r => r.RoleName == roleName)
                .Select(r => (long?)r.RoleId)
                .FirstOrDefaultAsync();
        }

        public async Task<int?> GetStatusIdByNameAsync(string statusName, string statusType)
        {
            return await _dbContext.LutStatus
                .AsNoTracking()
                .Where(s => s.Sname == statusName && s.Stype == statusType)
                .Select(s => (int?)s.Sid)
                .FirstOrDefaultAsync();
        }

        public async Task AddSuperAdminAsync(TbUsers user)
        {
            _dbContext.Users.Add(user);   // includes user.UserRoles
            await _dbContext.SaveChangesAsync();
        }
        public async Task<long> GetNextSuperAdminNumberAsync()
        {
            return await _dbContext.Database
                .SqlQuery<long>($"SELECT nextval('public.super_admin_no_seq') AS \"Value\"")
                .SingleAsync();
        }
    }
}
