using System.Globalization;
using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class SuperAdminDashboardService : ISuperAdminDashboardService
    {
        private const int UpcomingRenewalDays = 30;
        private const int UpcomingRenewalsShown = 10;
        private const int MaxPageSize = 100;
        private static readonly string[] SchoolFilters =
            { SubscriptionStatuses.Trial, SubscriptionStatuses.Active, SubscriptionStatuses.Overdue, SubscriptionStatuses.Cancelled, SubscriptionStatuses.NotSet };

        private const int TrendMonths = 12;
        private const int RecentRows = 10;

        private readonly ISubscriptionRepository _repo;
        private readonly ICompanyFinanceService _finance;
        private readonly ILogger<SuperAdminDashboardService> _logger;

        public SuperAdminDashboardService(ISubscriptionRepository repo, ICompanyFinanceService finance, ILogger<SuperAdminDashboardService> logger)
        {
            _repo = repo;
            _finance = finance;
            _logger = logger;
        }

        private static DashboardStatDto Stat(decimal current, decimal previous) =>
            new() { Value = current, PreviousValue = previous, TrendPercent = SubscriptionRules.TrendPercent(current, previous) };

        public async Task<ApiResponse<SuperAdminSummaryDto>> GetSummaryAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
        {
            var today = SchoolClock.Today();
            var end = to ?? today;
            var start = from ?? new DateOnly(end.Year, end.Month, 1);
            if (start > end) return Result<SuperAdminSummaryDto>(400, "The 'from' date can't be after the 'to' date.", null);
            try
            {
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null)
                {
                    _logger.LogError("Configuration problem: Active status is missing in lut_status");
                    return Result<SuperAdminSummaryDto>(500, "The system is not configured correctly. Please contact support.", null);
                }

                var totals = await _repo.GetTotalsAsync(today, ct);
                var (renewals, renewalTotal) = await _repo.GetUpcomingRenewalsAsync(today, today.AddDays(UpcomingRenewalDays), UpcomingRenewalsShown, ct);
                var pricing = await _repo.GetActivePricingAsync(ct);
                var totalSchools = await _repo.CountSchoolsAsync(null, ct);

                // Revenue / expenses / salary come from the Company Finance overview, so the dashboard and that page can never disagree
                var (prevStart, prevEnd) = SubscriptionRules.PreviousPeriod(start, end);
                var trendStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-(TrendMonths - 1));
                var period = await _finance.GetOverviewAsync(start, end, ct);
                if (period.Data == null) return Result<SuperAdminSummaryDto>(period.StatusCode, period.Message, null);
                var previous = await _finance.GetOverviewAsync(prevStart, prevEnd, ct);
                if (previous.Data == null) return Result<SuperAdminSummaryDto>(previous.StatusCode, previous.Message, null);
                var trend = await _finance.GetOverviewAsync(trendStart, today, ct);
                if (trend.Data == null) return Result<SuperAdminSummaryDto>(trend.StatusCode, trend.Message, null);

                var periodFromUtc = SchoolClock.StartOfDayUtc(start);
                var periodToUtc = SchoolClock.StartOfDayUtc(end.AddDays(1));
                var schoolsAtEnd = await _repo.CountSchoolsCreatedBeforeAsync(periodToUtc, ct);
                var schoolsAtPrevEnd = await _repo.CountSchoolsCreatedBeforeAsync(SchoolClock.StartOfDayUtc(prevEnd.AddDays(1)), ct);

                var registered = await _repo.GetSchoolCreatedAtAsync(SchoolClock.StartOfDayUtc(trendStart), SchoolClock.StartOfDayUtc(today.AddDays(1)), ct);
                var registeredBy = registered.GroupBy(c => { var d = SchoolClock.DateOf(c); return new DateOnly(d.Year, d.Month, 1); }).ToDictionary(g => g.Key, g => g.Count());

                var invoices = await _repo.GetInvoiceAmountsAsync(periodFromUtc, periodToUtc, ct);
                var buckets = invoices.GroupBy(i => SubscriptionRules.CollectionBucket(i.NetAmount, i.Paid, i.DueDate, today)).ToDictionary(g => g.Key);

                var recentSchools = await _repo.GetRecentSchoolsAsync(RecentRows, ct);
                var recentPayments = await _repo.GetRecentPaymentsAsync(periodFromUtc, periodToUtc, RecentRows, ct);

                var p = period.Data;
                var pv = previous.Data;
                var activeSubs = totals.Active + totals.Overdue;
                var spend = p.Expenses + p.SalaryPayouts;
                var prevSpend = pv.Expenses + pv.SalaryPayouts;

                return Result(200, "Successfully fetched", new SuperAdminSummaryDto
                {
                    AsOf = today,
                    From = start, To = end, PreviousFrom = prevStart, PreviousTo = prevEnd,
                    Stats = new DashboardStatsDto
                    {
                        TotalSchools = Stat(schoolsAtEnd, schoolsAtPrevEnd),
                        ActiveSubscriptions = new DashboardStatDto { Value = activeSubs },
                        ActiveSubscriptionsPercentOfTotal = totalSchools == 0 ? 0 : Math.Round(activeSubs * 100m / totalSchools, 1, MidpointRounding.AwayFromZero),
                        PaymentsCollected = Stat(p.Revenue, pv.Revenue),
                        OutstandingPayments = new DashboardStatDto { Value = totals.Outstanding },
                        Expenses = Stat(spend, prevSpend),
                        SalaryPayouts = p.SalaryPayouts,
                        CashSurplus = Stat(p.NetProfit, pv.NetProfit)
                    },
                    RevenueVsExpenses = trend.Data.Months.Select(m => new RevenueExpenseMonthDto { Month = m.Month, Label = m.Label, Revenue = m.Revenue, Expenses = m.Expenses + m.SalaryPayouts }).ToList(),
                    SchoolRegistrations = Enumerable.Range(0, TrendMonths).Select(i => trendStart.AddMonths(i))
                        .Select(m => new MonthCountDto { Month = m, Label = m.ToString("MMM yyyy", CultureInfo.InvariantCulture), Count = registeredBy.GetValueOrDefault(m) }).ToList(),
                    PaymentCollection = CollectionBuckets.All.Select(k => new CollectionBucketDto
                    {
                        Key = k,
                        Count = buckets.TryGetValue(k, out var g) ? g.Count() : 0,
                        Amount = buckets.TryGetValue(k, out g) ? g.Sum(i => i.NetAmount) : 0m,
                        Outstanding = buckets.TryGetValue(k, out g) ? g.Sum(i => i.NetAmount - i.Paid) : 0m
                    }).ToList(),
                    RecentSchools = recentSchools.Select(s => new RecentSchoolDto
                    {
                        SchoolId = s.SchoolId, SchoolCode = s.SchoolCode, SchoolName = s.SchoolName, BoardType = s.BoardName, SchoolType = s.SchoolTypeName,
                        RegisteredOn = SchoolClock.DateOf(s.CreatedAt), Status = s.StatusName
                    }).ToList(),
                    RecentTransactions = recentPayments.Select(t => new RecentTransactionDto
                    {
                        PaymentId = t.PaymentId, ReferenceNumber = t.ReferenceNumber, SchoolId = t.SchoolId, SchoolName = t.SchoolName,
                        PaymentDate = SchoolClock.DateOf(t.PaymentDate), Amount = t.Amount, PaymentMode = t.PaymentMode, Status = t.Status
                    }).ToList(),
                    TotalSchools = totalSchools,
                    ActiveSchools = await _repo.CountSchoolsAsync(activeId, ct),
                    TrialCount = totals.Trial, ActiveCount = totals.Active, OverdueCount = totals.Overdue,
                    CancelledCount = totals.Cancelled, NotSetCount = totals.NotSet, TrialExpiredCount = totals.TrialExpired,
                    TotalCollected = totals.Collected, TotalOutstanding = totals.Outstanding, OverdueAmount = totals.OverdueAmount,
                    UpcomingRenewalDays = UpcomingRenewalDays, UpcomingRenewalCount = renewalTotal,
                    UpcomingRenewals = renewals.Select(r => new UpcomingRenewalDto { SchoolId = r.SchoolId, SchoolName = r.SchoolName, BillingFrequency = r.BillingFrequency, NextRenewalDate = r.NextRenewalDate }).ToList(),
                    Pricing = pricing == null ? null : new SubscriptionPricingDto
                    {
                        PricingId = pricing.PricingId, RatePerStudentMonthly = pricing.RatePerStudentMonthly, YearlyDiscountPercent = pricing.YearlyDiscountPercent,
                        TrialDays = pricing.TrialDays, EffectiveFrom = pricing.EffectiveFrom, IsActive = pricing.IsActive, CreatedAt = pricing.CreatedAt
                    }
                });
            }
            catch (Exception ex) { return Error<SuperAdminSummaryDto>(ex, "building the Super Admin summary"); }
        }

        public async Task<ApiResponse<SchoolSubscriptionPagedDto>> ListSchoolsAsync(string? search, string? status, int page, int pageSize, CancellationToken ct)
        {
            string? filter = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                filter = SchoolFilters.FirstOrDefault(f => string.Equals(f, status.Trim(), StringComparison.OrdinalIgnoreCase));
                if (filter == null) return Result<SchoolSubscriptionPagedDto>(400, $"Status must be one of: {string.Join(", ", SchoolFilters)}.", null);
            }
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
            try
            {
                var today = SchoolClock.Today();
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null)
                {
                    _logger.LogError("Configuration problem: Active status is missing in lut_status");
                    return Result<SchoolSubscriptionPagedDto>(500, "The system is not configured correctly. Please contact support.", null);
                }

                var (rows, total) = await _repo.ListSchoolSubscriptionsAsync(search, filter, today, page, pageSize, ct);
                var counts = await _repo.GetHeadcountsAsync(rows.Select(r => r.SchoolId).ToList(), activeId.Value, ct);

                return Result(200, "Successfully fetched", new SchoolSubscriptionPagedDto
                {
                    Items = rows.Select(r => ToDto(r, counts.GetValueOrDefault(r.SchoolId) ?? new SchoolHeadcount(0, 0, 0), today)).ToList(),
                    Total = total, Page = page, PageSize = pageSize
                });
            }
            catch (Exception ex) { return Error<SchoolSubscriptionPagedDto>(ex, "listing schools with their subscription"); }
        }

        private static SchoolSubscriptionRowDto ToDto(SchoolSubscriptionRow r, SchoolHeadcount head, DateOnly today)
        {
            var sub = r.Subscription;
            return new SchoolSubscriptionRowDto
            {
                SchoolId = r.SchoolId, SchoolCode = r.SchoolCode, SchoolName = r.SchoolName, SchoolStatusId = r.SchoolStatusId,
                SubscriptionStatus = SubscriptionRules.EffectiveStatus(sub?.Status, r.HasOverdueInvoice),
                BillingFrequency = sub?.BillingFrequency,
                TrialEndDate = sub?.TrialEndDate,
                TrialExpired = sub != null && sub.Status == SubscriptionStatuses.Trial && today > sub.TrialEndDate,
                NextRenewalDate = sub?.NextRenewalDate,
                CustomRatePerStudentMonthly = sub?.CustomRatePerStudentMonthly, CustomYearlyDiscountPercent = sub?.CustomYearlyDiscountPercent,
                IncludeStaffInBilling = sub?.IncludeStaffInBilling ?? false,
                StudentCount = head.Students, TeacherCount = head.Teachers, StaffCount = head.Staff,
                BillableUserCount = SubscriptionRules.BillableUsers(head.Students, head.Teachers, head.Staff, sub?.IncludeStaffInBilling ?? false)
            };
        }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        private ApiResponse<T> Error<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", default);
        }
    }
}
