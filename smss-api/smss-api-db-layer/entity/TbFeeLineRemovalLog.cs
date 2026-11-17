using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>
    /// One confirmed removal of a fee line from a fee structure: who removed it, when, and exactly what it did to the students already on the structure (unpaid dues deleted, split into past
    /// and future, items deactivated or removed). Dues with a payment are never touched. The fee type's name is copied here because the line itself is gone afterwards.
    /// </summary>
    [Table("tb_fee_line_removal_log", Schema = "public")]
    public class TbFeeLineRemovalLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("removal_log_id")]
        public long RemovalLogId { get; set; }

        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("fee_structure_id")] public long FeeStructureId { get; set; }
        [Column("fee_type_id")] public long FeeTypeId { get; set; }
        [Column("fee_type_name")][StringLength(150)] public string FeeTypeName { get; set; } = null!;
        /// <summary>Student items on this fee head with at least one payment: kept but switched off.</summary>
        [Column("items_deactivated")] public int ItemsDeactivated { get; set; }
        /// <summary>Student items with no payment at all: removed together with their dues.</summary>
        [Column("items_removed")] public int ItemsRemoved { get; set; }
        [Column("past_dues_deleted")] public int PastDuesDeleted { get; set; }
        [Column("past_amount", TypeName = "numeric(14,2)")] public decimal PastAmount { get; set; }
        [Column("future_dues_deleted")] public int FutureDuesDeleted { get; set; }
        [Column("future_amount", TypeName = "numeric(14,2)")] public decimal FutureAmount { get; set; }
        /// <summary>Dues that carry a payment and were therefore left exactly as they were.</summary>
        [Column("paid_dues_kept")] public int PaidDuesKept { get; set; }
        [Column("created_by")] public long CreatedBy { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
    }
}
