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
                // CASH collected: what went against the installments minus the part that was discounted
                select new { g.Key.Year, g.Key.Month, Amount = g.Sum(x => x.AllocatedAmount - x.ConcessionAmount) }).ToListAsync(ct);
            return rows.Select(r => new CollectionMonth(r.Year, r.Month, r.Amount)).ToList();
        }

        public async Task<List<ConcessionGivenRow>> GetConcessionGivenAsync(string schoolId, long academicYearId, long? classId, long? sectionId, CancellationToken ct)
        {
            var rows = await (
                from a in _db.FeePaymentAllocations.AsNoTracking()
                join p in _db.FeePayments.AsNoTracking() on a.PaymentId equals p.PaymentId
                join d in _db.StudentFeeDues.AsNoTracking() on a.FeeDueId equals d.FeeDueId
                join i in ItemsInScope(schoolId, academicYearId, classId, sectionId) on d.StudentFeeItemId equals i.StudentFeeItemId
                where p.SchoolId == schoolId && p.Status == "Active" && p.ConcessionId != null && a.ConcessionAmount > 0
                group new { a, p } by p.ConcessionId into g
                select new { ConcessionId = g.Key!.Value, Amount = g.Sum(x => x.a.ConcessionAmount), Count = g.Select(x => x.p.PaymentId).Distinct().Count() }).ToListAsync(ct);
            if (rows.Count == 0) return new List<ConcessionGivenRow>();
            var ids = rows.Select(r => r.ConcessionId).ToList();
            var masters = await _db.FeeConcessions.AsNoTracking().Where(c => c.SchoolId == schoolId && ids.Contains(c.ConcessionId)).ToDictionaryAsync(c => c.ConcessionId, ct);
            return rows.Where(r => masters.ContainsKey(r.ConcessionId))
                .Select(r => new ConcessionGivenRow(r.ConcessionId, masters[r.ConcessionId].ConcessionName, masters[r.ConcessionId].ConcessionCode, r.Amount, r.Count))
                .OrderBy(r => r.ConcessionName).ToList();
        }

        public async Task<Dictionary<string, decimal>> GetConcessionGivenByStudentAsync(string schoolId, long academicYearId, IReadOnlyCollection<string> studentIds, CancellationToken ct)
        {
            if (studentIds.Count == 0) return new Dictionary<string, decimal>();
            var ids = studentIds.ToArray();
            return await (
                from a in _db.FeePaymentAllocations.AsNoTracking()
                join p in _db.FeePayments.AsNoTracking() on a.PaymentId equals p.PaymentId
                join d in _db.StudentFeeDues.AsNoTracking() on a.FeeDueId equals d.FeeDueId
                join i in _db.StudentFeeItems.AsNoTracking() on d.StudentFeeItemId equals i.StudentFeeItemId
                where p.SchoolId == schoolId && p.Status == "Active" && a.ConcessionAmount > 0 && i.SchoolId == schoolId && i.AcademicYearId == academicYearId && ids.Contains(i.StudentId)
                group a by i.StudentId into g
                select new { g.Key, Amount = g.Sum(x => x.ConcessionAmount) }).ToDictionaryAsync(x => x.Key, x => x.Amount, ct);
        }

        public async Task<ConcessionLogPage> GetConcessionLogAsync(string schoolId, DateTime? fromUtc, DateTime? toExclusiveUtc, int page, int pageSize, CancellationToken ct)
        {
            var q = _db.FeePayments.AsNoTracking().Where(p => p.SchoolId == schoolId && p.ConcessionId != null
                && (fromUtc == null || p.PaymentDate >= fromUtc) && (toExclusiveUtc == null || p.PaymentDate < toExclusiveUtc));
            var total = await q.CountAsync(ct);
            var activeTotal = await q.Where(p => p.Status == "Active").SumAsync(p => (decimal?)p.ConcessionAmount, ct) ?? 0m;
            var rows = await q.OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.PaymentId).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

            var studentIds = rows.Select(r => r.StudentId).Distinct().ToList();
            var students = await _db.Students.AsNoTracking().Where(s => s.SchoolId == schoolId && studentIds.Contains(s.StudentId))
                .Select(s => new { s.StudentId, s.FirstName, s.MiddleName, s.LastName }).ToListAsync(ct);
            var nameOf = students.ToDictionary(s => s.StudentId, s => string.Join(' ', new[] { s.FirstName, s.MiddleName, s.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))));
            var userIds = rows.Where(r => r.ConcessionAppliedBy != null).Select(r => r.ConcessionAppliedBy!.Value).Distinct().ToList();
            var users = userIds.Count == 0 ? new Dictionary<long, string>()
                : await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.DisplayName ?? u.OrgUserId ?? string.Empty, ct);

            return new ConcessionLogPage(rows.Select(p => new ConcessionLogRow(
                p.PaymentId, p.ReceiptNumber, p.PaymentDate, p.StudentId, nameOf.GetValueOrDefault(p.StudentId, p.StudentId),
                p.ConcessionId!.Value, p.ConcessionNameSnapshot ?? string.Empty, p.ConcessionCodeSnapshot ?? string.Empty, p.ConcessionTypeSnapshot ?? string.Empty,
                p.ConcessionValueSnapshot ?? 0m, p.ConcessionAmount, p.ConcessionReason,
                p.ConcessionAppliedBy is long u && users.TryGetValue(u, out var n) ? n : null, p.Status)).ToList(), total, activeTotal);
        }

        public async Task<List<TaxCollectedRow>> GetTaxCollectedAsync(string schoolId, long academicYearId, long? classId, long? sectionId, CancellationToken ct)
        {
            var inScope = ItemsInScope(schoolId, academicYearId, classId, sectionId);
            var rows = await (
                from p in _db.FeePayments.AsNoTracking()
                where p.SchoolId == schoolId && p.Status == "Active" && p.TaxTypeId != null
                      && (from a in _db.FeePaymentAllocations
                          join d in _db.StudentFeeDues on a.FeeDueId equals d.FeeDueId
                          join i in inScope on d.StudentFeeItemId equals i.StudentFeeItemId
                          where a.PaymentId == p.PaymentId select a.AllocationId).Any()
                group p by p.TaxTypeId into g
                select new { TaxTypeId = g.Key!.Value, Amount = g.Sum(x => x.TaxAmount), Count = g.Count() }).ToListAsync(ct);
            if (rows.Count == 0) return new List<TaxCollectedRow>();
            var ids = rows.Select(r => r.TaxTypeId).ToList();
            var types = await _db.TaxTypes.AsNoTracking().Where(t => t.SchoolId == schoolId && ids.Contains(t.TaxTypeId)).ToDictionaryAsync(t => t.TaxTypeId, ct);
            return rows.Where(r => types.ContainsKey(r.TaxTypeId))
                .Select(r => new TaxCollectedRow(r.TaxTypeId, types[r.TaxTypeId].TaxName, types[r.TaxTypeId].TaxCode, r.Amount, r.Count))
                .OrderBy(r => r.TaxName).ToList();
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
