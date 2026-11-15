using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class StudentImportRepository : IStudentImportRepository
    {
        private readonly dbContext _db;
        public StudentImportRepository(dbContext db) => _db = db;

        public Task<int?> GetActiveStatusIdAsync(CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == "Active" && s.Stype == "general status").Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public Task<LutAdmissionType?> GetAdmissionTypeByCodeAsync(string code, CancellationToken ct)
        {
            var wanted = code.Trim().ToUpper();
            return _db.LutAdmissionTypes.AsNoTracking().Where(t => t.TypeCode.ToUpper() == wanted).FirstOrDefaultAsync(ct);
        }

        public Task<TbAcademicYears?> GetCurrentYearAsync(string schoolId, int activeStatusId, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.IsCurrent && y.StatusId == activeStatusId).FirstOrDefaultAsync(ct);

        public Task<string?> GetSchoolShortCodeAsync(string schoolId, CancellationToken ct) =>
            _db.Schools.AsNoTracking().Where(s => s.SchoolId == schoolId).Select(s => s.SchoolShortCode).FirstOrDefaultAsync(ct);

        public async Task<List<ImportClassRow>> ListClassesAsync(string schoolId, int activeStatusId, CancellationToken ct) =>
            (await _db.Classes.AsNoTracking().Where(c => c.SchoolId == schoolId && c.StatusId == activeStatusId).OrderBy(c => c.SequenceOrder).ThenBy(c => c.ClassName)
                .Select(c => new { c.ClassId, c.ClassName, c.SequenceOrder }).ToListAsync(ct))
            .Select(c => new ImportClassRow(c.ClassId, c.ClassName, c.SequenceOrder)).ToList();

        public async Task<List<ImportSectionRow>> ListSectionsAsync(string schoolId, int activeStatusId, CancellationToken ct) =>
            (await _db.Sections.AsNoTracking().Where(s => s.SchoolId == schoolId && s.StatusId == activeStatusId).OrderBy(s => s.SectionName)
                .Select(s => new { s.SectionId, s.ClassId, s.SectionName }).ToListAsync(ct))
            .Select(s => new ImportSectionRow(s.SectionId, s.ClassId, s.SectionName)).ToList();

        public async Task<List<ImportGenderRow>> ListGendersAsync(CancellationToken ct) =>
            (await _db.LutGenders.AsNoTracking().Where(g => g.IsActive).OrderBy(g => g.GenderId).Select(g => new { g.GenderId, g.GenderName, g.GenderCode }).ToListAsync(ct))
            .Select(g => new ImportGenderRow((int)g.GenderId, g.GenderName, g.GenderCode)).ToList();

        public async Task<HashSet<string>> ExistingAdmissionNumbersAsync(string schoolId, IReadOnlyCollection<string> numbers, CancellationToken ct)
        {
            if (numbers.Count == 0) return new HashSet<string>();
            var list = numbers.ToList();
            return (await _db.Students.AsNoTracking().Where(s => s.SchoolId == schoolId && list.Contains(s.AdmissionNumber)).Select(s => s.AdmissionNumber).ToListAsync(ct)).ToHashSet();
        }

        public async Task<List<ImportRollRow>> ExistingRollsAsync(string schoolId, long academicYearId, IReadOnlyCollection<long> sectionIds, CancellationToken ct)
        {
            if (sectionIds.Count == 0) return new();
            var ids = sectionIds.ToList();
            return (await _db.StudentEnrollments.AsNoTracking()
                .Where(e => e.SchoolId == schoolId && e.AcademicYearId == academicYearId && ids.Contains(e.ClassSectionId) && e.RollNumber != null)
                .Select(e => new { e.ClassSectionId, e.RollNumber }).ToListAsync(ct))
                .Select(e => new ImportRollRow(e.ClassSectionId, e.RollNumber!)).ToList();
        }

        public async Task<List<ImportPersonRow>> FindPeopleBornOnAsync(string schoolId, IReadOnlyCollection<DateTime> dates, CancellationToken ct)
        {
            if (dates.Count == 0) return new();
            var days = dates.Select(d => d.Date).Distinct().ToList();
            var students = await _db.Students.AsNoTracking().Where(s => s.SchoolId == schoolId && days.Contains(s.DateOfBirth))
                .Select(s => new { s.StudentId, s.FirstName, s.LastName, s.DateOfBirth }).ToListAsync(ct);
            if (students.Count == 0) return new();
            var ids = students.Select(s => s.StudentId).ToList();
            var phones = await (from l in _db.StudentGuardians.AsNoTracking()
                                join g in _db.Guardians.AsNoTracking() on l.GuardianId equals g.GuardianId
                                where ids.Contains(l.StudentId)
                                select new { l.StudentId, g.MobileNumber }).ToListAsync(ct);
            var by = phones.GroupBy(p => p.StudentId).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.MobileNumber).ToList());
            return students.Select(s => new ImportPersonRow(s.StudentId, s.FirstName, s.LastName, s.DateOfBirth, by.GetValueOrDefault(s.StudentId, Array.Empty<string>()))).ToList();
        }

        public Task<int> CountStudentsAsync(string schoolId, CancellationToken ct) => _db.Students.CountAsync(s => s.SchoolId == schoolId, ct);

        public async Task<int> MaxAdmissionNumberSequenceAsync(string schoolId, string prefix, CancellationToken ct)
        {
            var existing = await _db.Students.AsNoTracking().Where(s => s.SchoolId == schoolId && s.AdmissionNumber.StartsWith(prefix)).Select(s => s.AdmissionNumber).ToListAsync(ct);
            return existing.Select(n => int.TryParse(n[prefix.Length..], out var v) ? v : 0).DefaultIfEmpty(0).Max();
        }

        public async Task LockSchoolImportAsync(string schoolId, CancellationToken ct)
        {
            // the same key the single-student flows use for admission numbers, so an import and a direct entry cannot allocate the same number
            var key = "admission-no:" + schoolId;
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);
        public void AddRange(IEnumerable<object> entities) => _db.AddRange(entities);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
        public void ClearTracking() => _db.ChangeTracker.Clear();
    }
}
