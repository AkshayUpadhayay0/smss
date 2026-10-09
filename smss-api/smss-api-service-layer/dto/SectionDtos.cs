using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // No school_id or status: the first comes from the token, the second from toggle-status.
    public abstract class SectionRequestBase : IValidatableObject
    {
        [Required(ErrorMessage = "Class is required.")]
        [Range(1, long.MaxValue, ErrorMessage = "Class is required.")]
        public long? ClassId { get; set; }

        [Required(ErrorMessage = "Section name is required."), StringLength(20, ErrorMessage = "Section name can be at most 20 characters.")]
        public string SectionName { get; set; } = null!;

        // smallint in the database; optional
        [Range(1, short.MaxValue, ErrorMessage = "Max strength must be a positive whole number (1 to 32767).")]
        public int? MaxStrength { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (SectionName != null && string.IsNullOrWhiteSpace(SectionName))
                yield return new ValidationResult("Section name is required.", new[] { nameof(SectionName) });
        }
    }

    public class CreateSectionRequest : SectionRequestBase { }
    public class UpdateSectionRequest : SectionRequestBase { }

    public class SectionResponse
    {
        public long SectionId { get; set; }
        public string SchoolId { get; set; } = null!;
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;          // joined, so lists need no second lookup
        public int ClassSequenceOrder { get; set; }             // lets lists group sections in class order
        public string SectionName { get; set; } = null!;
        public int? MaxStrength { get; set; }
        public int? StatusId { get; set; }
        public string? StatusName { get; set; }                 // "Active" / "Inactive"
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
