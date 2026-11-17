using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>
    /// One explicit "apply to earlier months" (a fee line, all its students) or a back-dated "Charge fees from" at admission (one student): who did it, when, for what,
    /// the date range, how many dues and what total, and the written reason. The back-dated dues (source BACKDATED) point at it.
    /// </summary>
    [Table("tb_fee_backdating_log", Schema = "public")]
    public class TbFeeBackdatingLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("backdating_log_id")]
        public long BackdatingLogId { get; set; }

        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        /// <summary>LINE (a fee line, every student on it) or STUDENT (one student at admission).</summary>
        [Column("kind")][StringLength(10)] public string Kind { get; set; } = "LINE";
        [Column("fee_structure_id")] public long? FeeStructureId { get; set; }
        [Column("fee_type_id")] public long? FeeTypeId { get; set; }
        [Column("student_id")][StringLength(20)] public string? StudentId { get; set; }
        [Column("academic_year_id")] public long AcademicYearId { get; set; }
        [Column("from_date", TypeName = "date")] public DateOnly FromDate { get; set; }
        [Column("to_date", TypeName = "date")] public DateOnly ToDate { get; set; }
        [Column("students_count")] public int StudentsCount { get; set; }
        [Column("dues_count")] public int DuesCount { get; set; }
        [Column("total_amount", TypeName = "numeric(14,2)")] public decimal TotalAmount { get; set; }
        [Column("reason")] public string Reason { get; set; } = null!;
        [Column("created_by")] public long CreatedBy { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
    }
}
