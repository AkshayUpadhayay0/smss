using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    /// <summary>S3: the subjects of ONE class in ONE academic year, and the per-section exceptions. No school id: it comes from the token.</summary>
    public class YearClassSubjectsDto
    {
        public long AcademicYearId { get; set; }
        public string YearName { get; set; } = null!;
        public bool IsCurrent { get; set; }
        public bool IsClosed { get; set; }
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        /// <summary>False when the class is not offered in the year: nothing can be mapped until it is offered (Academic Setup, classes and sections).</summary>
        public bool ClassOffered { get; set; }
        public List<SetupSubjectDto> Subjects { get; set; } = new();
        public List<SetupSectionDto> Sections { get; set; } = new();
    }

    public class SetupSubjectDto
    {
        public long SubjectId { get; set; }
        public string SubjectName { get; set; } = null!;
        public string? SubjectCode { get; set; }
        public bool IsActive { get; set; }
        /// <summary>Mapped to the class in this year.</summary>
        public bool Mapped { get; set; }
        /// <summary>Timetable slots, homework, tests, entered marks and exam setups in this year that use the subject for this class (0 = free to remove).</summary>
        public int InUse { get; set; }
        public string? InUseText { get; set; }
    }

    public class SetupSectionDto
    {
        public long SectionId { get; set; }
        public string SectionName { get; set; } = null!;
        /// <summary>Taught here although the class does not have it (ADD).</summary>
        public List<long> ExtraSubjectIds { get; set; } = new();
        /// <summary>The class has it but this section does not take it (REMOVE).</summary>
        public List<long> NotTaughtSubjectIds { get; set; } = new();
        /// <summary>Subjects of this section that are in use here (cannot be taken away).</summary>
        public List<long> InUseSubjectIds { get; set; } = new();
        public int EffectiveCount { get; set; }
    }

    /// <summary>The WHOLE subject set of the class for the year (empty = none), and the exceptions of its sections (sections not listed have none).</summary>
    public class SaveYearClassSubjectsRequest
    {
        [Required(ErrorMessage = "Subject list is required (it may be empty).")]
        public List<long>? SubjectIds { get; set; }
        public List<SectionSubjectOverrideRequest>? Sections { get; set; }
    }

    public class SectionSubjectOverrideRequest
    {
        [Range(1, long.MaxValue, ErrorMessage = "Section is required.")]
        public long SectionId { get; set; }
        public List<long>? ExtraSubjectIds { get; set; }
        public List<long>? NotTaughtSubjectIds { get; set; }
    }
}
