using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class PasswordResetRepository : IPasswordResetRepository
    {
        private const string StatusActive = "Active", StatusGeneral = "general status";
        private readonly dbContext _db;
        public PasswordResetRepository(dbContext db) => _db = db;

        public async Task<PasswordResetTarget?> FindTargetAsync(string loginId, string? onlySchoolId, CancellationToken ct)
        {
            var key = loginId.Trim().ToLower();
            var query = _db.Users.Where(u => u.OrgUserId != null && u.OrgUserId.ToLower() == key);
            if (onlySchoolId != null) query = query.Where(u => u.SchoolId == onlySchoolId);
            var user = await query.FirstOrDefaultAsync(ct);   // tracked on purpose: the service changes the hash on it
            if (user == null) return null;

            var roles = await (from ur in _db.UserRoles.AsNoTracking()
                               join r in _db.LutRoles.AsNoTracking() on ur.RoleId equals r.RoleId
                               where ur.UserId == user.UserId
                               select r.RoleName).ToListAsync(ct);

            var activeId = await _db.LutStatus.AsNoTracking().Where(s => s.Sname == StatusActive && s.Stype == StatusGeneral).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);
            var schoolActive = true;
            if (!string.IsNullOrEmpty(user.SchoolId))
            {
                var schoolStatus = await _db.Schools.AsNoTracking().Where(s => s.SchoolId == user.SchoolId).Select(s => s.SchoolStatusId).FirstOrDefaultAsync(ct);
                schoolActive = activeId != null && schoolStatus == activeId;
            }
            return new PasswordResetTarget(user, roles, activeId != null && user.StatusId == activeId && schoolActive);
        }

        public async Task SaveResetAsync(TbPasswordResetAudit audit, long userId, DateTime now, CancellationToken ct)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            _db.PasswordResetAudit.Add(audit);
            await _db.SaveChangesAsync(ct);   // the user row (new hash, first-login flag) and the audit row
            await _db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
            await tx.CommitAsync(ct);
        }
    }
}
