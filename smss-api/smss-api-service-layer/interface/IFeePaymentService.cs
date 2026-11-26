using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId is always the caller's own school from the token; null/empty answers 400.
    public interface IFeePaymentService
    {
        /// <summary>Records a payment and puts it against installments, all or nothing. Returns the receipt.</summary>
        Task<ApiResponse<FeeReceiptDto>> RecordPaymentAsync(string? schoolId, string studentId, long collectedByUserId, RecordPaymentRequest req, CancellationToken ct);
        /// <summary>Soft void: marks the payment Voided (kept, never deleted) and takes its amounts back off the installments, in one transaction.</summary>
        Task<ApiResponse<PaymentSummaryDto>> VoidPaymentAsync(string? schoolId, long paymentId, long voidedByUserId, VoidPaymentRequest req, CancellationToken ct);
        Task<ApiResponse<List<PaymentSummaryDto>>> GetPaymentsAsync(string? schoolId, string studentId, CancellationToken ct);
        Task<ApiResponse<FeeReceiptDto>> GetReceiptAsync(string? schoolId, long paymentId, CancellationToken ct);
        /// <summary>The school's ACTIVE concessions, for the Collect Payment form.</summary>
        Task<ApiResponse<List<ConcessionChoiceDto>>> GetConcessionChoicesAsync(string? schoolId, CancellationToken ct);
        /// <summary>A Student's own receipt: the student id comes from the signed-in user (never the request). Someone else's payment, a voided one, or another school's = 404.</summary>
        Task<ApiResponse<FeeReceiptDto>> GetOwnReceiptAsync(string? schoolId, long userId, long paymentId, CancellationToken ct);
        /// <summary>A receipt of one student's own Active payment, in the own view; 404 for any other student's payment. Only for a caller the access helper has cleared.</summary>
        Task<ApiResponse<FeeReceiptDto>> GetReceiptForStudentAsync(string? schoolId, string studentId, long paymentId, CancellationToken ct);
    }
}
