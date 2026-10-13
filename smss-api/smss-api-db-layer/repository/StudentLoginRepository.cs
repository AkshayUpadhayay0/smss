using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class StudentLoginRepository : IStudentLoginRepository
    {
        private readonly dbContext _db;
        public StudentLoginRepository(dbContext db) => _db = db;

        public Task<TbStudent?> GetStudentAsync(string schoolId, string studentId, CancellationToken ct) =>
            _db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.SchoolId == schoolId && s.StudentId == studentId, ct);

        public Task<TbSchools?> GetSchoolAsync(string schoolId, CancellationToken ct) =>
            _db.Schools.AsNoTracking().FirstOrDefaultAsync(s => s.SchoolId == schoolId, ct);

        public Task<bool> StudentHasLoginAsync(string schoolId, string studentId, CancellationToken ct) =>
            _db.Users.AnyAsync(u => u.SchoolId == schoolId && u.OrgUserId == studentId, ct);

        public async Task<string?> GetPrimaryGuardianEmailAsync(string schoolId, string studentId, CancellationToken ct)
        {
            var email = await (
                from sg in _db.StudentGuardians.AsNoTracking()
                join g in _db.Guardians.AsNoTracking() on sg.GuardianId equals g.GuardianId
                where sg.StudentId == studentId && sg.IsPrimary && g.SchoolId == schoolId
                select g.Email).FirstOrDefaultAsync(ct);
            return string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        }

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking()
                .Where(s => s.Sname == statusName && s.Stype == statusType)
                .Select(s => (int?)s.Sid)
                .FirstOrDefaultAsync(ct);

        public Task<long?> GetRoleIdByNameAsync(string roleName, CancellationToken ct) =>
            _db.LutRoles.AsNoTracking()
                .Where(r => r.RoleName == roleName)
                .Select(r => (long?)r.RoleId)
                .FirstOrDefaultAsync(ct);

        // One SaveChanges = one transaction: user + its role row
        public async Task AddUserAsync(TbUsers user, CancellationToken ct)
        {
            _db.Users.Add(user);   // includes user.UserRoles
            await _db.SaveChangesAsync(ct);
        }
    }
}
