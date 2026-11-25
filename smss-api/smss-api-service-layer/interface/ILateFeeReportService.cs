using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId always comes from the caller's token; a class of another school answers 404.
    public interface ILateFeeReportService
    {
        /// <summary>The Late fee report. <paramref name="from"/> / <paramref name="to"/> are India calendar days (default: this month); view is "charged" or "waived".</summary>
        Task<ApiResponse<LateFeeReportDto>> GetAsync(string? schoolId, DateOnly? from, DateOnly? to, long? classId, string? view, int page, int pageSize, CancellationToken ct);
    }
}
