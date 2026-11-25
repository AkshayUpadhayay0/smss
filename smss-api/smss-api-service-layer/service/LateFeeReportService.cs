using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>The Late fee report: read-only, paged, never includes voided payments. A school whose rule is off (or that never charged one) simply gets an empty list and zero totals.</summary>
    public class LateFeeReportService : ILateFeeReportService
    {
        private readonly ILateFeeReportRepository _repo;
        private readonly ILogger<LateFeeReportService> _logger;
        public LateFeeReportService(ILateFeeReportRepository repo, ILogger<LateFeeReportService> logger) { _repo = repo; _logger = logger; }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) where T : class => new() { Status = code is >= 200 and < 300, StatusCode = code, Message = message, Data = data };

        public async Task<ApiResponse<LateFeeReportDto>> GetAsync(string? schoolId, DateOnly? from, DateOnly? to, long? classId, string? view, int page, int pageSize, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<LateFeeReportDto>(400, TenantMessages.NoSchool, null);
            var today = SchoolClock.Today();
            var f = from ?? new DateOnly(today.Year, today.Month, 1);
            var t = to ?? today;
            if (t < f) return Result<LateFeeReportDto>(400, "The end date can't be before the start date.", null);
            var v = string.Equals(view, "waived", StringComparison.OrdinalIgnoreCase) ? "waived" : "charged";
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 25;
            if (pageSize > 100) pageSize = 100;
            try
            {
                if (classId != null && !await _repo.ClassExistsAsync(schoolId, classId.Value, ct)) return Result<LateFeeReportDto>(404, "Class not found.", null);
                var fromUtc = SchoolClock.StartOfDayUtc(f); var toUtc = SchoolClock.StartOfDayUtc(t.AddDays(1));
                var s = await _repo.GetSummaryAsync(schoolId, fromUtc, toUtc, classId, ct);
                var dto = new LateFeeReportDto { From = f, To = t, ClassId = classId, View = v, Page = page, PageSize = pageSize, ChargedCount = s.ChargedCount, ChargedAmount = s.ChargedAmount, WaivedCount = s.WaivedCount, WaivedAmount = s.WaivedAmount };
                var offset = (page - 1) * pageSize;
                if (v == "charged")
                {
                    dto.Charged = (await _repo.ListChargedAsync(schoolId, fromUtc, toUtc, classId, offset, pageSize, ct)).Select(r => new LateFeeChargedItemDto
                    {
                        PaymentId = r.PaymentId, ReceiptNumber = r.ReceiptNumber, PaymentDate = r.PaymentDate, StudentId = r.StudentId, StudentName = r.StudentName, ClassName = r.ClassName, FeeType = r.FeeType,
                        DueDate = r.DueDate, DaysLate = r.DaysLate, DaysCharged = r.DaysCharged, BaseAmount = r.BaseAmount, Amount = r.Amount,
                    }).ToList();
                    dto.TotalItems = s.ChargedCount;
                }
                else
                {
                    dto.Waived = (await _repo.ListWaivedAsync(schoolId, fromUtc, toUtc, classId, offset, pageSize, ct)).Select(r => new LateFeeWaivedItemDto
                    {
                        PaymentId = r.PaymentId, ReceiptNumber = r.ReceiptNumber, PaymentDate = r.PaymentDate, StudentId = r.StudentId, StudentName = r.StudentName, ClassName = r.ClassName,
                        WouldHaveBeen = r.WouldHaveBeen, WaivedBy = r.WaivedBy, Note = r.Note,
                    }).ToList();
                    dto.TotalItems = s.WaivedCount;
                }
                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex) { _logger.LogError(ex, "Late fee report failed"); return Result<LateFeeReportDto>(500, "The late fee report could not be prepared. Please try again.", null); }
        }
    }
}
