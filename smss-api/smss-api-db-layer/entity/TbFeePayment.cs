using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>One payment transaction. The only thing that marks money as received; an installment's status is derived from these.</summary>
    [Table("tb_fee_payments", Schema = "public")]
    public class TbFeePayment
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("payment_id")]
        public long PaymentId { get; set; }

        [Column("student_id")][StringLength(20)] public string StudentId { get; set; } = null!;
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("payment_date")] public DateTime PaymentDate { get; set; }
        /// <summary>The TOTAL paid: <see cref="FeeAmount"/> + <see cref="TaxAmount"/> (a CHECK enforces it).</summary>
        [Column("amount", TypeName = "numeric(12,2)")] public decimal Amount { get; set; }
        /// <summary>The CASH fee: the gross applied to installments minus <see cref="ConcessionAmount"/>. The allocations add up to FeeAmount + ConcessionAmount. Cash reports read this, never <see cref="Amount"/>.</summary>
        [Column("fee_amount", TypeName = "numeric(12,2)")] public decimal FeeAmount { get; set; }
        /// <summary>Tax added on top of the fee, computed by the server (never sent by the client). 0 when no tax type was chosen.</summary>
        [Column("tax_amount", TypeName = "numeric(12,2)")] public decimal TaxAmount { get; set; }
        [Column("tax_type_id")] public long? TaxTypeId { get; set; }
        /// <summary>Name, code and rate of the tax type AT THE TIME of the payment: later edits or deactivation never change an old receipt.</summary>
        [Column("tax_name_snapshot")][StringLength(100)] public string? TaxNameSnapshot { get; set; }
        [Column("tax_code_snapshot")][StringLength(30)] public string? TaxCodeSnapshot { get; set; }
        [Column("tax_rate_snapshot", TypeName = "numeric(5,2)")] public decimal? TaxRateSnapshot { get; set; }
        /// <summary>The discount given on this payment, computed by the server (never sent by the client). 0 when no concession was chosen. Tax is on FeeAmount, i.e. AFTER this discount.</summary>
        [Column("concession_amount", TypeName = "numeric(12,2)")] public decimal ConcessionAmount { get; set; }
        [Column("concession_id")] public long? ConcessionId { get; set; }
        /// <summary>Name, code, type and value of the concession AT THE TIME of the payment: later edits never change an old receipt.</summary>
        [Column("concession_name_snapshot")][StringLength(100)] public string? ConcessionNameSnapshot { get; set; }
        [Column("concession_code_snapshot")][StringLength(30)] public string? ConcessionCodeSnapshot { get; set; }
        [Column("concession_type_snapshot")][StringLength(20)] public string? ConcessionTypeSnapshot { get; set; }
        [Column("concession_value_snapshot", TypeName = "numeric(10,2)")] public decimal? ConcessionValueSnapshot { get; set; }
        /// <summary>Why the concession was given (required whenever one is chosen).</summary>
        [Column("concession_reason")] public string? ConcessionReason { get; set; }
        /// <summary>The user (tb_users.user_id) who applied it, from the token.</summary>
        [Column("concession_applied_by")] public long? ConcessionAppliedBy { get; set; }
        /// <summary>The late fee this payment charged, computed by the server (never sent by the client): 0 unless <see cref="LateFeeDecision"/> is APPLIED. Part of <see cref="Amount"/>; NOT part of <see cref="FeeAmount"/>.</summary>
        [Column("late_fee_amount", TypeName = "numeric(12,2)")] public decimal LateFeeAmount { get; set; }
        /// <summary>The part of <see cref="TaxAmount"/> that is tax on the late fee (only when the school's setting says tax applies to it).</summary>
        [Column("late_fee_tax_amount", TypeName = "numeric(12,2)")] public decimal LateFeeTaxAmount { get; set; }
        /// <summary>APPLIED, WAIVED (the cashier chose not to charge it, with a note) or null (no late fee was due).</summary>
        [Column("late_fee_decision")][StringLength(10)] public string? LateFeeDecision { get; set; }
        [Column("late_fee_note")] public string? LateFeeNote { get; set; }
        [Column("late_fee_decided_by")] public long? LateFeeDecidedBy { get; set; }
        /// <summary>The rule in force when the payment was taken, the date the days were counted to and each due's figures: a later rule change never rewrites a receipt.</summary>
        [Column("late_fee_rule_snapshot", TypeName = "jsonb")] public string? LateFeeRuleSnapshot { get; set; }
        /// <summary>Cash, Cheque, UPI, Card, Bank Transfer or Online.</summary>
        [Column("payment_mode")][StringLength(30)] public string PaymentMode { get; set; } = null!;
        [Column("receipt_number")][StringLength(50)] public string ReceiptNumber { get; set; } = null!;
        /// <summary>The user (tb_users.user_id) who recorded it, from the token.</summary>
        [Column("collected_by")] public long? CollectedBy { get; set; }
        [Column("remarks")] public string? Remarks { get; set; }
        /// <summary>Active or Voided. A voided payment is kept (with its allocations) as the audit trail; it is never deleted.</summary>
        [Column("status")][StringLength(20)] public string Status { get; set; } = "Active";
        [Column("voided_by")] public long? VoidedBy { get; set; }
        [Column("voided_at")] public DateTime? VoidedAt { get; set; }
        [Column("void_reason")] public string? VoidReason { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }

        public ICollection<TbFeePaymentAllocation> Allocations { get; set; } = new List<TbFeePaymentAllocation>();
    }
}
