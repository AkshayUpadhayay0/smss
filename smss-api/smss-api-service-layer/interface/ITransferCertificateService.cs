using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId always comes from the caller's token; a student or certificate of another school answers 404.
    public interface ITransferCertificateService
    {
        Task<ApiResponse<TcCheckDto>> CheckAsync(string? schoolId, string? studentId, CancellationToken ct);
        Task<ApiResponse<TcDto>> IssueAsync(string? schoolId, long userId, IssueTcRequest request, CancellationToken ct);
        Task<ApiResponse<List<TcDto>>> ListAsync(string? schoolId, string? studentId, string? status, CancellationToken ct);
        Task<ApiResponse<TcDto>> GetAsync(string? schoolId, long certificateId, CancellationToken ct);
        /// <summary>The original, drawn again from the stored snapshot (every print is audited).</summary>
        Task<TcFileResult> OriginalPdfAsync(string? schoolId, long userId, long certificateId, CancellationToken ct);
        /// <summary>A duplicate: the same number, marked "DUPLICATE COPY No. N" (N is the next for this certificate).</summary>
        Task<TcFileResult> DuplicatePdfAsync(string? schoolId, long userId, long certificateId, CancellationToken ct);
        Task<ApiResponse<TcDto>> VoidAsync(string? schoolId, long userId, long certificateId, VoidTcRequest request, CancellationToken ct);
    }
}
