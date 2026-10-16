using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>One generated installment. Its status (Paid / Partially Paid / Pending / Overdue) is never stored: it is computed.</summary>
    [Table("tb_student_fee_dues", Schema = "public")]
    public class TbStudentFeeDue
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("fee_due_id")]
        public long FeeDueId { get; set; }

        [Column("student_fee_item_id")] public long StudentFeeItemId { get; set; }
        [ForeignKey(nameof(StudentFeeItemId))] public TbStudentFeeItem? Item { get; set; }

        [Column("student_id")][StringLength(20)] public string StudentId { get; set; } = null!;
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("due_date", TypeName = "date")] public DateOnly DueDate { get; set; }
        [Column("amount_due", TypeName = "numeric(12,2)")] public decimal AmountDue { get; set; }
        /// <summary>A cached running total written only by payment allocation (a later stage); never edited directly.</summary>
        [Column("amount_paid", TypeName = "numeric(12,2)")] public decimal AmountPaid { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
