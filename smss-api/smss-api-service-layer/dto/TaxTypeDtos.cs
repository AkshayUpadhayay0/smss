using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // No school id anywhere: the school always comes from the token.
    public class TaxTypeRequest
    {
        [Required(ErrorMessage = "Tax name is required."), StringLength(100, ErrorMessage = "Tax name can be at most 100 characters.")]
        public string TaxName { get; set; } = null!;

        [Required(ErrorMessage = "Tax code is required."), StringLength(30, ErrorMessage = "Tax code can be at most 30 characters.")]
        [RegularExpression("^[A-Za-z0-9_-]+$", ErrorMessage = "Tax code can contain only letters, numbers, hyphen and underscore.")]
        public string TaxCode { get; set; } = null!;

        [Required(ErrorMessage = "Rate is required.")]
        [Range(typeof(decimal), "0", "100", ErrorMessage = "Rate must be between 0 and 100.")]
        public decimal? RatePercent { get; set; }

        [StringLength(500, ErrorMessage = "Description can be at most 500 characters.")]
        public string? Description { get; set; }
    }

    public class TaxTypeDto
    {
        public long TaxTypeId { get; set; }
        public string TaxName { get; set; } = null!;
        public string TaxCode { get; set; } = null!;
        public decimal RatePercent { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
