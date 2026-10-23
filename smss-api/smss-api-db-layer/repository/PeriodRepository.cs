using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class PeriodRepository : IPeriodRepository
    {
        private readonly dbContext _db;
        public PeriodRepository(dbContext db) => _db = db;

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == statusName && s.Stype == statusType).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        private IQueryable<PeriodListRow> Rows(IQueryable<TbPeriod> source, int activeStatusId) =>
            from p in source
            join st in _db.LutStatus.AsNoTracking() on p.StatusId equals st.Sid
            select new PeriodListRow(p, st.Sname, _db.TimetableEntries.Count(e => e.PeriodId == p.PeriodId && e.StatusId == activeStatusId));

        public async Task<List<PeriodListRow>> ListAsync(string schoolId, bool includeInactive, int activeStatusId, CancellationToken ct)
        {
            var rows = await Rows(_db.Periods.AsNoTracking().Where(p => p.SchoolId == schoolId && (includeInactive || p.StatusId == activeStatusId)), activeStatusId).ToListAsync(ct);
            return rows.OrderBy(r => r.Period.PeriodNumber).ToList();
        }

        public Task<TbPeriod?> GetForUpdateAsync(string schoolId, long periodId, CancellationToken ct) =>
            _db.Periods.Where(p => p.SchoolId == schoolId && p.PeriodId == periodId).FirstOrDefaultAsync(ct);

        public Task<PeriodListRow?> GetRowAsync(string schoolId, long periodId, int activeStatusId, CancellationToken ct) =>
            Rows(_db.Periods.AsNoTracking().Where(p => p.SchoolId == schoolId && p.PeriodId == periodId), activeStatusId).FirstOrDefaultAsync(ct);

        public Task<bool> NumberTakenAsync(string schoolId, short number, long? exceptPeriodId, CancellationToken ct) =>
            _db.Periods.AnyAsync(p => p.SchoolId == schoolId && p.PeriodNumber == number && p.PeriodId != exceptPeriodId, ct);

        public Task<short?> FindOverlapAsync(string schoolId, TimeOnly start, TimeOnly end, long? exceptPeriodId, int activeStatusId, CancellationToken ct) =>
            _db.Periods.AsNoTracking().Where(p => p.SchoolId == schoolId && p.StatusId == activeStatusId && p.PeriodId != exceptPeriodId && p.StartTime < end && p.EndTime > start)
                .OrderBy(p => p.PeriodNumber).Select(p => (short?)p.PeriodNumber).FirstOrDefaultAsync(ct);

        public Task<int> ActiveEntryCountAsync(string schoolId, long periodId, int activeStatusId, CancellationToken ct) =>
            _db.TimetableEntries.CountAsync(e => e.SchoolId == schoolId && e.PeriodId == periodId && e.StatusId == activeStatusId, ct);

        public async Task AddAsync(TbPeriod period, CancellationToken ct) => await _db.Periods.AddAsync(period, ct);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
    }
}
