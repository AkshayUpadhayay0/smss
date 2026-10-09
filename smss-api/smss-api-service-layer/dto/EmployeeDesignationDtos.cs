using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // No school_id or status: the first comes from the token, the second from toggle-status.
    public abstract class EmployeeDesignationRequestBase : IValidatableObject
    {
        [Required(ErrorMessage = "Designation name is required."), StringLength(100, ErrorMessage = "Designation name can be at most 100 characters.")]
        public string DesignationName { get; set; } = null!;

        [StringLength(250, ErrorMessage = "Description can be at most 250 characters.")]
        public string? Description { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (DesignationName != null && string.IsNullOrWhiteSpace(DesignationName))
                yield return new ValidationResult("Designation name is required.", new[] { nameof(DesignationName) });
        }
    }

    public class CreateEmployeeDesignationRequest : EmployeeDesignationRequestBase { }
    public class UpdateEmployeeDesignationRequest : EmployeeDesignationRequestBase { }

    public class EmployeeDesignationResponse
    {
        public long DesignationId { get; set; }
        public string SchoolId { get; set; } = null!;
        public string DesignationName { get; set; } = null!;
        public string? Description { get; set; }
        public int? StatusId { get; set; }
        public string? StatusName { get; set; }   // "Active" / "Inactive"
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
