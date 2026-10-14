using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>A fee concession applied to a student.</summary>
    [Table("tb_student_fee_concessions", Schema = "public")]
    public class TbStudentFeeConcession
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("student_fee_concession_id")]
        public long StudentFeeConcessionId { get; set; }

        [Column("student_id")][StringLength(20)] public string StudentId { get; set; } = null!;
        [Column("concession_id")] public long ConcessionId { get; set; }
        /// <summary>The user (tb_users.user_id) who applied it, from the token.</summary>
        [Column("approved_by")] public long? ApprovedBy { get; set; }
        [Column("remarks")] public string? Remarks { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
