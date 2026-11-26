using smss_api_db_layer.helper;
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

        private const string ActiveName = "Active", GeneralType = "general status";

        public async Task<DashboardYearRow?> CurrentYearAsync(string schoolId, CancellationToken ct)
        {
            var active = _db.LutStatus.Where(s => s.Sname == ActiveName && s.Stype == GeneralType).Select(s => (int?)s.Sid).FirstOrDefault();
            var y = await _db.AcademicYears.AsNoTracking().Where(x => x.SchoolId == schoolId && x.IsCurrent && x.StatusId == active)
                .OrderByDescending(x => x.StartDate).Select(x => new { x.AcademicYearId, x.YearName, x.StartDate, x.EndDate, Closed = x.ClosedAt != null }).FirstOrDefaultAsync(ct);
            return y == null ? null : new DashboardYearRow(y.AcademicYearId, y.YearName, y.StartDate, y.EndDate, true, y.Closed);
        }

        public async Task<DashboardYearRow?> YearAsync(string schoolId, long academicYearId, CancellationToken ct)
        {
            var y = await _db.AcademicYears.AsNoTracking().Where(x => x.SchoolId == schoolId && x.AcademicYearId == academicYearId)
                .Select(x => new { x.AcademicYearId, x.YearName, x.StartDate, x.EndDate, x.IsCurrent, Closed = x.ClosedAt != null }).FirstOrDefaultAsync(ct);
            return y == null ? null : new DashboardYearRow(y.AcademicYearId, y.YearName, y.StartDate, y.EndDate, y.IsCurrent, y.Closed);
        }

        public async Task<DashboardYearRow?> YearContainingAsync(string schoolId, DateOnly date, CancellationToken ct)
        {
            var y = await _db.AcademicYears.AsNoTracking().Where(x => x.SchoolId == schoolId && x.StartDate <= date && x.EndDate >= date)
                .OrderByDescending(x => x.IsCurrent).ThenByDescending(x => x.StartDate)
                .Select(x => new { x.AcademicYearId, x.YearName, x.StartDate, x.EndDate, x.IsCurrent, Closed = x.ClosedAt != null }).FirstOrDefaultAsync(ct);
            return y == null ? null : new DashboardYearRow(y.AcademicYearId, y.YearName, y.StartDate, y.EndDate, y.IsCurrent, y.Closed);
        }

        public Task<int> EnrolledStudentsAsync(string schoolId, long academicYearId, CancellationToken ct)
        {
            var active = _db.LutStatus.Where(s => s.Sname == ActiveName && s.Stype == GeneralType).Select(s => (int?)s.Sid).FirstOrDefault();
            return (from e in _db.StudentEnrollments.AsNoTracking().LiveForYear(_db.AcademicYears)
                    join s in _db.Students.AsNoTracking() on new { e.StudentId, e.SchoolId } equals new { s.StudentId, s.SchoolId }
                    where e.SchoolId == schoolId && e.AcademicYearId == academicYearId && e.StatusId == active && s.StatusId == active
                    select e.StudentId).Distinct().CountAsync(ct);
        }

        public Task<bool> HasFeeItemsAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            _db.StudentFeeItems.AsNoTracking().AnyAsync(i => i.SchoolId == schoolId && i.AcademicYearId == academicYearId, ct);

        public async Task<List<SchoolYearSpanRow>> GetYearsOverlappingAsync(string schoolId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
        {
            var rows = await _db.AcademicYears.AsNoTracking()
                .Where(y => y.SchoolId == schoolId && y.StartDate <= toDate && y.EndDate >= fromDate)
                .OrderBy(y => y.StartDate).Select(y => new { y.AcademicYearId, y.StartDate, y.EndDate }).ToListAsync(ct);
            return rows.Select(y => new SchoolYearSpanRow(y.AcademicYearId, y.StartDate, y.EndDate)).ToList();
        }
    }
}
