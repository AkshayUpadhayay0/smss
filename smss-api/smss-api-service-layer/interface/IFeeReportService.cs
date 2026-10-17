using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId is the caller's own school from the token. canManage only marks whether the caller may act (drill-down); the numbers are the same.
    public interface IFeeReportService
    {
        Task<ApiResponse<FeeDashboardDto>> GetDashboardAsync(string? schoolId, long academicYearId, long? classId, long? sectionId, int days, bool canManage, CancellationToken ct);
        Task<ApiResponse<ClassFeeReportDto>> GetClassReportAsync(string? schoolId, long classId, long academicYearId, long? sectionId, bool canManage, CancellationToken ct);
    }
}
