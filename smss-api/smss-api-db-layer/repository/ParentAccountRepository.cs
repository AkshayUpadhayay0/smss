using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class ParentAccountRepository : IParentAccountRepository
    {
        private readonly dbContext _db;
        public ParentAccountRepository(dbContext db) => _db = db;

        public Task<TbUsers?> GetUserAsync(long userId, bool tracked, CancellationToken ct) =>
            (tracked ? _db.Users.AsTracking() : _db.Users.AsNoTracking()).Where(u => u.UserId == userId).FirstOrDefaultAsync(ct);

        public async Task AddUserAsync(TbUsers user, CancellationToken ct) { await _db.Users.AddAsync(user, ct); await _db.SaveChangesAsync(ct); }

        public async Task<long?> GetRoleIdByNameAsync(string roleName, CancellationToken ct) =>
            await _db.LutRoles.AsNoTracking().Where(r => r.RoleName == roleName).Select(r => (long?)r.RoleId).FirstOrDefaultAsync(ct);

        public Task<string?> GetSchoolNameAsync(string schoolId, CancellationToken ct) =>
            _db.Schools.AsNoTracking().Where(s => s.SchoolId == schoolId).Select(s => (string?)s.SchoolName).FirstOrDefaultAsync(ct);

        private static string Full(string first, string? middle, string? last) => string.Join(' ', new[] { first, middle, last }.Where(p => !string.IsNullOrWhiteSpace(p)));

        public async Task<string?> GetStudentNameAsync(string schoolId, string studentId, CancellationToken ct)
        {
            var s = await _db.Students.AsNoTracking().Where(x => x.SchoolId == schoolId && x.StudentId == studentId).Select(x => new { x.FirstName, x.MiddleName, x.LastName }).FirstOrDefaultAsync(ct);
            return s == null ? null : Full(s.FirstName, s.MiddleName, s.LastName);
        }

        public async Task<string?> GetPrimaryChildNameAsync(string schoolId, long guardianId, int activeStudentStatusId, CancellationToken ct)
        {
            var s = await (from l in _db.StudentGuardians.AsNoTracking()
                           join st in _db.Students.AsNoTracking() on l.StudentId equals st.StudentId
                           where l.GuardianId == guardianId && st.SchoolId == schoolId && st.StatusId == activeStudentStatusId
                           orderby l.IsPrimary descending, st.StudentId
                           select new { st.FirstName, st.MiddleName, st.LastName }).FirstOrDefaultAsync(ct);
            return s == null ? null : Full(s.FirstName, s.MiddleName, s.LastName);
        }

        public Task<TbGuardian?> GetPrimaryGuardianOfStudentAsync(string schoolId, string studentId, CancellationToken ct) =>
            (from l in _db.StudentGuardians.AsNoTracking()
             join g in _db.Guardians.AsNoTracking() on l.GuardianId equals g.GuardianId
             join st in _db.Students.AsNoTracking() on l.StudentId equals st.StudentId
             where l.StudentId == studentId && l.IsPrimary && st.SchoolId == schoolId && g.SchoolId == schoolId
             select g).FirstOrDefaultAsync(ct);

        public Task<bool> IsLinkedAsync(string schoolId, long guardianId, string studentId, CancellationToken ct) =>
            (from l in _db.StudentGuardians.AsNoTracking()
             join st in _db.Students.AsNoTracking() on l.StudentId equals st.StudentId
             where l.GuardianId == guardianId && l.StudentId == studentId && st.SchoolId == schoolId
             select l.StudentGuardianId).AnyAsync(ct);

        public Task<List<AccountlessGuardianRow>> ListAccountlessGuardiansAsync(string schoolId, int activeStatusId, CancellationToken ct) =>
            (from g in _db.Guardians.AsNoTracking()
             where g.SchoolId == schoolId && g.StatusId == activeStatusId && g.MergedIntoGuardianId == null && g.UserId == null
                   && (from l in _db.StudentGuardians
                       join st in _db.Students on l.StudentId equals st.StudentId
                       where l.GuardianId == g.GuardianId && st.SchoolId == schoolId && st.StatusId == activeStatusId
                       select l.StudentGuardianId).Any()
             orderby g.GuardianId
             select new AccountlessGuardianRow(g.GuardianId, g.FirstName, g.WhatsAppNumber, g.Email, g.ConsentGivenAt)).ToListAsync(ct);

        public Task<List<AccountContactRow>> FindAccountsByContactAsync(string? whatsAppNumber, string? emailLower, int activeStatusId, CancellationToken ct) =>
            (from g in _db.Guardians.AsNoTracking()
             join u in _db.Users.AsNoTracking() on g.UserId equals u.UserId
             join s in _db.Schools.AsNoTracking() on g.SchoolId equals s.SchoolId
             where g.UserId != null && g.StatusId == activeStatusId && g.MergedIntoGuardianId == null && u.StatusId == activeStatusId && s.SchoolStatusId == activeStatusId
                   && ((whatsAppNumber != null && g.WhatsAppNumber == whatsAppNumber) || (emailLower != null && g.Email != null && g.Email.ToLower() == emailLower))
             orderby g.GuardianId
             select new AccountContactRow(g.GuardianId, g.SchoolId, s.SchoolName, u.UserId, g.FirstName, u.OrgUserId!, g.WhatsAppNumber, g.Email)).ToListAsync(ct);

        public Task<GuardianLoginInfo?> GetGuardianByUserIdAsync(long userId, CancellationToken ct) =>
            _db.Guardians.AsNoTracking().Where(g => g.UserId == userId).Select(g => new GuardianLoginInfo(g.GuardianId, g.SchoolId, g.StatusId, g.MergedIntoGuardianId)).FirstOrDefaultAsync(ct);

        public Task<List<TbMessageDelivery>> GetRecentDeliveriesAsync(string schoolId, long guardianId, int take, CancellationToken ct) =>
            _db.MessageDeliveries.AsNoTracking().Where(d => d.SchoolId == schoolId && d.GuardianId == guardianId).OrderByDescending(d => d.DeliveryId).Take(take).ToListAsync(ct);
    }
}
