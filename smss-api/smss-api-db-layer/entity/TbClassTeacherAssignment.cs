using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>
    /// Who is the class teacher of a section for one academic year. Reassigning deactivates the old row (status Inactive) and adds a new
    /// one, so the history stays. The database allows only ONE Active row per (school, year, class, section) (partial unique index).
    /// </summary>
    [Table("tb_class_teacher_assignments", Schema = "public")]
    public class TbClassTeacherAssignment
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("assignment_id")]
        public long AssignmentId { get; set; }

        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("academic_year_id")] public long AcademicYearId { get; set; }
        [Column("class_id")] public long ClassId { get; set; }
        [Column("section_id")] public long SectionId { get; set; }
        [Column("employee_id")][StringLength(20)] public string EmployeeId { get; set; } = null!;
        [Column("status_id")] public int StatusId { get; set; }
        /// <summary>tb_users.user_id of the School Admin who made the assignment, from the token.</summary>
        [Column("assigned_by")] public long? AssignedBy { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
