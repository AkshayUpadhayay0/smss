using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId always comes from the caller's token; a student of another school answers 404 "Student not found."
    public interface ISectionChangeService
    {
        Task<ApiResponse<SectionChangeCheckDto>> CheckAsync(string? schoolId, string? studentId, CancellationToken ct);
        Task<ApiResponse<SectionChangeResultDto>> ChangeAsync(string? schoolId, long userId, ChangeSectionRequest request, CancellationToken ct);
        Task<ApiResponse<List<SectionChangeHistoryDto>>> HistoryAsync(string? schoolId, string? studentId, CancellationToken ct);
    }
}
