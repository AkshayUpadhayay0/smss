using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class OnboardingRepository : IOnboardingRepository
    {
        private readonly dbContext _db;
        public OnboardingRepository(dbContext db) => _db = db;

        public Task<int?> ActiveStatusIdAsync(CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == "Active" && s.Stype == "general status").Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public Task<bool?> IsFirstLoginAsync(string schoolId, long userId, CancellationToken ct) =>
            _db.Users.AsNoTracking().Where(u => u.UserId == userId && u.SchoolId == schoolId).Select(u => (bool?)u.IsFirstLogin).FirstOrDefaultAsync(ct);

        public Task<bool> HasActiveStudentAsync(string schoolId, int activeStatusId, CancellationToken ct) =>
            _db.Students.AsNoTracking().AnyAsync(s => s.SchoolId == schoolId && s.StatusId == activeStatusId, ct);

        public Task<long?> CurrentYearIdAsync(string schoolId, int activeStatusId, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.IsCurrent && y.StatusId == activeStatusId)
                .OrderByDescending(y => y.StartDate).Select(y => (long?)y.AcademicYearId).FirstOrDefaultAsync(ct);

        // The checklist judges what the CURRENT YEAR offers (tb_academic_year_classes / _sections), not the masters: a class that exists but is not offered this year does not count.
        public async Task<List<OnboardingClassRow>> ActiveClassesAsync(string schoolId, long? academicYearId, int activeStatusId, CancellationToken ct)
        {
            if (academicYearId == null) return new();
            var rows = await (from c in _db.Classes.AsNoTracking()
                              join o in _db.AcademicYearClasses.AsNoTracking() on c.ClassId equals o.ClassId
                              where c.SchoolId == schoolId && c.StatusId == activeStatusId && o.SchoolId == schoolId && o.AcademicYearId == academicYearId && o.StatusId == activeStatusId
                              orderby c.SequenceOrder, c.ClassName
                              select new { c.ClassId, c.ClassName }).ToListAsync(ct);
            return rows.Select(c => new OnboardingClassRow(c.ClassId, c.ClassName)).ToList();
        }

        public async Task<HashSet<long>> ClassIdsWithActiveSectionAsync(string schoolId, long? academicYearId, int activeStatusId, CancellationToken ct)
        {
            if (academicYearId == null) return new();
            var ids = await (from s in _db.Sections.AsNoTracking()
                             join o in _db.AcademicYearSections.AsNoTracking() on s.SectionId equals o.SectionId
                             where s.SchoolId == schoolId && s.StatusId == activeStatusId && o.SchoolId == schoolId && o.AcademicYearId == academicYearId && o.StatusId == activeStatusId
                             select s.ClassId).Distinct().ToListAsync(ct);
            return ids.ToHashSet();
        }

        public Task<bool> HasActiveFeeTypeAsync(string schoolId, CancellationToken ct) =>
            _db.FeeTypes.AsNoTracking().AnyAsync(t => t.SchoolId == schoolId && t.IsActive, ct);

        public async Task<HashSet<long>> ClassIdsWithFeeStructureAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            (await _db.FeeStructures.AsNoTracking()
                .Where(f => f.SchoolId == schoolId && f.AcademicYearId == academicYearId && f.IsActive && f.Details.Any())
                .Select(f => f.ClassId).Distinct().ToListAsync(ct)).ToHashSet();
    }
}
