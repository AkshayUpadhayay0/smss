using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // No school_id or status: the first comes from the token, the second from toggle-status.
    public abstract class AdmissionTypeRequestBase : IValidatableObject
    {
        [Required(ErrorMessage = "Type name is required."), StringLength(100, ErrorMessage = "Type name can be at most 100 characters.")]
        public string TypeName { get; set; } = null!;

        [StringLength(250, ErrorMessage = "Description can be at most 250 characters.")]
        public string? Description { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (TypeName != null && string.IsNullOrWhiteSpace(TypeName))
                yield return new ValidationResult("Type name is required.", new[] { nameof(TypeName) });
        }
    }

    public class CreateAdmissionTypeRequest : AdmissionTypeRequestBase { }
    public class UpdateAdmissionTypeRequest : AdmissionTypeRequestBase { }

    public class AdmissionTypeResponse
    {
        public long AdmissionTypeId { get; set; }
        public string SchoolId { get; set; } = null!;
        public string TypeName { get; set; } = null!;
        public string? Description { get; set; }
        public int? StatusId { get; set; }
        public string? StatusName { get; set; }   // "Active" / "Inactive"
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
