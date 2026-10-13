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
        /// <summary>How many schools were created on this UTC date (schools are never hard-deleted, so this only grows).</summary>
        Task<int> GetSchoolCountForDateAsync(DateOnly date, CancellationToken ct);
        /// <summary>How many schools already have a short code of the form {prefix}{digits} (not scoped by date).</summary>
        Task<int> GetSchoolCountForShortCodePrefixAsync(string prefix, CancellationToken ct);
        Task<long?> GetRoleIdByNameAsync(string roleName);
        Task<List<string>> GetDuplicateFieldsAsync(string? schoolCode, string? gstin, string? pan, string? excludeSchoolId = null);
        Task AddSchoolWithAdminUserAsync(TbSchools school, TbUsers user);
        /// <summary>The school's own admin login (not its students' logins).</summary>
        Task<TbUsers?> GetUserBySchoolIdAsync(string schoolId, bool track = false);
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType);
        Task<int> SaveChangesAsync();
    }
}
