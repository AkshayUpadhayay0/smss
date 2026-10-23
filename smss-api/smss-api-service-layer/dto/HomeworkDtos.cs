using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    public class CreateHomeworkRequest
    {
        /// <summary>Optional: when left out, the academic year that contains the assigned date is used.</summary>
        public long? AcademicYearId { get; set; }
        [Required(ErrorMessage = "Section is required.")] public long? SectionId { get; set; }
        [Required(ErrorMessage = "Subject is required.")] public long? SubjectId { get; set; }
        /// <summary>
        /// The teacher who owns the homework. A Teacher always owns their own (leave it out, or send their own id). A School Admin names the owning teacher
        /// (left out, it is the admin's own employee id if they have one).
        /// </summary>
        [StringLength(20)] public string? EmployeeId { get; set; }
        [Required(ErrorMessage = "Title is required."), StringLength(200, ErrorMessage = "The title can be at most 200 characters.")] public string? Title { get; set; }
        [StringLength(2000, ErrorMessage = "The description can be at most 2000 characters.")] public string? Description { get; set; }
        /// <summary>Optional: today (India date) when left out.</summary>
        public DateOnly? AssignedDate { get; set; }
        [Required(ErrorMessage = "Due date is required.")] public DateOnly? DueDate { get; set; }
    }

    /// <summary>What an update may change. The section, subject, academic year and owning teacher are fixed once the homework exists.</summary>
    public class UpdateHomeworkRequest
    {
        [Required(ErrorMessage = "Title is required."), StringLength(200, ErrorMessage = "The title can be at most 200 characters.")] public string? Title { get; set; }
        [StringLength(2000, ErrorMessage = "The description can be at most 2000 characters.")] public string? Description { get; set; }
        [Required(ErrorMessage = "Assigned date is required.")] public DateOnly? AssignedDate { get; set; }
        [Required(ErrorMessage = "Due date is required.")] public DateOnly? DueDate { get; set; }
    }

    public class HomeworkDto
    {
        public long HomeworkId { get; set; }
        public long AcademicYearId { get; set; }
        public string AcademicYearName { get; set; } = null!;
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public long SectionId { get; set; }
        public string SectionName { get; set; } = null!;
        public long SubjectId { get; set; }
        public string SubjectName { get; set; } = null!;
        public string EmployeeId { get; set; } = null!;
        public string TeacherName { get; set; } = null!;
        public string Title { get; set; } = null!;
        public string? Description { get; set; }
        public DateOnly AssignedDate { get; set; }
        public DateOnly DueDate { get; set; }
        public bool HasAttachment { get; set; }
        /// <summary>The name the attachment downloads as (the storage path itself is never exposed).</summary>
        public string? AttachmentFileName { get; set; }
        public string StatusName { get; set; } = null!;
    }
}
