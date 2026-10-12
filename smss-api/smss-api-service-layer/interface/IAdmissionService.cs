using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId always comes from the caller's token (the controller guarantees it); userId is the signed-in user.
    public interface IAdmissionService
    {
        Task<ApiResponse<List<AdmissionResponseDto>>> GetAllAsync(string schoolId, CancellationToken ct);
        Task<ApiResponse<AdmissionDetailDto>> GetByIdAsync(string schoolId, long admissionId, CancellationToken ct);
        Task<ApiResponse<AdmissionDetailDto>> SubmitAsync(string schoolId, SubmitAdmissionRequestDto req, CancellationToken ct);
        Task<ApiResponse<AdmissionDetailDto>> StartReviewAsync(string schoolId, long admissionId, long reviewedByUserId, CancellationToken ct);
        Task<ApiResponse<AdmissionDetailDto>> ApproveAsync(string schoolId, long admissionId, long approvedByUserId, AdmissionDecisionRequestDto req, CancellationToken ct);
        Task<ApiResponse<AdmissionDetailDto>> RejectAsync(string schoolId, long admissionId, AdmissionDecisionRequestDto req, CancellationToken ct);
        Task<ApiResponse<AdmissionDetailDto>> CancelAsync(string schoolId, long admissionId, AdmissionDecisionRequestDto req, CancellationToken ct);
        Task<ApiResponse<AdmissionDetailDto>> ConfirmAdmissionAsync(string schoolId, long admissionId, ConfirmAdmissionRequestDto req, CancellationToken ct);
    }
}
