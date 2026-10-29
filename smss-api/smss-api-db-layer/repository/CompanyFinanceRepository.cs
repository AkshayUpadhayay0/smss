using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class CompanyFinanceRepository : ICompanyFinanceRepository
    {
        private const string Active = "Active";   // tb_company_expenses / tb_company_salary_payments / tb_subscription_payments status

        private readonly dbContext _db;
        public CompanyFinanceRepository(dbContext db) => _db = db;

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == statusName && s.Stype == statusType).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        // ---------------------------------------------------------------- company employees
        public Task<List<TbCompanyEmployee>> ListEmployeesAsync(string? search, int? statusId, CancellationToken ct)
        {
            var q = _db.CompanyEmployees.AsNoTracking().AsQueryable();
            if (statusId != null) q = q.Where(e => e.StatusId == statusId);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var like = "%" + search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                q = q.Where(e => EF.Functions.ILike(e.FirstName + " " + e.LastName, like) || EF.Functions.ILike(e.Designation, like) || (e.Email != null && EF.Functions.ILike(e.Email, like)));
            }
            return q.OrderBy(e => e.FirstName).ThenBy(e => e.LastName).ThenBy(e => e.CompanyEmployeeId).ToListAsync(ct);
        }

        public Task<TbCompanyEmployee?> GetEmployeeAsync(long id, bool track, CancellationToken ct)
        {
            var q = _db.CompanyEmployees.Where(e => e.CompanyEmployeeId == id);
            if (!track) q = q.AsNoTracking();
            return q.FirstOrDefaultAsync(ct);
        }

        public void AddEmployee(TbCompanyEmployee employee) => _db.CompanyEmployees.Add(employee);

        // ---------------------------------------------------------------- categories and expenses
        public Task<List<LutExpenseCategory>> GetActiveCategoriesAsync(CancellationToken ct) =>
            _db.ExpenseCategories.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.CategoryId).ToListAsync(ct);

        public Task<bool> CategoryUsableAsync(long categoryId, CancellationToken ct) =>
            _db.ExpenseCategories.AsNoTracking().AnyAsync(c => c.CategoryId == categoryId && c.IsActive, ct);

        public void AddExpense(TbCompanyExpense expense) => _db.CompanyExpenses.Add(expense);

        private IQueryable<ExpenseWithNames> ExpenseQuery() =>
            from e in _db.CompanyExpenses.AsNoTracking()
            join c in _db.ExpenseCategories.AsNoTracking() on e.CategoryId equals c.CategoryId
            join u in _db.Users.AsNoTracking() on e.RecordedBy equals (long?)u.UserId into users
            from u in users.DefaultIfEmpty()
            select new ExpenseWithNames { Expense = e, CategoryName = c.CategoryName, RecordedByName = u == null ? null : u.DisplayName };

        private sealed class ExpenseWithNames
        {
            public TbCompanyExpense Expense { get; set; } = null!;
            public string CategoryName { get; set; } = null!;
            public string? RecordedByName { get; set; }
        }

        public async Task<ExpenseRow?> GetExpenseRowAsync(long expenseId, CancellationToken ct)
        {
            var r = await ExpenseQuery().Where(x => x.Expense.ExpenseId == expenseId).FirstOrDefaultAsync(ct);
            return r == null ? null : new ExpenseRow(r.Expense, r.CategoryName, r.RecordedByName);
        }

        public async Task<(List<ExpenseRow> Rows, int Total, decimal ActiveTotal)> ListExpensesAsync(long? categoryId, DateOnly? from, DateOnly? to, string? status, int page, int pageSize, CancellationToken ct)
        {
            var q = ExpenseQuery();
            if (categoryId != null) q = q.Where(x => x.Expense.CategoryId == categoryId);
            if (from != null) q = q.Where(x => x.Expense.ExpenseDate >= from);
            if (to != null) q = q.Where(x => x.Expense.ExpenseDate <= to);
            if (!string.IsNullOrEmpty(status)) q = q.Where(x => x.Expense.Status == status);

            var total = await q.CountAsync(ct);
            var activeTotal = await q.Where(x => x.Expense.Status == Active).SumAsync(x => (decimal?)x.Expense.Amount, ct) ?? 0m;
            var rows = await q.OrderByDescending(x => x.Expense.ExpenseDate).ThenByDescending(x => x.Expense.ExpenseId).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return (rows.Select(r => new ExpenseRow(r.Expense, r.CategoryName, r.RecordedByName)).ToList(), total, activeTotal);
        }

        public Task<TbCompanyExpense?> LockExpenseAsync(long expenseId, CancellationToken ct) =>
            _db.CompanyExpenses
                .FromSqlInterpolated($"SELECT * FROM public.tb_company_expenses WHERE expense_id = {expenseId} FOR UPDATE")
                .FirstOrDefaultAsync(ct);

        // ---------------------------------------------------------------- salary payments
        public void AddSalaryPayment(TbCompanySalaryPayment payment) => _db.CompanySalaryPayments.Add(payment);

        public Task<bool> ActiveSalaryExistsAsync(long companyEmployeeId, int month, int year, CancellationToken ct) =>
            _db.CompanySalaryPayments.AsNoTracking().AnyAsync(p => p.CompanyEmployeeId == companyEmployeeId && p.PeriodMonth == month && p.PeriodYear == year && p.Status == Active, ct);

        private IQueryable<SalaryWithNames> SalaryQuery() =>
            from p in _db.CompanySalaryPayments.AsNoTracking()
            join e in _db.CompanyEmployees.AsNoTracking() on p.CompanyEmployeeId equals e.CompanyEmployeeId
            join u in _db.Users.AsNoTracking() on p.RecordedBy equals (long?)u.UserId into users
            from u in users.DefaultIfEmpty()
            select new SalaryWithNames { Payment = p, EmployeeName = e.FirstName + " " + e.LastName, RecordedByName = u == null ? null : u.DisplayName };

        private sealed class SalaryWithNames
        {
            public TbCompanySalaryPayment Payment { get; set; } = null!;
            public string EmployeeName { get; set; } = null!;
            public string? RecordedByName { get; set; }
        }

        public async Task<SalaryRow?> GetSalaryRowAsync(long salaryPaymentId, CancellationToken ct)
        {
            var r = await SalaryQuery().Where(x => x.Payment.SalaryPaymentId == salaryPaymentId).FirstOrDefaultAsync(ct);
            return r == null ? null : new SalaryRow(r.Payment, r.EmployeeName, r.RecordedByName);
        }

        public async Task<(List<SalaryRow> Rows, int Total, decimal ActiveTotal)> ListSalaryPaymentsAsync(long? companyEmployeeId, int? year, int? month, string? status, int page, int pageSize, CancellationToken ct)
        {
            var q = SalaryQuery();
            if (companyEmployeeId != null) q = q.Where(x => x.Payment.CompanyEmployeeId == companyEmployeeId);
            if (year != null) q = q.Where(x => x.Payment.PeriodYear == year);
            if (month != null) q = q.Where(x => x.Payment.PeriodMonth == month);
            if (!string.IsNullOrEmpty(status)) q = q.Where(x => x.Payment.Status == status);

            var total = await q.CountAsync(ct);
            var activeTotal = await q.Where(x => x.Payment.Status == Active).SumAsync(x => (decimal?)x.Payment.Amount, ct) ?? 0m;
            var rows = await q.OrderByDescending(x => x.Payment.PeriodYear).ThenByDescending(x => x.Payment.PeriodMonth).ThenByDescending(x => x.Payment.SalaryPaymentId)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return (rows.Select(r => new SalaryRow(r.Payment, r.EmployeeName, r.RecordedByName)).ToList(), total, activeTotal);
        }

        public Task<TbCompanySalaryPayment?> LockSalaryPaymentAsync(long salaryPaymentId, CancellationToken ct) =>
            _db.CompanySalaryPayments
                .FromSqlInterpolated($"SELECT * FROM public.tb_company_salary_payments WHERE salary_payment_id = {salaryPaymentId} FOR UPDATE")
                .FirstOrDefaultAsync(ct);

        // ---------------------------------------------------------------- financial overview
        // Revenue: the payment instant is bucketed by its INDIA calendar month, like every other date the Super Admin sees
        public Task<List<MonthAmount>> GetRevenueByMonthAsync(DateTime fromUtc, DateTime toExclusiveUtc, CancellationToken ct) =>
            _db.Database.SqlQuery<MonthAmount>($@"SELECT date_trunc('month', payment_date AT TIME ZONE 'Asia/Kolkata')::date AS ""Month"", SUM(amount) AS ""Total""
                                                  FROM public.tb_subscription_payments
                                                  WHERE status = 'Active' AND payment_date >= {fromUtc} AND payment_date < {toExclusiveUtc}
                                                  GROUP BY 1").ToListAsync(ct);

        public Task<List<MonthAmount>> GetExpensesByMonthAsync(DateOnly from, DateOnly to, CancellationToken ct) =>
            _db.Database.SqlQuery<MonthAmount>($@"SELECT date_trunc('month', expense_date)::date AS ""Month"", SUM(amount) AS ""Total""
                                                  FROM public.tb_company_expenses
                                                  WHERE status = 'Active' AND expense_date >= {from} AND expense_date <= {to}
                                                  GROUP BY 1").ToListAsync(ct);

        public Task<List<MonthAmount>> GetSalaryByMonthAsync(DateOnly from, DateOnly to, CancellationToken ct) =>
            _db.Database.SqlQuery<MonthAmount>($@"SELECT date_trunc('month', payment_date)::date AS ""Month"", SUM(amount) AS ""Total""
                                                  FROM public.tb_company_salary_payments
                                                  WHERE status = 'Active' AND payment_date >= {from} AND payment_date <= {to}
                                                  GROUP BY 1").ToListAsync(ct);
    }
}
