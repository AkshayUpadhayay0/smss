using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // None of these carry a school id or the collector: both come from the token.

    public class PaymentAllocationRequest
    {
        [Required(ErrorMessage = "Installment is required."), Range(1, long.MaxValue, ErrorMessage = "Installment is required.")]
        public long? FeeDueId { get; set; }

        [Required(ErrorMessage = "Allocated amount is required.")]
        [Range(typeof(decimal), "0.01", "9999999999.99", ErrorMessage = "Allocated amount must be greater than 0.")]
        public decimal? Amount { get; set; }
    }

    public class RecordPaymentRequest
    {
        /// <summary>The FEE amount to put against the dues. Tax is added on top by the server when a <see cref="TaxTypeId"/> is given; without one the payment is exactly this amount.</summary>
        [Required(ErrorMessage = "Amount is required.")]
        [Range(typeof(decimal), "0.01", "9999999999.99", ErrorMessage = "Amount must be greater than 0.")]
        public decimal? Amount { get; set; }

        /// <summary>Optional. One of the school's ACTIVE tax types. There is deliberately no tax amount in this request: a client-sent one is never read.</summary>
        [Range(1, long.MaxValue, ErrorMessage = "Tax type is not valid.")]
        public long? TaxTypeId { get; set; }

        [Required(ErrorMessage = "Payment mode is required."), StringLength(30)]
        public string PaymentMode { get; set; } = null!;

        /// <summary>
        /// How the payment is split over installments. Omit it (or send none) and the payment is allocated automatically,
        /// oldest installment first. When sent, the amounts must add up to <see cref="Amount"/> exactly.
        /// </summary>
        [MaxLength(200)]
        public List<PaymentAllocationRequest>? Allocations { get; set; }

        [StringLength(500, ErrorMessage = "Remarks can be at most 500 characters.")]
        public string? Remarks { get; set; }
    }

    public class VoidPaymentRequest
    {
        [Required(ErrorMessage = "A reason is required to void a payment."), StringLength(500, ErrorMessage = "The reason can be at most 500 characters.")]
        public string VoidReason { get; set; } = null!;
    }

    /// <summary>The fee / tax / total split of one payment, on the history row, the receipt and the "payment recorded" answer. For a payment without tax: TaxAmount 0, tax fields null, TotalAmount = FeeAmount = Amount.</summary>
    public class PaymentTaxFields
    {
        /// <summary>What went against the installments.</summary>
        public decimal FeeAmount { get; set; }
        /// <summary>Name, code and rate as they were when the payment was taken (a later edit never changes them).</summary>
        public string? TaxName { get; set; }
        public string? TaxCode { get; set; }
        public decimal? TaxRatePercent { get; set; }
        public decimal TaxAmount { get; set; }
        /// <summary>Fee + tax: what the payer handed over.</summary>
        public decimal TotalAmount { get; set; }
    }

    public class PaymentSummaryDto : PaymentTaxFields
    {
        public long PaymentId { get; set; }
        public string ReceiptNumber { get; set; } = null!;
        public DateTime PaymentDate { get; set; }
        /// <summary>The TOTAL paid (= TotalAmount). Kept under its old name so existing readers still show what was handed over.</summary>
        public decimal Amount { get; set; }
        public string PaymentMode { get; set; } = null!;
        public string? CollectedByName { get; set; }
        public string? Remarks { get; set; }
        /// <summary>How many installments this payment went to.</summary>
        public int DueCount { get; set; }
        /// <summary>Active or Voided.</summary>
        public string Status { get; set; } = "Active";
        public DateTime? VoidedAt { get; set; }
        public string? VoidedByName { get; set; }
        public string? VoidReason { get; set; }
    }

    public class ReceiptLineDto
    {
        public long FeeDueId { get; set; }
        public string FeeTypeName { get; set; } = null!;
        public string FrequencyName { get; set; } = null!;
        public DateOnly DueDate { get; set; }
        /// <summary>The installment's full amount.</summary>
        public decimal AmountDue { get; set; }
        /// <summary>What THIS payment put towards it.</summary>
        public decimal AllocatedAmount { get; set; }
        /// <summary>Everything paid against the installment so far (this payment included).</summary>
        public decimal PaidToDate { get; set; }
    }

    public class ReceiptSchoolDto
    {
        public string Name { get; set; } = null!;
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? LogoUrl { get; set; }
        /// <summary>The school's GSTIN. Filled ONLY on a receipt that carries tax, and only when the school has one; null otherwise.</summary>
        public string? Gstin { get; set; }
    }

    public class ReceiptStudentDto
    {
        public string StudentId { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? ClassName { get; set; }
        public string? AcademicYearName { get; set; }
    }

    /// <summary>Everything needed to show or print a receipt.</summary>
    public class FeeReceiptDto : PaymentTaxFields
    {
        public long PaymentId { get; set; }
        public string ReceiptNumber { get; set; } = null!;
        public DateTime PaymentDate { get; set; }
        public decimal Amount { get; set; }
        public string PaymentMode { get; set; } = null!;
        public string? Remarks { get; set; }
        public string? CollectedByName { get; set; }
        /// <summary>Active or Voided: a voided receipt can still be opened and printed, marked VOID.</summary>
        public string Status { get; set; } = "Active";
        public DateTime? VoidedAt { get; set; }
        public string? VoidedByName { get; set; }
        public string? VoidReason { get; set; }
        public ReceiptSchoolDto School { get; set; } = new();
        public ReceiptStudentDto Student { get; set; } = new();
        public List<ReceiptLineDto> Lines { get; set; } = new();
    }
}
