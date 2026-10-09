using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // Religion/Caste Category, Blood Group, Gender, Document Type: global lookups in the Board Type style.
    public class CreateReligionCategoryRequestDto
    {
        [Required, StringLength(30, MinimumLength = 2)]
        [RegularExpression(@"^[A-Za-z0-9_]+$", ErrorMessage = "Code may contain only letters, numbers and underscore.")]
        public string ReligionCode { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 2)]
        public string ReligionName { get; set; } = string.Empty;

        [StringLength(250)]
        public string? Description { get; set; }
    }

    // ReligionCode is deliberately absent: the code is immutable after creation.
    public class UpdateReligionCategoryRequestDto
    {
        [Required, StringLength(100, MinimumLength = 2)]
        public string ReligionName { get; set; } = string.Empty;

        [StringLength(250)]
        public string? Description { get; set; }
    }

    public class CreateBloodGroupRequestDto
    {
        [Required, StringLength(10, MinimumLength = 2)]
        [RegularExpression(@"^[A-Za-z0-9_]+$", ErrorMessage = "Code may contain only letters, numbers and underscore.")]
        public string BloodGroupCode { get; set; } = string.Empty;

        [Required, StringLength(10, MinimumLength = 2)]
        public string BloodGroupName { get; set; } = string.Empty;

        [StringLength(250)]
        public string? Description { get; set; }
    }

    // BloodGroupCode is deliberately absent: the code is immutable after creation.
    public class UpdateBloodGroupRequestDto
    {
        [Required, StringLength(10, MinimumLength = 2)]
        public string BloodGroupName { get; set; } = string.Empty;

        [StringLength(250)]
        public string? Description { get; set; }
    }

    public class CreateGenderRequestDto
    {
        [Required, StringLength(20, MinimumLength = 2)]
        [RegularExpression(@"^[A-Za-z0-9_]+$", ErrorMessage = "Code may contain only letters, numbers and underscore.")]
        public string GenderCode { get; set; } = string.Empty;

        [Required, StringLength(50, MinimumLength = 2)]
        public string GenderName { get; set; } = string.Empty;

        [StringLength(250)]
        public string? Description { get; set; }
    }

    // GenderCode is deliberately absent: the code is immutable after creation.
    public class UpdateGenderRequestDto
    {
        [Required, StringLength(50, MinimumLength = 2)]
        public string GenderName { get; set; } = string.Empty;

        [StringLength(250)]
        public string? Description { get; set; }
    }

    public class CreateDocumentTypeRequestDto
    {
        [Required, StringLength(30, MinimumLength = 2)]
        [RegularExpression(@"^[A-Za-z0-9_]+$", ErrorMessage = "Code may contain only letters, numbers and underscore.")]
        public string DocumentTypeCode { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 2)]
        public string DocumentTypeName { get; set; } = string.Empty;

        [StringLength(250)]
        public string? Description { get; set; }
    }

    // DocumentTypeCode is deliberately absent: the code is immutable after creation.
    public class UpdateDocumentTypeRequestDto
    {
        [Required, StringLength(100, MinimumLength = 2)]
        public string DocumentTypeName { get; set; } = string.Empty;

        [StringLength(250)]
        public string? Description { get; set; }
    }
}
