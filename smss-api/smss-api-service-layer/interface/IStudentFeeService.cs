using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId is always the caller's own school from the token; null/empty answers 400.
    public interface IStudentFeeService
    {
        Task<ApiResponse<StudentFeesDto>> GetFeesAsync(string? schoolId, string studentId, CancellationToken ct);
        /// <summary>Assigns, or re-assigns, the student's fee structure for their current academic year.</summary>
        Task<ApiResponse<StudentFeesDto>> AssignStructureAsync(string? schoolId, string studentId, AssignFeeStructureRequest req, CancellationToken ct);
        Task<ApiResponse<StudentFeesDto>> ApplyConcessionAsync(string? schoolId, string studentId, long approvedByUserId, ApplyConcessionRequest req, CancellationToken ct);
        Task<ApiResponse<StudentFeesDto>> RemoveConcessionAsync(string? schoolId, string studentId, long concessionId, CancellationToken ct);
    }
}
