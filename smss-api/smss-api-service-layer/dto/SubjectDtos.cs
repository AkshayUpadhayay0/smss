using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // No school_id or status: the first comes from the token, the second from toggle-status.
    public abstract class SubjectRequestBase : IValidatableObject
    {
        [Required(ErrorMessage = "Subject name is required."), StringLength(100, ErrorMessage = "Subject name can be at most 100 characters.")]
        public string SubjectName { get; set; } = null!;

        [StringLength(20, ErrorMessage = "Subject code can be at most 20 characters.")]
        public string? SubjectCode { get; set; }

        [StringLength(250, ErrorMessage = "Description can be at most 250 characters.")]
        public string? Description { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (SubjectName != null && string.IsNullOrWhiteSpace(SubjectName))
                yield return new ValidationResult("Subject name is required.", new[] { nameof(SubjectName) });
        }
    }

    public class CreateSubjectRequest : SubjectRequestBase { }
    public class UpdateSubjectRequest : SubjectRequestBase { }

    public class SubjectResponse
    {
        public long SubjectId { get; set; }
        public string SchoolId { get; set; } = null!;
        public string SubjectName { get; set; } = null!;
        public string? SubjectCode { get; set; }
        public string? Description { get; set; }
        public int? StatusId { get; set; }
        public string? StatusName { get; set; }   // "Active" / "Inactive"
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
