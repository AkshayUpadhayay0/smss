using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // No school_id, status or is_current: the first comes from the token, the others from actions
    // (toggle-status, set-current).
    public abstract class AcademicYearRequestBase : IValidatableObject
    {
        [Required(ErrorMessage = "Year name is required."), StringLength(20, ErrorMessage = "Year name can be at most 20 characters.")]
        public string YearName { get; set; } = null!;

        [Required(ErrorMessage = "Start date is required.")] public DateOnly? StartDate { get; set; }
        [Required(ErrorMessage = "End date is required.")] public DateOnly? EndDate { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (YearName != null && string.IsNullOrWhiteSpace(YearName))
                yield return new ValidationResult("Year name is required.", new[] { nameof(YearName) });
            if (StartDate.HasValue && EndDate.HasValue && EndDate <= StartDate)
                yield return new ValidationResult("End date must be after start date.", new[] { nameof(EndDate) });
        }
    }

    public class CreateAcademicYearRequest : AcademicYearRequestBase { }
    public class UpdateAcademicYearRequest : AcademicYearRequestBase { }

    public class AcademicYearResponse
    {
        public long AcademicYearId { get; set; }
        public string SchoolId { get; set; } = null!;
        public string YearName { get; set; } = null!;
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public bool IsCurrent { get; set; }
        public int? StatusId { get; set; }
        public string? StatusName { get; set; }   // "Active" / "Inactive", so clients never need to know sids
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
