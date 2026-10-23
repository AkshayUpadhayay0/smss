using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    public class AssessmentTypeDto
    {
        public long AssessmentTypeId { get; set; }
        public string TypeName { get; set; } = null!;
        public string TypeCode { get; set; } = null!;
    }

    /// <summary>One student's entry: a mark, or absent (never both, never neither).</summary>
    public class AssessmentMarkInput
    {
        [Required, StringLength(20)] public string? StudentId { get; set; }
        public decimal? ObtainedMarks { get; set; }
        public bool IsAbsent { get; set; }
    }

    public class CreateAssessmentRequest
    {
        /// <summary>Accepted for convenience but never trusted: the school always comes from the token. A different school id is refused (403).</summary>
        [StringLength(100)] public string? SchoolId { get; set; }
        [Required(ErrorMessage = "Academic year is required.")] public long? AcademicYearId { get; set; }
        /// <summary>Optional: the section decides the class. When sent it must be that section's class.</summary>
        public long? ClassId { get; set; }
        [Required(ErrorMessage = "Section is required.")] public long? SectionId { get; set; }
        [Required(ErrorMessage = "Subject is required.")] public long? SubjectId { get; set; }
        [Required(ErrorMessage = "Assessment type is required.")] public long? AssessmentTypeId { get; set; }
        /// <summary>The teacher who owns the test. A Teacher always owns their own (leave out, or send their own id); a School Admin names the owning teacher.</summary>
        [StringLength(20)] public string? EmployeeId { get; set; }
        [Required(ErrorMessage = "Title is required."), StringLength(200, ErrorMessage = "The title can be at most 200 characters.")] public string? Title { get; set; }
        [Required(ErrorMessage = "Test date is required.")] public DateOnly? TestDate { get; set; }
        [Required(ErrorMessage = "Maximum marks is required.")] public decimal? MaxMarks { get; set; }
        /// <summary>Must cover EVERY currently active student of the section, exactly once.</summary>
        [Required(ErrorMessage = "The marks are required.")] public List<AssessmentMarkInput>? Marks { get; set; }
    }

    /// <summary>What an update may change. The section, subject, class, academic year and owner are fixed once the test exists.</summary>
    public class UpdateAssessmentRequest
    {
        [Required(ErrorMessage = "Title is required."), StringLength(200, ErrorMessage = "The title can be at most 200 characters.")] public string? Title { get; set; }
        [Required(ErrorMessage = "Test date is required.")] public DateOnly? TestDate { get; set; }
        [Required(ErrorMessage = "Maximum marks is required.")] public decimal? MaxMarks { get; set; }
        [Required(ErrorMessage = "The marks are required.")] public List<AssessmentMarkInput>? Marks { get; set; }
    }

    public class AssessmentDto
    {
        public long AssessmentId { get; set; }
        public long AcademicYearId { get; set; }
        public string AcademicYearName { get; set; } = null!;
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public long SectionId { get; set; }
        public string SectionName { get; set; } = null!;
        public long SubjectId { get; set; }
        public string SubjectName { get; set; } = null!;
        public long AssessmentTypeId { get; set; }
        public string AssessmentTypeName { get; set; } = null!;
        public string EmployeeId { get; set; } = null!;
        public string TeacherName { get; set; } = null!;
        public string Title { get; set; } = null!;
        public DateOnly TestDate { get; set; }
        public decimal MaxMarks { get; set; }
        /// <summary>How many students have a row, and how many of those were absent.</summary>
        public int StudentCount { get; set; }
        public int AbsentCount { get; set; }
        public string StatusName { get; set; } = null!;
    }

    public class AssessmentMarkDto
    {
        public string StudentId { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string? RollNumber { get; set; }
        public decimal? ObtainedMarks { get; set; }
        public bool IsAbsent { get; set; }
        /// <summary>False for a student who has since left the section: their stored result stays on the test but is no longer part of the editable roster.</summary>
        public bool OnRoster { get; set; }
    }

    public class AssessmentDetailDto : AssessmentDto
    {
        public List<AssessmentMarkDto> Marks { get; set; } = new();
    }

    public class AssessmentRosterStudentDto
    {
        public string StudentId { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string? RollNumber { get; set; }
    }

    /// <summary>A student's own result. Deliberately nothing about anyone else: no class average, no rank, no comparison.</summary>
    public class StudentAssessmentResultDto
    {
        public long AssessmentId { get; set; }
        public string Title { get; set; } = null!;
        public string AssessmentTypeName { get; set; } = null!;
        public string SubjectName { get; set; } = null!;
        public DateOnly TestDate { get; set; }
        public decimal MaxMarks { get; set; }
        /// <summary>Null when the student was absent.</summary>
        public decimal? ObtainedMarks { get; set; }
        public bool IsAbsent { get; set; }
    }
}
