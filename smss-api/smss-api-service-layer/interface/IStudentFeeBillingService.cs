using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId is always the caller's own school from the token; null/empty answers 400.
    public interface IStudentFeeBillingService
    {
        /// <summary>
        /// Brings a student's fee items and dues in line with their fee structure and concessions for one academic year:
        /// creates missing items (applying the admission-time <paramref name="selections"/>), re-resolves concession and net
        /// amounts, and regenerates the unpaid future dues. Never touches a due that has a payment. Idempotent.
        /// </summary>
        Task SyncAsync(string schoolId, string studentId, long academicYearId, IReadOnlyList<FeeItemSelection>? selections, CancellationToken ct);

        /// <summary>The fee structure's due day for a fee head changed: move the unpaid future dues sourced from it.</summary>
        Task ApplyDueDayChangeAsync(string schoolId, long feeStructureId, long feeTypeId, int newDueDay, CancellationToken ct);

        /// <summary>The structure's lines changed: bring every student assigned to it up to date.</summary>
        Task SyncStructureAsync(string schoolId, long feeStructureId, CancellationToken ct);

        Task<ApiResponse<StudentFeeItemsDto>> GetItemsAsync(string? schoolId, string studentId, CancellationToken ct);
        Task<ApiResponse<StudentFeeItemsDto>> UpdateItemAsync(string? schoolId, string studentId, long itemId, UpdateFeeItemRequest req, CancellationToken ct);
        Task<ApiResponse<StudentFeeDuesDto>> GetDuesAsync(string? schoolId, string studentId, CancellationToken ct);
        /// <param name="startDate">The date the student starts (admission date); fee installments before its month are not generated.</param>
        Task<ApiResponse<FeePreviewDto>> PreviewAsync(string? schoolId, long academicYearId, long classId, DateOnly? startDate, CancellationToken ct);
    }
}
