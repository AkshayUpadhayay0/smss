using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId always comes from the caller's token; a student of another school answers 404 "Student not found."
    public interface ILeavingService
    {
        Task<ApiResponse<LeavingCheckDto>> CheckAsync(string? schoolId, string? studentId, DateOnly? leavingDate, CancellationToken ct);
        Task<ApiResponse<RecordLeavingResultDto>> RecordAsync(string? schoolId, long userId, RecordLeavingRequest request, CancellationToken ct);
        Task<ApiResponse<LeavingCorrectionCheckDto>> CheckCorrectionAsync(string? schoolId, string? studentId, CancellationToken ct);
        Task<ApiResponse<LeavingCorrectionCheckDto>> CorrectAsync(string? schoolId, long userId, LeavingCorrectionRequest request, CancellationToken ct);
        Task<ApiResponse<List<LeaverDto>>> ListLeaversAsync(string? schoolId, long? academicYearId, CancellationToken ct);
    }
}
