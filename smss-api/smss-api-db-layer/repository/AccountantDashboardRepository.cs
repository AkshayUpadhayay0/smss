using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class AccountantDashboardRepository : IAccountantDashboardRepository
    {
        private const string Active = "Active";
        private readonly dbContext _db;
        public AccountantDashboardRepository(dbContext db) => _db = db;

        public async Task<decimal> GetCollectedBetweenAsync(string schoolId, DateTime fromUtc, DateTime toExclusiveUtc, CancellationToken ct) =>
            await _db.FeePayments.AsNoTracking()
                .Where(p => p.SchoolId == schoolId && p.Status == Active && p.PaymentDate >= fromUtc && p.PaymentDate < toExclusiveUtc)
                .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        public async Task<AccountantYearRow?> GetYearContainingAsync(string schoolId, DateOnly date, CancellationToken ct)
        {
            var y = await _db.AcademicYears.AsNoTracking().Where(a => a.SchoolId == schoolId && a.StartDate <= date && a.EndDate >= date)
                .OrderByDescending(a => a.IsCurrent).ThenByDescending(a => a.StartDate).Select(a => new { a.AcademicYearId, a.YearName }).FirstOrDefaultAsync(ct);
            return y == null ? null : new AccountantYearRow(y.AcademicYearId, y.YearName);
        }

        public async Task<List<AccountantPaymentRow>> GetRecentPaymentsAsync(string schoolId, int take, CancellationToken ct)
        {
            var rows = await (from p in _db.FeePayments.AsNoTracking()
                              join s in _db.Students.AsNoTracking() on new { p.SchoolId, p.StudentId } equals new { s.SchoolId, s.StudentId }
                              where p.SchoolId == schoolId && p.Status == Active
                              orderby p.PaymentDate descending, p.PaymentId descending
                              select new { p.PaymentId, p.ReceiptNumber, p.StudentId, s.FirstName, s.MiddleName, s.LastName, p.Amount, p.PaymentMode, p.PaymentDate })
                             .Take(take).ToListAsync(ct);
            return rows.Select(r => new AccountantPaymentRow(r.PaymentId, r.ReceiptNumber, r.StudentId,
                string.Join(" ", new[] { r.FirstName, r.MiddleName, r.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))), r.Amount, r.PaymentMode, r.PaymentDate)).ToList();
        }
    }
}
