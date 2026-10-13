using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace smss_api_db_layer.entity
{
    [Table("tb_student_enrollments", Schema = "public")]
    public class TbStudentEnrollment
    {
        [Key][Column("enrollment_id")] public long EnrollmentId { get; set; }
        [Column("school_id")] public string SchoolId { get; set; } = null!;
        [Column("student_id")][StringLength(20)] public string StudentId { get; set; } = null!;
        // Declares the dependency so EF inserts the student BEFORE its enrollment when both are saved together
        [ForeignKey(nameof(StudentId))] public TbStudent? Student { get; set; }
        [Column("academic_year_id")] public long AcademicYearId { get; set; }
        [Column("class_section_id")] public long ClassSectionId { get; set; }
        [Column("roll_number")] public string? RollNumber { get; set; }
        [Column("enrollment_date", TypeName = "date")] public DateTime EnrollmentDate { get; set; }
        [Column("admission_type_id")] public long AdmissionTypeId { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
