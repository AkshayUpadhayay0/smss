using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId always comes from the caller's token; a year, class or section of another school answers 404.
    public interface IPromotionService
    {
        /// <summary>What promoting one class (or section) into a later year WOULD do. Read-only: nothing is written.</summary>
        Task<ApiResponse<PromotionPreviewDto>> PreviewAsync(string? schoolId, PromotionPreviewRequest request, CancellationToken ct);

        /// <summary>
        /// Runs the promotion in one transaction under the school's promotion lock, recomputing everything first. Valid rows are written, the others skipped with a reason; one batch and one item per student is audited.
        /// Fees are assigned afterwards, best-effort. Needs request.Confirm.
        /// </summary>
        Task<ApiResponse<PromotionResultDto>> ExecuteAsync(string? schoolId, long userId, PromotionExecuteRequest request, CancellationToken ct);

        /// <summary>Students enrolled in the year before the current-flagged year with no enrolment in the current one (no PASSED_OUT / LEFT outcome). includeRows = false answers the count only.</summary>
        Task<ApiResponse<PromotionPendingDto>> GetPendingAsync(string? schoolId, bool includeRows, CancellationToken ct);

        /// <summary>What correcting ONE student's promotion (out of sourceYearId) would remove, or plainly why it is refused. Read-only.</summary>
        Task<ApiResponse<PromotionCorrectionCheckDto>> CheckCorrectionAsync(string? schoolId, string? studentId, long sourceYearId, CancellationToken ct);

        /// <summary>
        /// Corrects ONE student's promotion: removes the new-year enrolment and its unpaid fee items and dues, clears the outcome of the old enrolment, writes an audit row. Only while the new year holds no
        /// attendance, payment, marks or concession. The student can then be promoted again.
        /// </summary>
        Task<ApiResponse<PromotionCorrectionResultDto>> CorrectAsync(string? schoolId, long userId, PromotionCorrectionRequest request, CancellationToken ct);
    }
}
