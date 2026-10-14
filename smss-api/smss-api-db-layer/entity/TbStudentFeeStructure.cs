using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>The fee structure a student pays under for one academic year.</summary>
    [Table("tb_student_fee_structure", Schema = "public")]
    public class TbStudentFeeStructure
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("student_fee_id")]
        public long StudentFeeId { get; set; }

        [Column("student_id")][StringLength(20)] public string StudentId { get; set; } = null!;
        // Declares the dependency so EF inserts the student BEFORE this row when both are saved together
        [ForeignKey(nameof(StudentId))] public TbStudent? Student { get; set; }

        [Column("fee_structure_id")] public long FeeStructureId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("academic_year_id")] public long AcademicYearId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
