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

        public async Task<List<SiblingStudentRow>> SearchSiblingStudentsAsync(string schoolId, int activeStatusId, long viewedYearId, long? classId, string? search, int limit, CancellationToken ct)
        {
            var q = from e in _db.StudentEnrollments.AsNoTracking()
                    where e.SchoolId == schoolId && e.AcademicYearId == viewedYearId
                    join s in _db.Students.AsNoTracking() on e.StudentId equals s.StudentId
                    where s.SchoolId == schoolId && s.StatusId == activeStatusId
                    join sec in _db.Sections.AsNoTracking() on e.ClassSectionId equals sec.SectionId
                    join c in _db.Classes.AsNoTracking() on sec.ClassId equals c.ClassId
                    select new { s.StudentId, s.FirstName, s.MiddleName, s.LastName, s.AdmissionNumber, c.ClassId, c.ClassName, sec.SectionName };
            if (classId != null) q = q.Where(x => x.ClassId == classId);
            var term = search?.Trim();
            if (!string.IsNullOrEmpty(term))
            {
                var like = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                q = q.Where(x => EF.Functions.ILike(x.FirstName + " " + (x.MiddleName ?? "") + " " + (x.LastName ?? ""), like) || EF.Functions.ILike(x.AdmissionNumber, like) || EF.Functions.ILike(x.StudentId, like));
            }
            var students = await q.OrderBy(x => x.FirstName).ThenBy(x => x.StudentId).Take(limit).ToListAsync(ct);
            if (students.Count == 0) return new List<SiblingStudentRow>();
            var ids = students.Select(s => s.StudentId).ToList();
            var links = await (from l in _db.StudentGuardians.AsNoTracking()
                               where ids.Contains(l.StudentId)
                               join g in _db.Guardians.AsNoTracking() on l.GuardianId equals g.GuardianId
                               where g.SchoolId == schoolId && g.StatusId == activeStatusId && g.MergedIntoGuardianId == null
                               orderby l.IsPrimary descending, g.GuardianId
                               select new { l.StudentId, l.Relationship, l.IsPrimary, g.GuardianId, g.FirstName, g.LastName, g.MobileNumber, g.WhatsAppNumber, g.Email }).ToListAsync(ct);
            return students.Select(s => new SiblingStudentRow(s.StudentId, Name(s.FirstName, s.MiddleName, s.LastName), s.ClassName, s.SectionName,
                links.Where(l => l.StudentId == s.StudentId).Select(l => new SiblingGuardianRow(l.GuardianId, Name(l.FirstName, null, l.LastName), l.Relationship, l.MobileNumber,
                    !string.IsNullOrWhiteSpace(l.WhatsAppNumber), !string.IsNullOrWhiteSpace(l.Email), l.IsPrimary)).ToList())).ToList();
        }

        // ---------------------------------------------------------------- duplicates and merge
        private sealed class KeyRow { public long GuardianId { get; set; } public string Key { get; set; } = null!; }

        public async Task<List<DuplicateGuardianRow>> ListDuplicateCandidatesAsync(string schoolId, int activeStatusId, long? viewedYearId, CancellationToken ct)
        {
            // the keys shared by two or more active, not merged guardians of THIS school (a raw query: fn_phone_key is a database function)
            var keyed = await _db.Database.SqlQuery<KeyRow>($@"SELECT g.guardian_id AS ""GuardianId"", public.fn_phone_key(g.mobile_number) AS ""Key"" FROM public.tb_guardians g
                WHERE g.school_id = {schoolId} AND g.status_id = {activeStatusId} AND g.merged_into_guardian_id IS NULL AND public.fn_phone_key(g.mobile_number) IS NOT NULL").ToListAsync(ct);
            var shared = keyed.GroupBy(k => k.Key).Where(g => g.Count() > 1).SelectMany(g => g).ToList();
            if (shared.Count == 0) return new List<DuplicateGuardianRow>();
            var ids = shared.Select(s => s.GuardianId).ToList();
            var guardians = await _db.Guardians.AsNoTracking().Where(g => g.SchoolId == schoolId && ids.Contains(g.GuardianId))
                .Select(g => new { g.GuardianId, g.FirstName, g.LastName, g.Relationship, g.MobileNumber, g.WhatsAppNumber, g.Email, g.UserId, g.CreatedAt }).ToListAsync(ct);
            var links = await (from l in _db.StudentGuardians.AsNoTracking() where ids.Contains(l.GuardianId) select new { l.GuardianId, l.StudentId, l.IsPrimary }).ToListAsync(ct);
            var refs = await GetStudentRefsAsync(schoolId, links.Select(l => l.StudentId).Distinct().ToList(), viewedYearId, ct);
            return guardians.Select(g => new DuplicateGuardianRow(g.GuardianId, Name(g.FirstName, null, g.LastName), g.Relationship, g.MobileNumber, shared.First(s => s.GuardianId == g.GuardianId).Key,
                    !string.IsNullOrWhiteSpace(g.WhatsAppNumber), !string.IsNullOrWhiteSpace(g.Email), g.UserId != null, g.CreatedAt,
                    links.Where(l => l.GuardianId == g.GuardianId && refs.ContainsKey(l.StudentId)).Select(l => { var r = refs[l.StudentId]; return new DuplicateStudentRef(r.StudentId, r.StudentName, r.AdmissionNumber, r.ClassName, r.SectionName, l.IsPrimary); }).OrderBy(s => s.StudentName).ToList()))
                .OrderBy(r => r.PhoneKey).ThenBy(r => r.GuardianId).ToList();
        }

        public Task<List<TbGuardian>> LockGuardiansAsync(string schoolId, IReadOnlyCollection<long> guardianIds, CancellationToken ct)
        {
            var ids = guardianIds.Distinct().OrderBy(i => i).ToArray();
            return _db.Guardians.FromSqlInterpolated($"SELECT * FROM public.tb_guardians WHERE school_id = {schoolId} AND guardian_id = ANY({ids}) ORDER BY guardian_id FOR UPDATE").AsTracking().ToListAsync(ct);
        }

        public Task<List<TbStudentGuardian>> GetLinksOfGuardianAsync(string schoolId, long guardianId, bool tracked, CancellationToken ct)
        {
            var q = tracked ? _db.StudentGuardians.AsTracking() : _db.StudentGuardians.AsNoTracking();
            return q.Where(l => l.GuardianId == guardianId && _db.Students.Any(s => s.StudentId == l.StudentId && s.SchoolId == schoolId)).OrderBy(l => l.StudentId).ToListAsync(ct);
        }

        public Task<List<TbStudentGuardian>> GetLinksToStudentsAsync(string schoolId, IReadOnlyCollection<string> studentIds, long guardianId, bool tracked, CancellationToken ct)
        {
            var q = tracked ? _db.StudentGuardians.AsTracking() : _db.StudentGuardians.AsNoTracking();
            return q.Where(l => l.GuardianId == guardianId && studentIds.Contains(l.StudentId) && _db.Students.Any(s => s.StudentId == l.StudentId && s.SchoolId == schoolId)).ToListAsync(ct);
        }

        public async Task<Dictionary<string, MergeStudentRef>> GetStudentRefsAsync(string schoolId, IReadOnlyCollection<string> studentIds, long? viewedYearId, CancellationToken ct)
        {
            var people = await _db.Students.AsNoTracking().Where(s => s.SchoolId == schoolId && studentIds.Contains(s.StudentId))
                .Select(s => new { s.StudentId, s.FirstName, s.MiddleName, s.LastName, s.AdmissionNumber }).ToListAsync(ct);
            var classes = new Dictionary<string, (string Class, string Section)>();
            var rows = await (from e in _db.StudentEnrollments.AsNoTracking()
                              where e.SchoolId == schoolId && studentIds.Contains(e.StudentId) && (viewedYearId == null || e.AcademicYearId == viewedYearId)
                              join sec in _db.Sections.AsNoTracking() on e.ClassSectionId equals sec.SectionId
                              join c in _db.Classes.AsNoTracking() on sec.ClassId equals c.ClassId
                              select new { e.StudentId, e.EnrollmentId, c.ClassName, sec.SectionName }).ToListAsync(ct);
            foreach (var g in rows.GroupBy(r => r.StudentId)) { var r = g.OrderByDescending(x => x.EnrollmentId).First(); classes[g.Key] = (r.ClassName, r.SectionName); }
            return people.ToDictionary(p => p.StudentId, p => new MergeStudentRef(p.StudentId, Name(p.FirstName, p.MiddleName, p.LastName), p.AdmissionNumber,
                classes.TryGetValue(p.StudentId, out var c) ? c.Class : null, classes.TryGetValue(p.StudentId, out var c2) ? c2.Section : null));
        }

        public Task<List<TbAdmission>> GetAdmissionsOfGuardianAsync(string schoolId, long guardianId, bool tracked, CancellationToken ct)
        {
            var q = tracked ? _db.Admissions.AsTracking() : _db.Admissions.AsNoTracking();
            return q.Where(a => a.SchoolId == schoolId && a.GuardianId == guardianId).OrderBy(a => a.AdmissionId).ToListAsync(ct);
        }

        public Task<List<TbAdmissionGuardian>> GetAdmissionGuardianRowsAsync(string schoolId, IReadOnlyCollection<long> admissionIds, bool tracked, CancellationToken ct)
        {
            var q = tracked ? _db.AdmissionGuardians.AsTracking() : _db.AdmissionGuardians.AsNoTracking();
            return q.Where(a => a.SchoolId == schoolId && admissionIds.Contains(a.AdmissionId)).ToListAsync(ct);
        }

        public Task<List<TbAdmissionGuardian>> GetAdmissionGuardianRowsOfGuardianAsync(string schoolId, long guardianId, bool tracked, CancellationToken ct)
        {
            var q = tracked ? _db.AdmissionGuardians.AsTracking() : _db.AdmissionGuardians.AsNoTracking();
            return q.Where(a => a.SchoolId == schoolId && a.GuardianId == guardianId).OrderBy(a => a.AdmissionId).ToListAsync(ct);
        }

        public Task<List<TbGuardian>> GetGuardiansMergedIntoAsync(string schoolId, long guardianId, bool tracked, CancellationToken ct)
        {
            var q = tracked ? _db.Guardians.AsTracking() : _db.Guardians.AsNoTracking();
            return q.Where(g => g.SchoolId == schoolId && g.MergedIntoGuardianId == guardianId).OrderBy(g => g.GuardianId).ToListAsync(ct);
        }

        public Task<List<TbAdmission>> GetAdmissionsByIdsAsync(string schoolId, IReadOnlyCollection<long> admissionIds, CancellationToken ct) =>
            _db.Admissions.AsNoTracking().Where(a => a.SchoolId == schoolId && admissionIds.Contains(a.AdmissionId)).ToListAsync(ct);

        public void RemoveLink(TbStudentGuardian link) => _db.StudentGuardians.Remove(link);
        public void RemoveAdmissionGuardian(TbAdmissionGuardian row) => _db.AdmissionGuardians.Remove(row);
        public async Task AddAdmissionGuardianAsync(TbAdmissionGuardian row, CancellationToken ct) => await _db.AdmissionGuardians.AddAsync(row, ct);
        public async Task AddMergeAsync(TbGuardianMerge merge, CancellationToken ct) => await _db.GuardianMerges.AddAsync(merge, ct);

        public async Task AddChangesAsync(IEnumerable<TbGuardianChange> changes, CancellationToken ct) => await _db.GuardianChanges.AddRangeAsync(changes, ct);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
    }
}
