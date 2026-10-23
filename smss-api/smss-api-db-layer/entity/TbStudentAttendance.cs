using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>One student's mark for one day. UNIQUE (student_id, attendance_date): never two marks for the same student and day.</summary>
    [Table("tb_student_attendance", Schema = "public")]
    public class TbStudentAttendance
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("attendance_id")]
        public long AttendanceId { get; set; }

        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("academic_year_id")] public long AcademicYearId { get; set; }
        [Column("section_id")] public long SectionId { get; set; }
        /// <summary>Denormalised from the section (kept consistent by a composite FK) so class-wise reports need no join.</summary>
        [Column("class_id")] public long ClassId { get; set; }
        [Column("student_id")][StringLength(20)] public string StudentId { get; set; } = null!;
        [Column("attendance_date", TypeName = "date")] public DateOnly AttendanceDate { get; set; }
        [Column("attendance_status_id")] public long AttendanceStatusId { get; set; }
        [Column("marked_by")] public long MarkedBy { get; set; }
        [Column("marked_at")] public DateTime MarkedAt { get; set; }
        // Filled together, only when a School Admin corrects a past day
        [Column("corrected_by")] public long? CorrectedBy { get; set; }
        [Column("corrected_at")] public DateTime? CorrectedAt { get; set; }
        [Column("correction_reason")] public string? CorrectionReason { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
