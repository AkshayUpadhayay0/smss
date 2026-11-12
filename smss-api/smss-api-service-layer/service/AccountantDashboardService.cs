using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>
    /// The Accountant dashboard, read-only. The Fee dashboard service supplies this month / outstanding / overdue / upcoming and the 6-month series (so these numbers equal the
    /// Fee Dashboard page); a small read-only repository supplies today's collection and the latest payments. A part that fails is named in PartsUnavailable, never shown as 0.
    /// </summary>
    public class AccountantDashboardService : IAccountantDashboardService
    {
        public const int UpcomingDays = 7;
        public const int RecentPaymentRows = 10;

        private readonly IFeeReportService _fees;
        private readonly IAccountantDashboardRepository _repo;
        private readonly ISchoolDashboardRepository _years;
        private readonly ILogger<AccountantDashboardService> _logger;

        public AccountantDashboardService(IFeeReportService fees, IAccountantDashboardRepository repo, ISchoolDashboardRepository years, ILogger<AccountantDashboardService> logger)
        {
            _fees = fees; _repo = repo; _years = years; _logger = logger;
        }

        public async Task<ApiResponse<AccountantDashboardDto>> GetSummaryAsync(string? schoolId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result(400, TenantMessages.NoSchool, null);
            try
            {
                var today = SchoolClock.Today();
                var dto = new AccountantDashboardDto { Today = today };
                dto.Stats.UpcomingDays = UpcomingDays;

                // Collected today: the India calendar day, as the Fee dashboard also reads payment dates. Independent of the academic year.
                try
                {
                    dto.Stats.CollectedToday = await _repo.GetCollectedBetweenAsync(schoolId, SchoolClock.StartOfDayUtc(today), SchoolClock.StartOfDayUtc(today.AddDays(1)), ct);
                }
                catch (Exception ex) { Down(AccountantDashboardParts.CollectedToday, dto, ex); }

                try
                {
                    dto.RecentPayments = (await _repo.GetRecentPaymentsAsync(schoolId, RecentPaymentRows, ct)).Select(p => new DashboardPaymentDto
                    {
                        PaymentId = p.PaymentId, ReceiptNumber = p.ReceiptNumber, StudentId = p.StudentId, StudentName = p.StudentName,
                        Amount = p.Amount, PaymentMode = p.PaymentMode, PaymentDate = p.PaymentDate
                    }).ToList();
                }
                catch (Exception ex) { Down(AccountantDashboardParts.Payments, dto, ex); }

                await FillFeesAsync(schoolId, today, dto, ct);
                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while building the Accountant dashboard");
                return Result(500, "Something went wrong. Please try again later.", null);
            }
        }

        private async Task FillFeesAsync(string schoolId, DateOnly today, AccountantDashboardDto dto, CancellationToken ct)
        {
            try
            {
                var year = await _repo.GetYearContainingAsync(schoolId, today, ct);
                if (year == null) { Down(AccountantDashboardParts.Fees, dto, null, "no academic year contains today"); return; }
                var yearId = year.AcademicYearId;
                dto.AcademicYearId = yearId;
                dto.AcademicYearName = year.YearName;

                var current = await _fees.GetDashboardAsync(schoolId, yearId, null, null, UpcomingDays, true, ct);
                if (!current.Status || current.Data == null) { Down(AccountantDashboardParts.Fees, dto, null, $"{current.StatusCode}: {current.Message}"); return; }

                // The six-month window can reach back into the previous academic year, whose payments the current year's dashboard does not hold: add any other year it touches.
                var months = SchoolDashboardService.LastMonths(today);
                var dashboards = new List<FeeDashboardDto> { current.Data };
                foreach (var span in await _years.GetYearsOverlappingAsync(schoolId, months[0], months[^1].AddMonths(1).AddDays(-1), ct))
                {
                    if (span.AcademicYearId == yearId) continue;
                    var other = await _fees.GetDashboardAsync(schoolId, span.AcademicYearId, null, null, UpcomingDays, true, ct);
                    if (!other.Status || other.Data == null) { Down(AccountantDashboardParts.Fees, dto, null, $"{other.StatusCode}: {other.Message}"); return; }   // a partial series would understate a month
                    dashboards.Add(other.Data);
                }

                var d = current.Data;
                dto.CollectionTrend = SchoolDashboardService.FeeTrend(months, dashboards);
                dto.Stats.CollectedThisMonth = dto.CollectionTrend[^1].Amount;
                dto.Stats.Outstanding = d.TotalOutstanding;
                dto.Stats.OverdueCount = d.OverdueCount;
                dto.Stats.OverdueAmount = d.OverdueAmount;
                dto.Stats.UpcomingCount = d.UpcomingCount;
                dto.Stats.UpcomingAmount = d.UpcomingAmount;
            }
            catch (Exception ex)
            {
                dto.CollectionTrend = new();
                Down(AccountantDashboardParts.Fees, dto, ex);
            }
        }

        private void Down(string part, AccountantDashboardDto dto, Exception? ex, string? reason = null)
        {
            if (ex != null) _logger.LogWarning(ex, "Accountant dashboard: could not load {Part}", part);
            else _logger.LogWarning("Accountant dashboard: could not load {Part} ({Reason})", part, reason);
            if (!dto.PartsUnavailable.Contains(part)) dto.PartsUnavailable.Add(part);
        }

        private static ApiResponse<AccountantDashboardDto> Result(int code, string message, AccountantDashboardDto? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };
    }
}
