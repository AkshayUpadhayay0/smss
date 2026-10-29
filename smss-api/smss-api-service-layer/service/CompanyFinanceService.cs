using System.Globalization;
using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class CompanyFinanceService : ICompanyFinanceService
    {
        private const int MaxMonths = 60;

        private readonly ICompanyFinanceRepository _repo;
        private readonly ILogger<CompanyFinanceService> _logger;

        public CompanyFinanceService(ICompanyFinanceRepository repo, ILogger<CompanyFinanceService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        // Cash basis: revenue is money actually collected from schools (Active subscription payments), expenses are Active company expenses by their
        // date, salary payouts are Active salary payments by payment date. Net profit = revenue - expenses - salary payouts.
        public async Task<ApiResponse<CompanyFinanceOverviewDto>> GetOverviewAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
        {
            var today = SchoolClock.Today();
            var end = to ?? today;
            var start = from ?? new DateOnly(end.Year, 1, 1);
            if (start > end) return Result<CompanyFinanceOverviewDto>(400, "The 'from' date can't be after the 'to' date.", null);
            var monthCount = (end.Year - start.Year) * 12 + end.Month - start.Month + 1;
            if (monthCount > MaxMonths) return Result<CompanyFinanceOverviewDto>(400, $"The range can cover at most {MaxMonths} months.", null);
            try
            {
                var revenue = await _repo.GetRevenueByMonthAsync(SchoolClock.StartOfDayUtc(start), SchoolClock.StartOfDayUtc(end.AddDays(1)), ct);
                var expenses = await _repo.GetExpensesByMonthAsync(start, end, ct);
                var salary = await _repo.GetSalaryByMonthAsync(start, end, ct);

                var revenueBy = revenue.ToDictionary(m => m.Month, m => m.Total);
                var expensesBy = expenses.ToDictionary(m => m.Month, m => m.Total);
                var salaryBy = salary.ToDictionary(m => m.Month, m => m.Total);

                var months = new List<FinanceMonthDto>();
                for (var m = new DateOnly(start.Year, start.Month, 1); m <= end; m = m.AddMonths(1))
                {
                    var r = revenueBy.GetValueOrDefault(m);
                    var e = expensesBy.GetValueOrDefault(m);
                    var s = salaryBy.GetValueOrDefault(m);
                    months.Add(new FinanceMonthDto { Month = m, Label = m.ToString("MMM yyyy", CultureInfo.InvariantCulture), Revenue = r, Expenses = e, SalaryPayouts = s, NetProfit = r - e - s });
                }

                var totalRevenue = months.Sum(x => x.Revenue);
                var totalExpenses = months.Sum(x => x.Expenses);
                var totalSalary = months.Sum(x => x.SalaryPayouts);
                return Result(200, "Successfully fetched", new CompanyFinanceOverviewDto
                {
                    From = start, To = end, Revenue = totalRevenue, Expenses = totalExpenses, SalaryPayouts = totalSalary,
                    NetProfit = totalRevenue - totalExpenses - totalSalary, Months = months
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while building the company financial overview");
                return Result<CompanyFinanceOverviewDto>(500, "Something went wrong. Please try again later.", default);
            }
        }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };
    }
}
