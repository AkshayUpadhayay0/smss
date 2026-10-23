using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class SchoolEventRepository : ISchoolEventRepository
    {
        private readonly dbContext _db;
        public SchoolEventRepository(dbContext db) => _db = db;

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == statusName && s.Stype == statusType).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public Task<EventYearRow?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.AcademicYearId == academicYearId)
                .Select(y => new EventYearRow(y.AcademicYearId, y.YearName, y.StartDate, y.EndDate)).FirstOrDefaultAsync(ct);

        public Task<EventYearRow?> GetYearContainingAsync(string schoolId, DateOnly date, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.StartDate <= date && y.EndDate >= date)
                .OrderByDescending(y => y.IsCurrent).ThenByDescending(y => y.StartDate)
                .Select(y => new EventYearRow(y.AcademicYearId, y.YearName, y.StartDate, y.EndDate)).FirstOrDefaultAsync(ct);

        public Task<EventCategoryRow?> GetActiveCategoryAsync(long categoryId, CancellationToken ct) =>
            _db.LutEventCategories.AsNoTracking().Where(c => c.EventCategoryId == categoryId && c.IsActive)
                .Select(c => new EventCategoryRow(c.EventCategoryId, c.CategoryName, c.CategoryCode, c.ColorHex)).FirstOrDefaultAsync(ct);

        public Task<List<EventCategoryRow>> GetActiveCategoriesAsync(CancellationToken ct) =>
            _db.LutEventCategories.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.EventCategoryId)
                .Select(c => new EventCategoryRow(c.EventCategoryId, c.CategoryName, c.CategoryCode, c.ColorHex)).ToListAsync(ct);

        public async Task AddAsync(TbSchoolEvent ev, CancellationToken ct) => await _db.SchoolEvents.AddAsync(ev, ct);

        public Task<TbSchoolEvent?> GetActiveForUpdateAsync(string schoolId, long eventId, int activeStatusId, CancellationToken ct) =>
            _db.SchoolEvents.Where(e => e.SchoolId == schoolId && e.EventId == eventId && e.StatusId == activeStatusId).FirstOrDefaultAsync(ct);

        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

        private IQueryable<EventRow> Rows(IQueryable<TbSchoolEvent> source) =>
            from e in source
            join c in _db.LutEventCategories.AsNoTracking() on e.EventCategoryId equals c.EventCategoryId
            select new EventRow(e, c.CategoryName, c.CategoryCode, c.ColorHex,
                _db.Users.Where(u => u.UserId == e.CreatedBy).Select(u => u.DisplayName ?? u.OrgUserId).FirstOrDefault());

        public Task<EventRow?> GetRowAsync(string schoolId, long eventId, CancellationToken ct) =>
            Rows(_db.SchoolEvents.AsNoTracking().Where(e => e.SchoolId == schoolId && e.EventId == eventId)).FirstOrDefaultAsync(ct);

        public async Task<List<EventRow>> GetOverlappingAsync(string schoolId, long? academicYearId, DateOnly from, DateOnly to, int activeStatusId, CancellationToken ct)
        {
            var rows = await Rows(_db.SchoolEvents.AsNoTracking().Where(e => e.SchoolId == schoolId && e.StatusId == activeStatusId
                && e.StartDate <= to && e.EndDate >= from && (academicYearId == null || e.AcademicYearId == academicYearId))).ToListAsync(ct);
            return rows.OrderBy(r => r.Event.StartDate).ThenBy(r => r.Event.StartTime ?? TimeOnly.MinValue).ThenBy(r => r.Event.EventId).ToList();
        }

        public async Task<List<EventRow>> GetUpcomingAsync(string schoolId, DateOnly today, int take, int activeStatusId, CancellationToken ct)
        {
            // end_date >= today keeps a multi-day event that has already started; all-day first on a day, then by time
            var rows = await Rows(_db.SchoolEvents.AsNoTracking().Where(e => e.SchoolId == schoolId && e.StatusId == activeStatusId && e.EndDate >= today)
                .OrderBy(e => e.StartDate).ThenBy(e => e.EventId).Take(take * 3 + 20)).ToListAsync(ct);   // a small over-fetch, ordered precisely below
            return rows.OrderBy(r => r.Event.StartDate).ThenBy(r => r.Event.StartTime ?? TimeOnly.MinValue).ThenBy(r => r.Event.EventId).Take(take).ToList();
        }
    }
}
