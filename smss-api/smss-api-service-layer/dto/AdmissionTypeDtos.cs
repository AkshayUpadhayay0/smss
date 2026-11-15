using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // Admission Type: a global lookup managed by the Super Admin, in the Student Category style.
    public class CreateAdmissionTypeRequestDto
    {
        [Required, StringLength(30, MinimumLength = 2)]
        [RegularExpression(@"^[A-Za-z0-9_]+$", ErrorMessage = "Code may contain only letters, numbers and underscore.")]
        public string TypeCode { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 2)]
        public string TypeName { get; set; } = string.Empty;

        [StringLength(250)]
        public string? Description { get; set; }
    }

    // TypeCode is deliberately absent: the code is immutable after creation.
    public class UpdateAdmissionTypeRequestDto
    {
        [Required, StringLength(100, MinimumLength = 2)]
        public string TypeName { get; set; } = string.Empty;

        [StringLength(250)]
        public string? Description { get; set; }
    }
}
