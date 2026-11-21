using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class PartnerSchoolRepository : IPartnerSchoolRepository
    {
        // Same role_code the rest of the API uses for the Super Admin (RoleCodes.SuperAdmin in the service layer)
        public const string SuperAdminRoleCode = "SUPER_ADMIN";

        private readonly dbContext _db;
        public PartnerSchoolRepository(dbContext db) => _db = db;

        // One query, no N+1: city and state names are correlated sub-selects over the LGD lookup keys; the Active id is resolved by name in the same statement
        public Task<List<PartnerSchoolRow>> GetListedAsync(string activeName, string statusType, int max, CancellationToken ct) =>
            _db.Schools.AsNoTracking()
                .Where(s => s.ShowOnWebsite
                    && !_db.Users.Any(u => u.SchoolId == s.SchoolId && u.UserRoles.Any(ur => _db.LutRoles.Any(r => r.RoleId == ur.RoleId && r.RoleCode == SuperAdminRoleCode)))
                    && _db.LutStatus.Any(st => st.Sid == s.SchoolStatusId && st.Sname == activeName && st.Stype == statusType))
                .OrderBy(s => s.SchoolName).ThenBy(s => s.SchoolId)
                .Take(max)
                .Select(s => new PartnerSchoolRow(
                    s.SchoolName,
                    s.LogoUrl,
                    _db.LutCities.Where(c => c.Cid == s.CountryId && c.Sid == s.StateId && c.Did == s.DistrictId && c.CityId == s.CityId).Select(c => c.CityName).FirstOrDefault(),
                    _db.LutStates.Where(t => t.Cid == s.CountryId && t.Sid == s.StateId).Select(t => t.Sname).FirstOrDefault()))
                .ToListAsync(ct);

        public Task<bool> IsPlatformSchoolAsync(string schoolId, CancellationToken ct) =>
            _db.Users.AsNoTracking().AnyAsync(u => u.SchoolId == schoolId && u.UserRoles.Any(ur => _db.LutRoles.Any(r => r.RoleId == ur.RoleId && r.RoleCode == SuperAdminRoleCode)), ct);

        public async Task<bool> SetShowOnWebsiteAsync(string schoolId, bool show, DateTime nowUtc, CancellationToken ct)
        {
            var school = await _db.Schools.AsTracking().FirstOrDefaultAsync(s => s.SchoolId == schoolId, ct);
            if (school == null) return false;
            school.ShowOnWebsite = show;
            school.UpdatedAt = nowUtc;
            await _db.SaveChangesAsync(ct);
            return true;
        }
    }
}
