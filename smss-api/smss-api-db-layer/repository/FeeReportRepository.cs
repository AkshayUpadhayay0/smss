using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class FeeReportRepository : IFeeReportRepository
    {
        private readonly dbContext _db;
        public FeeReportRepository(dbContext db) => _db = db;

        public Task<ReportYearRow?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.AcademicYearId == academicYearId)
                .Select(y => new ReportYearRow(y.AcademicYearId, y.YearName, y.StartDate, y.EndDate)).FirstOrDefaultAsync(ct);

        public Task<string?> GetClassNameAsync(string schoolId, long classId, CancellationToken ct) =>
            _db.Classes.AsNoTracking().Where(c => c.SchoolId == schoolId && c.ClassId == classId).Select(c => c.ClassName).FirstOrDefaultAsync(ct);

        public Task<bool> SectionBelongsToClassAsync(string schoolId, long classId, long sectionId, CancellationToken ct) =>
            _db.Sections.AnyAsync(s => s.SchoolId == schoolId && s.ClassId == classId && s.SectionId == sectionId, ct);

        // The students in scope: enrolled that year (optionally in one class and/or section). An EXISTS, so a student never counts twice.
        private IQueryable<TbStudentFeeItem> ItemsInScope(string schoolId, long academicYearId, long? classId, long? sectionId) =>
            _db.StudentFeeItems.AsNoTracking().Where(i => i.SchoolId == schoolId && i.AcademicYearId == academicYearId
                && (classId == null && sectionId == null
                    || _db.StudentEnrollments.Any(e => e.SchoolId == schoolId && e.StudentId == i.StudentId && e.AcademicYearId == academicYearId
                        && (sectionId == null || e.ClassSectionId == sectionId)
                        && (classId == null || _db.Sections.Any(s => s.SchoolId == schoolId && s.SectionId == e.ClassSectionId && s.ClassId == classId)))));

        public async Task<DashboardTotals> GetDashboardTotalsAsync(string schoolId, long academicYearId, long? classId, long? sectionId, DateOnly today, DateOnly upcomingUntil, CancellationToken ct)
        {
            // Same rules as FeeSchedule.Status: still owed (paid < due) and due before today = Overdue; still owed and due today or later = not yet overdue.
            var row = await (
                from d in _db.StudentFeeDues.AsNoTracking()
                join i in ItemsInScope(schoolId, academicYearId, classId, sectionId) on d.StudentFeeItemId equals i.StudentFeeItemId
                where d.SchoolId == schoolId
                group d by 1 into g
                select new
                {
                    Due = g.Sum(x => x.AmountDue),
                    Paid = g.Sum(x => x.AmountPaid),
                    OverdueAmount = g.Sum(x => x.AmountPaid < x.AmountDue && x.DueDate < today ? x.AmountDue - x.AmountPaid : 0m),
                    OverdueCount = g.Count(x => x.AmountPaid < x.AmountDue && x.DueDate < today),
                    UpcomingAmount = g.Sum(x => x.AmountPaid < x.AmountDue && x.DueDate >= today && x.DueDate <= upcomingUntil ? x.AmountDue - x.AmountPaid : 0m),
                    UpcomingCount = g.Count(x => x.AmountPaid < x.AmountDue && x.DueDate >= today && x.DueDate <= upcomingUntil)
                }).FirstOrDefaultAsync(ct);
            return row == null ? new DashboardTotals(0, 0, 0, 0, 0, 0)
                : new DashboardTotals(row.Due, row.Paid, row.OverdueAmount, row.OverdueCount, row.UpcomingAmount, row.UpcomingCount);
        }

        public async Task<List<CollectionMonth>> GetCollectionByMonthAsync(string schoolId, long academicYearId, long? classId, long? sectionId, CancellationToken ct)
        {
            var rows = await (
                from a in _db.FeePaymentAllocations.AsNoTracking()
                join p in _db.FeePayments.AsNoTracking() on a.PaymentId equals p.PaymentId
                join d in _db.StudentFeeDues.AsNoTracking() on a.FeeDueId equals d.FeeDueId
                join i in ItemsInScope(schoolId, academicYearId, classId, sectionId) on d.StudentFeeItemId equals i.StudentFeeItemId
                where p.SchoolId == schoolId && p.Status == "Active"
                group a by new { p.PaymentDate.AddMinutes(330).Year, p.PaymentDate.AddMinutes(330).Month } into g
                select new { g.Key.Year, g.Key.Month, Amount = g.Sum(x => x.AllocatedAmount) }).ToListAsync(ct);
            return rows.Select(r => new CollectionMonth(r.Year, r.Month, r.Amount)).ToList();
        }

        public async Task<List<ClassStudentRow>> GetClassStudentsAsync(string schoolId, long academicYearId, long classId, long? sectionId, CancellationToken ct)
        {
            var rows = await (
                from e in _db.StudentEnrollments.AsNoTracking()
                join s in _db.Students.AsNoTracking() on e.StudentId equals s.StudentId
                join sec in _db.Sections.AsNoTracking() on e.ClassSectionId equals sec.SectionId
                where e.SchoolId == schoolId && s.SchoolId == schoolId && sec.SchoolId == schoolId
                      && e.AcademicYearId == academicYearId && sec.ClassId == classId && (sectionId == null || sec.SectionId == sectionId)
                select new { s.StudentId, s.FirstName, s.MiddleName, s.LastName, e.RollNumber, sec.SectionName }).ToListAsync(ct);
            return rows.Select(r => new ClassStudentRow(r.StudentId, r.FirstName, r.MiddleName, r.LastName, r.RollNumber, r.SectionName)).ToList();
        }
    }
}
