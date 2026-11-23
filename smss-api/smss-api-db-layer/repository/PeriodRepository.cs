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

        // The classic Periods screen and API work on the DEFAULT schedule's numbered periods (breaks and other schedules are managed under Academic Setup).
        private IQueryable<long> DefaultScheduleId(string schoolId) => _db.PeriodSchedules.AsNoTracking().Where(d => d.SchoolId == schoolId && d.IsDefault).Select(d => d.ScheduleId);

        public async Task<List<PeriodListRow>> ListAsync(string schoolId, bool includeInactive, int activeStatusId, CancellationToken ct)
        {
            var rows = await Rows(_db.Periods.AsNoTracking().Where(p => p.SchoolId == schoolId && p.SlotType == SlotTypes.Period && DefaultScheduleId(schoolId).Contains(p.ScheduleId)
                                                                   && (includeInactive || p.StatusId == activeStatusId)), activeStatusId).ToListAsync(ct);
            return rows.OrderBy(r => r.Period.PeriodNumber).ToList();
        }

        public Task<TbPeriod?> GetForUpdateAsync(string schoolId, long periodId, CancellationToken ct) =>
            _db.Periods.Where(p => p.SchoolId == schoolId && p.PeriodId == periodId).FirstOrDefaultAsync(ct);

        public Task<PeriodListRow?> GetRowAsync(string schoolId, long periodId, int activeStatusId, CancellationToken ct) =>
            Rows(_db.Periods.AsNoTracking().Where(p => p.SchoolId == schoolId && p.PeriodId == periodId), activeStatusId).FirstOrDefaultAsync(ct);

        public Task<bool> NumberTakenAsync(string schoolId, short number, long? exceptPeriodId, CancellationToken ct) =>
            _db.Periods.AnyAsync(p => p.SchoolId == schoolId && p.PeriodNumber == number && p.PeriodId != exceptPeriodId && DefaultScheduleId(schoolId).Contains(p.ScheduleId), ct);

        public Task<short?> FindOverlapAsync(string schoolId, TimeOnly start, TimeOnly end, long? exceptPeriodId, int activeStatusId, CancellationToken ct) =>
            _db.Periods.AsNoTracking().Where(p => p.SchoolId == schoolId && p.StatusId == activeStatusId && p.PeriodId != exceptPeriodId && p.StartTime < end && p.EndTime > start
                                                  && DefaultScheduleId(schoolId).Contains(p.ScheduleId))
                .OrderBy(p => p.SlotOrder).Select(p => p.PeriodNumber).FirstOrDefaultAsync(ct);

        public Task<int> ActiveEntryCountAsync(string schoolId, long periodId, int activeStatusId, CancellationToken ct) =>
            _db.TimetableEntries.CountAsync(e => e.SchoolId == schoolId && e.PeriodId == periodId && e.StatusId == activeStatusId, ct);

        public async Task AddAsync(TbPeriod period, CancellationToken ct) => await _db.Periods.AddAsync(period, ct);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

        // ---------------- S4
        public async Task<TbPeriodSchedule> EnsureDefaultScheduleAsync(string schoolId, int activeStatusId, CancellationToken ct)
        {
            var existing = await _db.PeriodSchedules.FirstOrDefaultAsync(d => d.SchoolId == schoolId && d.IsDefault, ct);
            if (existing != null) return existing;
            var now = DateTime.UtcNow;
            var created = new TbPeriodSchedule { SchoolId = schoolId, ScheduleName = "Regular", IsDefault = true, StatusId = activeStatusId, CreatedAt = now, UpdatedAt = now };
            // a school may already have a schedule called "Regular" that is not the default: then the default simply gets another name
            if (await _db.PeriodSchedules.AnyAsync(d => d.SchoolId == schoolId && d.ScheduleName == "Regular", ct)) created.ScheduleName = "Default";
            await _db.PeriodSchedules.AddAsync(created, ct);
            await _db.SaveChangesAsync(ct);
            return created;
        }

        public Task<TbPeriodSchedule?> GetDefaultScheduleAsync(string schoolId, CancellationToken ct) =>
            _db.PeriodSchedules.AsNoTracking().Where(d => d.SchoolId == schoolId && d.IsDefault).FirstOrDefaultAsync(ct);

        public Task<List<TbPeriodSchedule>> ListSchedulesAsync(string schoolId, CancellationToken ct) =>
            _db.PeriodSchedules.AsNoTracking().Where(d => d.SchoolId == schoolId).OrderByDescending(d => d.IsDefault).ThenBy(d => d.ScheduleName).ToListAsync(ct);

        public Task<TbPeriodSchedule?> GetScheduleForUpdateAsync(string schoolId, long scheduleId, CancellationToken ct) =>
            _db.PeriodSchedules.Where(d => d.SchoolId == schoolId && d.ScheduleId == scheduleId).FirstOrDefaultAsync(ct);

        public Task<bool> ScheduleNameTakenAsync(string schoolId, string name, long? exceptScheduleId, CancellationToken ct) =>
            _db.PeriodSchedules.AnyAsync(d => d.SchoolId == schoolId && d.ScheduleName.ToLower() == name.ToLower() && d.ScheduleId != exceptScheduleId, ct);

        public async Task<List<ScheduleUsageRow>> ScheduleUsageAsync(string schoolId, CancellationToken ct)
        {
            var slots = await _db.Periods.AsNoTracking().Where(p => p.SchoolId == schoolId).GroupBy(p => p.ScheduleId).Select(g => new { Id = g.Key, N = g.Count() }).ToListAsync(ct);
            var assigned = await _db.AcademicYearSchedules.AsNoTracking().Where(a => a.SchoolId == schoolId).GroupBy(a => a.ScheduleId).Select(g => new { Id = g.Key, N = g.Count() }).ToListAsync(ct);
            var entries = await (from e in _db.TimetableEntries.AsNoTracking()
                                 join p in _db.Periods.AsNoTracking() on e.PeriodId equals p.PeriodId
                                 where e.SchoolId == schoolId
                                 group e by p.ScheduleId into g
                                 select new { Id = g.Key, N = g.Count() }).ToListAsync(ct);
            var ids = (await _db.PeriodSchedules.AsNoTracking().Where(d => d.SchoolId == schoolId).Select(d => d.ScheduleId).ToListAsync(ct));
            return ids.Select(id => new ScheduleUsageRow(id, slots.FirstOrDefault(x => x.Id == id)?.N ?? 0, assigned.FirstOrDefault(x => x.Id == id)?.N ?? 0, entries.FirstOrDefault(x => x.Id == id)?.N ?? 0)).ToList();
        }

        public Task<List<TbPeriod>> AllSlotsAsync(string schoolId, CancellationToken ct) =>
            _db.Periods.AsNoTracking().Where(p => p.SchoolId == schoolId).OrderBy(p => p.ScheduleId).ThenBy(p => p.SlotOrder).ToListAsync(ct);

        public Task<List<TbPeriod>> ScheduleSlotsForUpdateAsync(string schoolId, long scheduleId, CancellationToken ct) =>
            _db.Periods.Where(p => p.SchoolId == schoolId && p.ScheduleId == scheduleId).OrderBy(p => p.SlotOrder).ToListAsync(ct);

        public Task<int> EntryCountAnyStatusAsync(string schoolId, long periodId, CancellationToken ct) =>
            _db.TimetableEntries.CountAsync(e => e.SchoolId == schoolId && e.PeriodId == periodId, ct);

        public Task<Dictionary<long, int>> ActiveEntryCountsAsync(string schoolId, int activeStatusId, CancellationToken ct) =>
            _db.TimetableEntries.AsNoTracking().Where(e => e.SchoolId == schoolId && e.StatusId == activeStatusId).GroupBy(e => e.PeriodId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);

        public Task<List<long>> EntryYearIdsAsync(string schoolId, CancellationToken ct) =>
            _db.TimetableEntries.AsNoTracking().Where(e => e.SchoolId == schoolId).Select(e => e.AcademicYearId).Distinct().ToListAsync(ct);

        public async Task ResequenceAsync(string schoolId, long scheduleId, CancellationToken ct)
        {
            // two statements: a single UPDATE that swaps two orders would trip the unique (schedule_id, slot_order) row by row
            await _db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.tb_periods SET slot_order = slot_order + 1000 WHERE school_id = {schoolId} AND schedule_id = {scheduleId}", ct);
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
WITH ranked AS (SELECT period_id, row_number() OVER (ORDER BY start_time, end_time, period_id) AS rn FROM public.tb_periods WHERE school_id = {schoolId} AND schedule_id = {scheduleId})
UPDATE public.tb_periods p SET slot_order = r.rn::smallint FROM ranked r WHERE p.period_id = r.period_id", ct);
            _db.ChangeTracker.Clear();   // the slot_order values changed behind EF's back
        }

        public void RemoveSlots(IEnumerable<TbPeriod> slots) => _db.Periods.RemoveRange(slots);
        public void RemoveSchedule(TbPeriodSchedule schedule) => _db.PeriodSchedules.Remove(schedule);
        public async Task AddScheduleAsync(TbPeriodSchedule schedule, CancellationToken ct) => await _db.PeriodSchedules.AddAsync(schedule, ct);

        public Task<TbAcademicYears?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.AcademicYearId == academicYearId).FirstOrDefaultAsync(ct);

        public async Task<List<(long ClassId, string ClassName)>> OfferedClassesAsync(string schoolId, long academicYearId, int activeStatusId, CancellationToken ct)
        {
            var rows = await (from o in _db.AcademicYearClasses.AsNoTracking()
                              join c in _db.Classes.AsNoTracking() on o.ClassId equals c.ClassId
                              where o.SchoolId == schoolId && c.SchoolId == schoolId && o.AcademicYearId == academicYearId && o.StatusId == activeStatusId
                              orderby c.SequenceOrder, c.ClassName
                              select new { c.ClassId, c.ClassName }).ToListAsync(ct);
            return rows.Select(r => (r.ClassId, r.ClassName)).ToList();
        }

        public Task<List<TbAcademicYearSchedule>> AssignmentsForUpdateAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            _db.AcademicYearSchedules.Where(a => a.SchoolId == schoolId && a.AcademicYearId == academicYearId).ToListAsync(ct);

        public Task<List<EntryScheduleUse>> EntryScheduleUseAsync(string schoolId, long academicYearId, int activeStatusId, CancellationToken ct) =>
            (from e in _db.TimetableEntries.AsNoTracking()
             join p in _db.Periods.AsNoTracking() on e.PeriodId equals p.PeriodId
             where e.SchoolId == schoolId && e.AcademicYearId == academicYearId && e.StatusId == activeStatusId && e.TtIsActive   // the live (ACTIVE) timetables only: a draft never blocks
             group e by new { e.ClassId, e.DayOfWeek, p.ScheduleId } into g
             select new EntryScheduleUse(g.Key.ClassId, g.Key.DayOfWeek, g.Key.ScheduleId, g.Count())).ToListAsync(ct);

        public async Task AddAssignmentsAsync(IEnumerable<TbAcademicYearSchedule> rows, CancellationToken ct) => await _db.AcademicYearSchedules.AddRangeAsync(rows, ct);
        public void RemoveAssignments(IEnumerable<TbAcademicYearSchedule> rows) => _db.AcademicYearSchedules.RemoveRange(rows);

        public async Task LockSchoolAsync(string schoolId, CancellationToken ct)
        {
            var key = "acad-setup:" + schoolId;
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }

        public Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);
    }
}
