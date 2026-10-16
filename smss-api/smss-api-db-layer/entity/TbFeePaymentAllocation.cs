using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>The part of a payment that went to one installment.</summary>
    [Table("tb_fee_payment_allocations", Schema = "public")]
    public class TbFeePaymentAllocation
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("allocation_id")]
        public long AllocationId { get; set; }

        [Column("payment_id")] public long PaymentId { get; set; }
        [ForeignKey(nameof(PaymentId))] public TbFeePayment? Payment { get; set; }

        [Column("fee_due_id")] public long FeeDueId { get; set; }
        [Column("allocated_amount", TypeName = "numeric(12,2)")] public decimal AllocatedAmount { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
    }
}
