using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class GuardianRepository : IGuardianRepository
    {
        private readonly dbContext _db;
        public GuardianRepository(dbContext db) => _db = db;

        private static string Name(string first, string? middle, string? last) =>
            string.Join(' ', new[] { first, middle, last }.Where(p => !string.IsNullOrWhiteSpace(p)));

        public Task<int?> GetGeneralStatusIdAsync(string name, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == name && s.Stype == "general status").Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public Task<TbGuardian?> GetGuardianAsync(string schoolId, long guardianId, bool tracked, CancellationToken ct)
        {
            var q = tracked ? _db.Guardians.AsTracking() : _db.Guardians.AsNoTracking();
            return q.Where(g => g.SchoolId == schoolId && g.GuardianId == guardianId).FirstOrDefaultAsync(ct);
        }

        public Task<List<TbGuardian>> GetGuardiansAsync(string schoolId, IReadOnlyCollection<long> guardianIds, bool tracked, CancellationToken ct)
        {
            var q = tracked ? _db.Guardians.AsTracking() : _db.Guardians.AsNoTracking();
            return q.Where(g => g.SchoolId == schoolId && guardianIds.Contains(g.GuardianId)).ToListAsync(ct);
        }

        public async Task<List<GuardianStudentRef>> GetLinkedStudentsAsync(string schoolId, long guardianId, int activeStudentStatusId, CancellationToken ct) =>
            (await (from l in _db.StudentGuardians.AsNoTracking()
                    where l.GuardianId == guardianId
                    join s in _db.Students.AsNoTracking() on l.StudentId equals s.StudentId
                    where s.SchoolId == schoolId
                    orderby s.FirstName, s.StudentId
                    select new { s.StudentId, s.FirstName, s.MiddleName, s.LastName, l.IsPrimary, Active = s.StatusId == activeStudentStatusId }).ToListAsync(ct))
            .Select(x => new GuardianStudentRef(x.StudentId, Name(x.FirstName, x.MiddleName, x.LastName), x.IsPrimary, x.Active)).ToList();

        public async Task<long?> GetViewedYearIdAsync(string schoolId, DateOnly today, CancellationToken ct)
        {
            var active = await GetGeneralStatusIdAsync("Active", ct);
            var current = await _db.AcademicYears.AsNoTracking().Where(x => x.SchoolId == schoolId && x.IsCurrent && x.StatusId == active)
                .OrderByDescending(x => x.StartDate).Select(x => (long?)x.AcademicYearId).FirstOrDefaultAsync(ct);
            if (current != null) return current;
            return await _db.AcademicYears.AsNoTracking().Where(x => x.SchoolId == schoolId && x.StartDate <= today && x.EndDate >= today)
                .OrderByDescending(x => x.StartDate).Select(x => (long?)x.AcademicYearId).FirstOrDefaultAsync(ct);
        }

        /// <summary>Active guardians who are the primary guardian of at least one active student and lack a WhatsApp number or an email.</summary>
        private IQueryable<long> IncompleteIds(string schoolId, int activeStatusId) =>
            (from l in _db.StudentGuardians.AsNoTracking()
             where l.IsPrimary
             join g in _db.Guardians.AsNoTracking() on l.GuardianId equals g.GuardianId
             where g.SchoolId == schoolId && g.StatusId == activeStatusId && (g.WhatsAppNumber == null || g.Email == null || g.Email.Trim() == "")
             join s in _db.Students.AsNoTracking() on l.StudentId equals s.StudentId
             where s.SchoolId == schoolId && s.StatusId == activeStatusId
             select g.GuardianId).Distinct();

        public Task<int> CountIncompletePrimaryAsync(string schoolId, int activeStatusId, CancellationToken ct) => IncompleteIds(schoolId, activeStatusId).CountAsync(ct);

        public async Task<List<IncompleteGuardianRow>> ListIncompletePrimaryAsync(string schoolId, int activeStatusId, long? viewedYearId, CancellationToken ct)
        {
            var ids = await IncompleteIds(schoolId, activeStatusId).ToListAsync(ct);
            if (ids.Count == 0) return new List<IncompleteGuardianRow>();
            var guardians = await _db.Guardians.AsNoTracking().Where(g => g.SchoolId == schoolId && ids.Contains(g.GuardianId))
                .Select(g => new { g.GuardianId, g.FirstName, g.LastName, g.Relationship, g.MobileNumber, g.WhatsAppNumber, g.Email }).ToListAsync(ct);
            // the students the guardian is PRIMARY for, active only
            var links = await (from l in _db.StudentGuardians.AsNoTracking()
                               where l.IsPrimary && ids.Contains(l.GuardianId)
                               join s in _db.Students.AsNoTracking() on l.StudentId equals s.StudentId
                               where s.SchoolId == schoolId && s.StatusId == activeStatusId
                               select new { l.GuardianId, s.StudentId, s.FirstName, s.MiddleName, s.LastName }).ToListAsync(ct);
            var studentIds = links.Select(l => l.StudentId).Distinct().ToList();
            var classes = new Dictionary<string, (long ClassId, string ClassName, long SectionId, string SectionName)>();
            if (viewedYearId != null)
            {
                var rows = await (from e in _db.StudentEnrollments.AsNoTracking()
                                  where e.SchoolId == schoolId && e.AcademicYearId == viewedYearId && studentIds.Contains(e.StudentId)
                                  join sec in _db.Sections.AsNoTracking() on e.ClassSectionId equals sec.SectionId
                                  join c in _db.Classes.AsNoTracking() on sec.ClassId equals c.ClassId
                                  select new { e.StudentId, e.EnrollmentId, c.ClassId, c.ClassName, sec.SectionId, sec.SectionName }).ToListAsync(ct);
                foreach (var g in rows.GroupBy(r => r.StudentId)) { var r = g.OrderByDescending(x => x.EnrollmentId).First(); classes[g.Key] = (r.ClassId, r.ClassName, r.SectionId, r.SectionName); }
            }
            return guardians.Select(g => new IncompleteGuardianRow(g.GuardianId, Name(g.FirstName, null, g.LastName), g.Relationship, g.MobileNumber, g.WhatsAppNumber, g.Email,
                links.Where(l => l.GuardianId == g.GuardianId).OrderBy(l => l.FirstName).ThenBy(l => l.StudentId).Select(l =>
                {
                    classes.TryGetValue(l.StudentId, out var c);
                    var has = classes.ContainsKey(l.StudentId);
                    return new IncompleteStudentRef(l.StudentId, Name(l.FirstName, l.MiddleName, l.LastName), has ? c.ClassId : null, has ? c.ClassName : null, has ? c.SectionId : null, has ? c.SectionName : null);
                }).ToList()))
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.GuardianId).ToList();
        }

        public async Task AddChangesAsync(IEnumerable<TbGuardianChange> changes, CancellationToken ct) => await _db.GuardianChanges.AddRangeAsync(changes, ct);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
    }
}
