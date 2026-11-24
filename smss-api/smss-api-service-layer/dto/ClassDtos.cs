using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // No school_id or status: the first comes from the token, the second from toggle-status.
    public abstract class ClassRequestBase : IValidatableObject
    {
        [Required(ErrorMessage = "Class name is required."), StringLength(50, ErrorMessage = "Class name can be at most 50 characters.")]
        public string ClassName { get; set; } = null!;

        [StringLength(20, ErrorMessage = "Class code can be at most 20 characters.")]
        public string? ClassCode { get; set; }

        // smallint in the database
        [Required(ErrorMessage = "Sequence order is required.")]
        [Range(1, short.MaxValue, ErrorMessage = "Sequence order must be a positive whole number (1 to 32767).")]
        public int? SequenceOrder { get; set; }

        private long? _nextClassId;
        /// <summary>
        /// The class students normally move to; null = the last class (its students pass out). On an UPDATE, leaving the property out of the request keeps the stored value
        /// and sending an explicit null clears it (see <see cref="NextClassIdSpecified"/>).
        /// </summary>
        public long? NextClassId { get => _nextClassId; set { _nextClassId = value; NextClassIdSpecified = true; } }
        /// <summary>True when the request carried the property at all (the JSON deserializer calls the setter only for a property that is present, null included).</summary>
        [System.Text.Json.Serialization.JsonIgnore] public bool NextClassIdSpecified { get; private set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (ClassName != null && string.IsNullOrWhiteSpace(ClassName))
                yield return new ValidationResult("Class name is required.", new[] { nameof(ClassName) });
        }
    }

    public class CreateClassRequest : ClassRequestBase { }
    public class UpdateClassRequest : ClassRequestBase { }

    public class ClassResponse
    {
        public long ClassId { get; set; }
        public string SchoolId { get; set; } = null!;
        public string ClassName { get; set; } = null!;
        public string? ClassCode { get; set; }
        public int SequenceOrder { get; set; }
        public long? NextClassId { get; set; }
        public string? NextClassName { get; set; }
        public int? StatusId { get; set; }
        public string? StatusName { get; set; }   // "Active" / "Inactive"
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
