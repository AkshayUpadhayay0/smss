using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>
    /// What one student owes for one fee head in one academic year: the frequency THIS student pays it at, whether it applies
    /// to them, and the amounts. base/net are per period of the fee structure line (net = base - concession).
    /// </summary>
    [Table("tb_student_fee_items", Schema = "public")]
    public class TbStudentFeeItem
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("student_fee_item_id")]
        public long StudentFeeItemId { get; set; }

        [Column("student_id")][StringLength(20)] public string StudentId { get; set; } = null!;
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("academic_year_id")] public long AcademicYearId { get; set; }
        [Column("fee_type_id")] public long FeeTypeId { get; set; }
        [Column("frequency_id")] public long FrequencyId { get; set; }
        [Column("base_amount", TypeName = "numeric(12,2)")] public decimal BaseAmount { get; set; }
        [Column("concession_amount", TypeName = "numeric(12,2)")] public decimal ConcessionAmount { get; set; }
        [Column("net_amount", TypeName = "numeric(12,2)")] public decimal NetAmount { get; set; }
        [Column("is_applicable")] public bool IsApplicable { get; set; } = true;
        [Column("isactive")] public bool IsActive { get; set; } = true;
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }

        public ICollection<TbStudentFeeDue> Dues { get; set; } = new List<TbStudentFeeDue>();
    }
}
