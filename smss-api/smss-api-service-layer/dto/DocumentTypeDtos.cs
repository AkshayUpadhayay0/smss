using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    public class CreateDocumentTypeRequestDto
    {
        [Required, MaxLength(50)]
        public string DocumentName { get; set; } = null!;

        [Required, MaxLength(20)]
        public string DocumentCode { get; set; } = null!;

        [Required]
        [RegularExpression("^(STUDENT|EMPLOYEE|BOTH)$", ErrorMessage = "AppliesTo must be STUDENT, EMPLOYEE, or BOTH.")]
        public string AppliesTo { get; set; } = null!;

        public bool IsRequired { get; set; } = false;
    }

    public class UpdateDocumentTypeRequestDto : CreateDocumentTypeRequestDto
    {
        [Required]
        public long DocumentTypeId { get; set; }
    }

    public class DocumentTypeResponseDto
    {
        public long DocumentTypeId { get; set; }
        public string SchoolId { get; set; } = null!;
        public string DocumentName { get; set; } = null!;
        public string DocumentCode { get; set; } = null!;
        public string AppliesTo { get; set; } = null!;
        public bool IsRequired { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
