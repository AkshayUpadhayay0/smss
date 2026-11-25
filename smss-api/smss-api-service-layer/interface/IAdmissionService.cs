using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId always comes from the caller's token (the controller guarantees it); userId is the signed-in user.
    public interface IAdmissionService
    {
        /// <summary>Every application of the school; each filter is optional (<paramref name="status"/> is an ADMISSION_STATUS name such as APPLICATION_SUBMITTED).</summary>
        Task<ApiResponse<List<AdmissionResponseDto>>> GetAllAsync(string schoolId, string? status, long? classId, long? academicYearId, CancellationToken ct);
        Task<ApiResponse<AdmissionDetailDto>> GetByIdAsync(string schoolId, long admissionId, CancellationToken ct);
        Task<ApiResponse<AdmissionDetailDto>> SubmitAsync(string schoolId, SubmitAdmissionRequestDto req, CancellationToken ct);
        /// <summary>Edit the applicant's details. Only while the application is added (not yet confirmed, rejected or withdrawn).</summary>
        Task<ApiResponse<AdmissionDetailDto>> UpdateAsync(string schoolId, long admissionId, SubmitAdmissionRequestDto req, CancellationToken ct);
        Task<ApiResponse<AdmissionDetailDto>> RejectAsync(string schoolId, long admissionId, long rejectedByUserId, AdmissionDecisionRequestDto req, CancellationToken ct);
        Task<ApiResponse<AdmissionDetailDto>> CancelAsync(string schoolId, long admissionId, AdmissionDecisionRequestDto req, CancellationToken ct);
        Task<ApiResponse<AdmissionDetailDto>> ConfirmAdmissionAsync(string schoolId, long admissionId, long confirmedByUserId, ConfirmAdmissionRequestDto req, CancellationToken ct);
    }
}
