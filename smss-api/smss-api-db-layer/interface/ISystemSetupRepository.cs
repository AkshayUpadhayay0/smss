using smss_api_db_layer.entity;
using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_db_layer.@interface
{
    public interface ISystemSetupRepository
    {
        Task<bool> SuperAdminExistsAsync();
        Task<bool> UsernameOrEmailExistsAsync(string username, string? email);
        Task<long?> GetRoleIdByNameAsync(string roleName);
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType);
        Task AddSuperAdminAsync(TbUsers user);
        Task<long> GetNextSuperAdminNumberAsync();
    }
}
