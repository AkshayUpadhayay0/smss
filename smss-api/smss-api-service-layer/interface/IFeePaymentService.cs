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
    }
}
