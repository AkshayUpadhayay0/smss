using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>One piece of homework for a section and subject, owned by the teacher who set it. Deleting hides it (status Inactive); nothing is removed.</summary>
    [Table("tb_homework", Schema = "public")]
    public class TbHomework
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("homework_id")]
        public long HomeworkId { get; set; }

        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("academic_year_id")] public long AcademicYearId { get; set; }
        [Column("class_id")] public long ClassId { get; set; }
        [Column("section_id")] public long SectionId { get; set; }
        [Column("subject_id")] public long SubjectId { get; set; }
        [Column("employee_id")][StringLength(20)] public string EmployeeId { get; set; } = null!;
        [Column("title")][StringLength(200)] public string Title { get; set; } = null!;
        [Column("description")] public string? Description { get; set; }
        [Column("assigned_date", TypeName = "date")] public DateOnly AssignedDate { get; set; }
        [Column("due_date", TypeName = "date")] public DateOnly DueDate { get; set; }
        /// <summary>Key in the private file store, never a URL.</summary>
        [Column("attachment_path")][StringLength(500)] public string? AttachmentPath { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
