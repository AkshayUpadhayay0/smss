using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record ExpenseRow(TbCompanyExpense Expense, string CategoryName, string? RecordedByName);

    public record SalaryRow(TbCompanySalaryPayment Payment, string EmployeeName, string? RecordedByName);

    /// <summary>One calendar month's total, for the Financial Overview. Mapped by column name from raw SQL.</summary>
    public class MonthAmount
    {
        public DateOnly Month { get; set; }
        public decimal Total { get; set; }
    }

    /// <summary>SMSS's own business data (company employees, expenses, salary payments). No school table is touched.</summary>
    public interface ICompanyFinanceRepository
    {
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);

        // company employees
        Task<List<TbCompanyEmployee>> ListEmployeesAsync(string? search, int? statusId, CancellationToken ct);
        Task<TbCompanyEmployee?> GetEmployeeAsync(long id, bool track, CancellationToken ct);
        void AddEmployee(TbCompanyEmployee employee);

        // expense categories and expenses
        Task<List<LutExpenseCategory>> GetActiveCategoriesAsync(CancellationToken ct);
        Task<bool> CategoryUsableAsync(long categoryId, CancellationToken ct);
        void AddExpense(TbCompanyExpense expense);
        Task<ExpenseRow?> GetExpenseRowAsync(long expenseId, CancellationToken ct);
        Task<(List<ExpenseRow> Rows, int Total, decimal ActiveTotal)> ListExpensesAsync(long? categoryId, DateOnly? from, DateOnly? to, string? status, int page, int pageSize, CancellationToken ct);
        /// <summary>Tracked and row-locked (FOR UPDATE). Needs an open transaction.</summary>
        Task<TbCompanyExpense?> LockExpenseAsync(long expenseId, CancellationToken ct);

        // salary payments
        void AddSalaryPayment(TbCompanySalaryPayment payment);
        Task<bool> ActiveSalaryExistsAsync(long companyEmployeeId, int month, int year, CancellationToken ct);
        Task<SalaryRow?> GetSalaryRowAsync(long salaryPaymentId, CancellationToken ct);
        Task<(List<SalaryRow> Rows, int Total, decimal ActiveTotal)> ListSalaryPaymentsAsync(long? companyEmployeeId, int? year, int? month, string? status, int page, int pageSize, CancellationToken ct);
        /// <summary>Tracked and row-locked (FOR UPDATE). Needs an open transaction.</summary>
        Task<TbCompanySalaryPayment?> LockSalaryPaymentAsync(long salaryPaymentId, CancellationToken ct);

        // financial overview: Active rows only, grouped by calendar month
        Task<List<MonthAmount>> GetRevenueByMonthAsync(DateTime fromUtc, DateTime toExclusiveUtc, CancellationToken ct);
        Task<List<MonthAmount>> GetExpensesByMonthAsync(DateOnly from, DateOnly to, CancellationToken ct);
        Task<List<MonthAmount>> GetSalaryByMonthAsync(DateOnly from, DateOnly to, CancellationToken ct);
    }
}
