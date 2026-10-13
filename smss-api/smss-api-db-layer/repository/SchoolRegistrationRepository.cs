using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_db_layer.repository
{
    public class SchoolRegistrationRepository: ISchoolRegistrationRepository
    {
        private readonly dbContext _dbContext;
        public SchoolRegistrationRepository(dbContext dbContext) => _dbContext = dbContext;

        public async Task<List<TbSchools>> GetSchoolsAsync()
        {
            return await _dbContext.Schools
                .AsNoTracking()
                .OrderBy(x => x.SchoolName)
                .ToListAsync();
        }

        public async Task<TbSchools?> GetSchoolByIdAsync(string schoolId, bool track = false)
        {
            IQueryable<TbSchools> query = _dbContext.Schools
                .Include(s => s.Contacts)
                .Where(s => s.SchoolId == schoolId);

            if (!track) query = query.AsNoTracking();
            return await query.FirstOrDefaultAsync();
        }

        // Uses the school_no_seq sequence from Step 2 (needs EF Core 7+)
        public async Task<long> GetNextSchoolNumberAsync()
        {
            return await _dbContext.Database
                .SqlQuery<long>($"SELECT nextval('public.school_no_seq') AS \"Value\"")
                .SingleAsync();
        }

        // An explicit UTC range, not `created_at::date`: the ::date cast uses the database session time zone
        // (Asia/Calcutta here), which would disagree with the UTC date that goes into the school id for 5.5 hours a day.
        // "DPS" must not count "DPSX001", so the digits-only remainder is checked after the prefix match
        public async Task<int> GetSchoolCountForShortCodePrefixAsync(string prefix, CancellationToken ct)
        {
            var codes = await _dbContext.Schools.AsNoTracking()
                .Where(s => s.SchoolShortCode != null && s.SchoolShortCode.StartsWith(prefix))
                .Select(s => s.SchoolShortCode!)
                .ToListAsync(ct);
            return codes.Count(c => c.Length > prefix.Length && c.Skip(prefix.Length).All(char.IsAsciiDigit));
        }

        public Task<int> GetSchoolCountForDateAsync(DateOnly date, CancellationToken ct)
        {
            var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var end = start.AddDays(1);
            return _dbContext.Schools.AsNoTracking().CountAsync(s => s.CreatedAt >= start && s.CreatedAt < end, ct);
        }

        public async Task<long?> GetRoleIdByNameAsync(string roleName)
        {
            return await _dbContext.LutRoles
                .AsNoTracking()
                .Where(r => r.RoleName == roleName)
                .Select(r => (long?)r.RoleId)
                .FirstOrDefaultAsync();
        }

        public async Task<List<string>> GetDuplicateFieldsAsync(
            string? schoolCode, string? gstin, string? pan, string? excludeSchoolId = null)
        {
            var duplicates = new List<string>();
            var q = _dbContext.Schools.AsNoTracking()
                .Where(s => excludeSchoolId == null || s.SchoolId != excludeSchoolId);

            if (schoolCode != null && await q.AnyAsync(s => s.SchoolCode == schoolCode)) duplicates.Add("School code");
            if (gstin != null && await q.AnyAsync(s => s.SchoolGstin == gstin)) duplicates.Add("GSTIN");
            if (pan != null && await q.AnyAsync(s => s.SchoolPan == pan)) duplicates.Add("PAN");

            return duplicates;
        }

        // One SaveChanges = one transaction: school -> contacts -> user -> user role
        public async Task AddSchoolWithAdminUserAsync(TbSchools school, TbUsers user)
        {
            _dbContext.Schools.Add(school);   // includes school.Contacts
            _dbContext.Users.Add(user);       // includes user.UserRoles
            await _dbContext.SaveChangesAsync();
        }

        // The school's admin login is the row whose login id IS the school id. Student logins carry the same school_id,
        // so filtering on school_id alone would pick an arbitrary one of them.
        public async Task<TbUsers?> GetUserBySchoolIdAsync(string schoolId, bool track = false)
        {
            IQueryable<TbUsers> query = _dbContext.Users.Where(u => u.SchoolId == schoolId && u.OrgUserId == schoolId);
            if (!track) query = query.AsNoTracking();
            return await query.FirstOrDefaultAsync();
        }

        // Matches lut_status by name + stype, e.g. ("Active", "general status") -> sid 1
        public async Task<int?> GetStatusIdByNameAsync(string statusName, string statusType)
        {
            return await _dbContext.LutStatus
                .AsNoTracking()
                .Where(s => s.Sname == statusName && s.Stype == statusType)
                .Select(s => (int?)s.Sid)
                .FirstOrDefaultAsync();
        }

        public Task<int> SaveChangesAsync() => _dbContext.SaveChangesAsync();
    }
}
