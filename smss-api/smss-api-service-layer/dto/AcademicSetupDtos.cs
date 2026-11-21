using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    /// <summary>One academic year's offering: every class and section master of the school, flagged with whether THIS year offers it, and what is already in use.</summary>
    public class YearOfferingDto
    {
        public long AcademicYearId { get; set; }
        public string YearName { get; set; } = null!;
        public bool IsCurrent { get; set; }
        public bool IsClosed { get; set; }
        public int OfferedClasses { get; set; }
        public int OfferedSections { get; set; }
        public List<OfferingClassDto> Classes { get; set; } = new();
    }

    public class OfferingClassDto
    {
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public short SequenceOrder { get; set; }
        /// <summary>The class master is Active. An inactive class cannot be newly offered.</summary>
        public bool IsActive { get; set; }
        public bool Offered { get; set; }
        /// <summary>Active enrolments of this class's sections in this year.</summary>
        public int Enrolments { get; set; }
        public bool HasFeeStructure { get; set; }
        public List<OfferingSectionDto> Sections { get; set; } = new();
    }

    public class OfferingSectionDto
    {
        public long SectionId { get; set; }
        public string SectionName { get; set; } = null!;
        public bool IsActive { get; set; }
        public bool Offered { get; set; }
        /// <summary>The section master's capacity.</summary>
        public short? MasterMaxStrength { get; set; }
        /// <summary>This year's own capacity; null = use the master's.</summary>
        public short? MaxStrength { get; set; }
        public short? EffectiveMaxStrength { get; set; }
        public int Enrolments { get; set; }
        public bool HasClassTeacher { get; set; }
        public int TimetableEntries { get; set; }
    }

    /// <summary>The WHOLE set of classes and sections the year offers. Anything not listed is withdrawn from the year (refused while it is in use).</summary>
    public class SaveYearOfferingRequest
    {
        [Required(ErrorMessage = "The list of classes is required (it can be empty).")] public List<OfferedClassRequest>? Classes { get; set; }
    }

    public class OfferedClassRequest
    {
        [Required(ErrorMessage = "Class is required.")] public long? ClassId { get; set; }
        public List<OfferedSectionRequest>? Sections { get; set; }
    }

    public class OfferedSectionRequest
    {
        [Required(ErrorMessage = "Section is required.")] public long? SectionId { get; set; }
        /// <summary>This year's capacity for the section; omit to use the section master's.</summary>
        [Range(1, 32767, ErrorMessage = "Capacity must be between 1 and 32767.")] public int? MaxStrength { get; set; }
    }
}
