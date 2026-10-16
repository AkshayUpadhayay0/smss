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
        [Column("amount", TypeName = "numeric(12,2)")] public decimal Amount { get; set; }
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
