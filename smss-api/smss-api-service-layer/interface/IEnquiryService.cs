using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    public interface IEnquiryService
    {
        /// <summary>Anonymous. Validates, sanitizes and stores one submission. A filled honeypot gets the normal success answer and nothing is stored.</summary>
        Task<ApiResponse<object>> SubmitAsync(CreateEnquiryRequest request, string? ipAddress, string? userAgent, CancellationToken ct);

        // Super Admin
        Task<ApiResponse<EnquiryListDto>> ListAsync(string? type, string? status, DateOnly? from, DateOnly? to, string? search, string? sort, int page, int pageSize, CancellationToken ct);
        Task<ApiResponse<EnquiryDetailDto>> GetAsync(long id, CancellationToken ct);
        Task<ApiResponse<EnquiryDetailDto>> UpdateStatusAsync(long id, long handledByUserId, UpdateEnquiryStatusRequest request, CancellationToken ct);
        Task<ApiResponse<EnquirySummaryDto>> GetSummaryAsync(CancellationToken ct);
    }
}
