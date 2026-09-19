using smss_api_db_layer.entity;
using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_db_layer.@interface
{
    public interface ISchoolRegistrationRepository
    {
        Task<List<TbSchools>> GetSchoolsAsync();
        Task<TbSchools?> GetSchoolByIdAsync(string schoolId, bool track = false);
        Task<long> GetNextSchoolNumberAsync();
        Task<long?> GetRoleIdByNameAsync(string roleName);
        Task<List<string>> GetDuplicateFieldsAsync(string? schoolCode, string? gstin, string? pan, string? excludeSchoolId = null);
        Task AddSchoolWithAdminUserAsync(TbSchools school, TbUsers user);
        Task<int> SaveChangesAsync();
    }
}
