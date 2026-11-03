using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class SchoolDashboardRepository : ISchoolDashboardRepository
    {
        private readonly dbContext _db;
        public SchoolDashboardRepository(dbContext db) => _db = db;

        public async Task<List<SchoolAttendanceDayRow>> GetAttendanceByDayAsync(string schoolId, DateOnly fromDate, DateOnly toDate, string presentCode, CancellationToken ct)
        {
            // one grouped query for the whole range; the status code is resolved by name, never by a hardcoded id
            var rows = await (
                from a in _db.StudentAttendance.AsNoTracking()
                join s in _db.LutAttendanceStatuses.AsNoTracking() on a.AttendanceStatusId equals s.AttendanceStatusId
                where a.SchoolId == schoolId && a.AttendanceDate >= fromDate && a.AttendanceDate <= toDate
                group s.StatusCode by a.AttendanceDate into g
                select new { Date = g.Key, Marked = g.Count(), Present = g.Count(code => code == presentCode) }).ToListAsync(ct);
            return rows.OrderBy(r => r.Date).Select(r => new SchoolAttendanceDayRow(r.Date, r.Marked, r.Present)).ToList();
        }

        public async Task<List<SchoolYearSpanRow>> GetYearsOverlappingAsync(string schoolId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
        {
            var rows = await _db.AcademicYears.AsNoTracking()
                .Where(y => y.SchoolId == schoolId && y.StartDate <= toDate && y.EndDate >= fromDate)
                .OrderBy(y => y.StartDate).Select(y => new { y.AcademicYearId, y.StartDate, y.EndDate }).ToListAsync(ct);
            return rows.Select(y => new SchoolYearSpanRow(y.AcademicYearId, y.StartDate, y.EndDate)).ToList();
        }
    }
}
