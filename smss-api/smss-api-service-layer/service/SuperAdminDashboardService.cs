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

        private readonly ISubscriptionRepository _repo;
        private readonly ILogger<SuperAdminDashboardService> _logger;

        public SuperAdminDashboardService(ISubscriptionRepository repo, ILogger<SuperAdminDashboardService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public async Task<ApiResponse<SuperAdminSummaryDto>> GetSummaryAsync(CancellationToken ct)
        {
            try
            {
                var today = SchoolClock.Today();
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null)
                {
                    _logger.LogError("Configuration problem: Active status is missing in lut_status");
                    return Result<SuperAdminSummaryDto>(500, "The system is not configured correctly. Please contact support.", null);
                }

                var totals = await _repo.GetTotalsAsync(today, ct);
                var (renewals, renewalTotal) = await _repo.GetUpcomingRenewalsAsync(today, today.AddDays(UpcomingRenewalDays), UpcomingRenewalsShown, ct);
                var pricing = await _repo.GetActivePricingAsync(ct);

                return Result(200, "Successfully fetched", new SuperAdminSummaryDto
                {
                    AsOf = today,
                    TotalSchools = await _repo.CountSchoolsAsync(null, ct),
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
