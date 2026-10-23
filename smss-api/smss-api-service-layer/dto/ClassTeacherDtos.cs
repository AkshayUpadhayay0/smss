using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    public class AssignClassTeacherRequest
    {
        [Required(ErrorMessage = "Academic year is required.")] public long? AcademicYearId { get; set; }
        /// <summary>Optional: the section decides the class. When sent it must be that section's class.</summary>
        public long? ClassId { get; set; }
        [Required(ErrorMessage = "Section is required.")] public long? SectionId { get; set; }
        [Required(ErrorMessage = "Employee is required."), StringLength(20)] public string? EmployeeId { get; set; }
    }

    public class UnassignClassTeacherRequest
    {
        [Required(ErrorMessage = "Academic year is required.")] public long? AcademicYearId { get; set; }
        [Required(ErrorMessage = "Section is required.")] public long? SectionId { get; set; }
    }

    public class ClassTeacherAssignmentDto
    {
        public long AssignmentId { get; set; }
        public long AcademicYearId { get; set; }
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public long SectionId { get; set; }
        public string SectionName { get; set; } = null!;
        public string EmployeeId { get; set; } = null!;
        public string EmployeeName { get; set; } = null!;
        /// <summary>Active = the current class teacher; Inactive = a past one (history).</summary>
        public string StatusName { get; set; } = null!;
        public DateTime AssignedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ClassTeacherSectionDto
    {
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public long SectionId { get; set; }
        public string SectionName { get; set; } = null!;
        public long? AssignmentId { get; set; }
        public string? EmployeeId { get; set; }
        public string? EmployeeName { get; set; }
        public bool HasClassTeacher { get; set; }
    }

    /// <summary>Every active section with its class teacher for one academic year: the admin screen's data.</summary>
    public class ClassTeacherListDto
    {
        public long AcademicYearId { get; set; }
        public string AcademicYearName { get; set; } = null!;
        public List<ClassTeacherSectionDto> Sections { get; set; } = new();
        public int UnassignedCount { get; set; }
    }

    public class EligibleTeacherDto
    {
        public string EmployeeId { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string DesignationName { get; set; } = null!;
    }

    /// <summary>The answer Attendance needs: is this employee the Active class teacher of this section, in this year, right now.</summary>
    public class ClassTeacherCheckDto
    {
        public string EmployeeId { get; set; } = null!;
        public long SectionId { get; set; }
        public long AcademicYearId { get; set; }
        public bool IsClassTeacher { get; set; }
        public long? AssignmentId { get; set; }
    }
}
