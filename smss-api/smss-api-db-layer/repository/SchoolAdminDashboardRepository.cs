using smss_api_db_layer.helper;
using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class SchoolAdminDashboardRepository : ISchoolAdminDashboardRepository
    {
        private readonly dbContext _db;
        public SchoolAdminDashboardRepository(dbContext db) => _db = db;

        public Task<int?> ActiveStatusIdAsync(CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == "Active" && s.Stype == "general status").Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public async Task<DashboardYearRow?> CurrentYearAsync(string schoolId, int activeStatusId, CancellationToken ct)
        {
            var y = await _db.AcademicYears.AsNoTracking().Where(x => x.SchoolId == schoolId && x.IsCurrent && x.StatusId == activeStatusId)
                .OrderByDescending(x => x.StartDate).Select(x => new { x.AcademicYearId, x.YearName, x.StartDate, x.EndDate }).FirstOrDefaultAsync(ct);
            return y == null ? null : new DashboardYearRow(y.AcademicYearId, y.YearName, y.StartDate, y.EndDate, true, false);   // the current year can never be closed
        }

        public async Task<DashboardYearRow?> YearAsync(string schoolId, long academicYearId, CancellationToken ct)
        {
            var y = await _db.AcademicYears.AsNoTracking().Where(x => x.SchoolId == schoolId && x.AcademicYearId == academicYearId)
                .Select(x => new { x.AcademicYearId, x.YearName, x.StartDate, x.EndDate, x.IsCurrent, Closed = x.ClosedAt != null }).FirstOrDefaultAsync(ct);
            return y == null ? null : new DashboardYearRow(y.AcademicYearId, y.YearName, y.StartDate, y.EndDate, y.IsCurrent, y.Closed);
        }

        public Task<int> EnrolledStudentsAsync(string schoolId, int activeStatusId, long academicYearId, CancellationToken ct) =>
            (from e in _db.StudentEnrollments.AsNoTracking().LiveForYear(_db.AcademicYears)
             join s in _db.Students.AsNoTracking() on new { e.StudentId, e.SchoolId } equals new { s.StudentId, s.SchoolId }
             where e.SchoolId == schoolId && e.AcademicYearId == academicYearId && e.StatusId == activeStatusId && s.StatusId == activeStatusId
             select e.StudentId).Distinct().CountAsync(ct);

        public Task<bool> HasFeeItemsAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            _db.StudentFeeItems.AsNoTracking().AnyAsync(i => i.SchoolId == schoolId && i.AcademicYearId == academicYearId, ct);

        public async Task<DashboardFeeMonthRow> FeeMonthAsync(string schoolId, int activeStatusId, long academicYearId, DateOnly monthStart, DateOnly monthEnd, CancellationToken ct)
        {
            // dues of the YEAR's fee items for ACTIVE students of this school, due inside the month; the student join also keeps another school's rows out
            var dues = from d in _db.StudentFeeDues.AsNoTracking()
                       join i in _db.StudentFeeItems.AsNoTracking() on d.StudentFeeItemId equals i.StudentFeeItemId
                       join s in _db.Students.AsNoTracking() on new { d.StudentId, d.SchoolId } equals new { s.StudentId, s.SchoolId }
                       where d.SchoolId == schoolId && i.SchoolId == schoolId && i.AcademicYearId == academicYearId && s.StatusId == activeStatusId && d.DueDate >= monthStart && d.DueDate <= monthEnd
                       select d;
            var withDues = await dues.Select(d => d.StudentId).Distinct().CountAsync(ct);
            var notPaid = await dues.Where(d => d.AmountPaid < d.AmountDue).Select(d => d.StudentId).Distinct().CountAsync(ct);
            var outstanding = await dues.Where(d => d.AmountPaid < d.AmountDue).SumAsync(d => (decimal?)(d.AmountDue - d.AmountPaid), ct) ?? 0m;
            return new DashboardFeeMonthRow(withDues, notPaid, outstanding);
        }

        public async Task<decimal> CollectedAsync(string schoolId, long academicYearId, DateTime? fromUtc, DateTime? toUtcExclusive, CancellationToken ct) =>
            await (from a in _db.FeePaymentAllocations.AsNoTracking()
                   join p in _db.FeePayments.AsNoTracking() on a.PaymentId equals p.PaymentId
                   join d in _db.StudentFeeDues.AsNoTracking() on a.FeeDueId equals d.FeeDueId
                   join i in _db.StudentFeeItems.AsNoTracking() on d.StudentFeeItemId equals i.StudentFeeItemId
                   where p.SchoolId == schoolId && p.Status == "Active" && i.SchoolId == schoolId && i.AcademicYearId == academicYearId
                         && (fromUtc == null || p.PaymentDate >= fromUtc) && (toUtcExclusive == null || p.PaymentDate < toUtcExclusive)
                   select (decimal?)(a.AllocatedAmount - a.ConcessionAmount)).SumAsync(ct) ?? 0m;

        public Task<int> NewAdmissionsAsync(string schoolId, int activeStatusId, DateOnly from, DateOnly to, CancellationToken ct)
        {
            var f = from.ToDateTime(TimeOnly.MinValue);
            var t = to.ToDateTime(TimeOnly.MinValue);
            return (from s in _db.Students.AsNoTracking()
                    join at in _db.LutAdmissionTypes.AsNoTracking() on s.AdmissionTypeId equals at.AdmissionTypeId
                    where s.SchoolId == schoolId && s.StatusId == activeStatusId && at.TypeCode == "NEW_ADMISSION" && s.AdmissionDate >= f && s.AdmissionDate <= t
                    select s.StudentId).CountAsync(ct);
        }

        public async Task<DashboardEmployeeRow> EmployeesAsync(string schoolId, int activeStatusId, CancellationToken ct)
        {
            // One grouped query. Grouping rule: Teaching staff are teachers; of the rest, the designation's name decides (accountant / admission / driver); everything else is "other".
            var groups = await (from e in _db.Employees.AsNoTracking()
                                join d in _db.EmployeeDesignations.AsNoTracking() on e.DesignationId equals d.DesignationId
                                where e.SchoolId == schoolId && e.StatusId == activeStatusId
                                let cat = e.EmployeeType == "Teaching" ? 1
                                        : d.DesignationName.ToLower().Contains("accountant") ? 2
                                        : d.DesignationName.ToLower().Contains("admission") ? 3
                                        : d.DesignationName.ToLower().Contains("driver") ? 4 : 0
                                group e by cat into g
                                select new { Cat = g.Key, N = g.Count() }).ToListAsync(ct);
            int Of(int c) => groups.Where(g => g.Cat == c).Sum(g => g.N);
            return new DashboardEmployeeRow(groups.Sum(g => g.N), Of(1), Of(2), Of(3), Of(4));
        }

        public Task<bool> WhatsAppMissingAsync(string schoolId, CancellationToken ct) =>
            _db.Schools.AsNoTracking().AnyAsync(s => s.SchoolId == schoolId && (s.WhatsAppNumber == null || s.WhatsAppNumber == ""), ct);

        public Task<int> ActiveVehiclesAsync(string schoolId, int activeStatusId, CancellationToken ct) =>
            _db.Vehicles.AsNoTracking().CountAsync(v => v.SchoolId == schoolId && v.StatusId == activeStatusId, ct);
    }
}
 