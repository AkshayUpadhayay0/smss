using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // Student Category: a global lookup in the Board Type style.
    public class CreateStudentCategoryRequestDto
    {
        [Required, StringLength(30, MinimumLength = 2)]
        [RegularExpression(@"^[A-Za-z0-9_]+$", ErrorMessage = "Code may contain only letters, numbers and underscore.")]
        public string CategoryCode { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 2)]
        public string CategoryName { get; set; } = string.Empty;

        [StringLength(250)]
        public string? Description { get; set; }
    }

    // CategoryCode is deliberately absent: the code is immutable after creation.
    public class UpdateStudentCategoryRequestDto
    {
        [Required, StringLength(100, MinimumLength = 2)]
        public string CategoryName { get; set; } = string.Empty;

        [StringLength(250)]
        public string? Description { get; set; }
    }
}
