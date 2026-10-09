using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // No school_id or status: the first comes from the token, the second from toggle-status.
    public abstract class EmployeeDepartmentRequestBase : IValidatableObject
    {
        [Required(ErrorMessage = "Department name is required."), StringLength(100, ErrorMessage = "Department name can be at most 100 characters.")]
        public string DepartmentName { get; set; } = null!;

        [StringLength(250, ErrorMessage = "Description can be at most 250 characters.")]
        public string? Description { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (DepartmentName != null && string.IsNullOrWhiteSpace(DepartmentName))
                yield return new ValidationResult("Department name is required.", new[] { nameof(DepartmentName) });
        }
    }

    public class CreateEmployeeDepartmentRequest : EmployeeDepartmentRequestBase { }
    public class UpdateEmployeeDepartmentRequest : EmployeeDepartmentRequestBase { }

    public class EmployeeDepartmentResponse
    {
        public long DepartmentId { get; set; }
        public string SchoolId { get; set; } = null!;
        public string DepartmentName { get; set; } = null!;
        public string? Description { get; set; }
        public int? StatusId { get; set; }
        public string? StatusName { get; set; }   // "Active" / "Inactive"
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
