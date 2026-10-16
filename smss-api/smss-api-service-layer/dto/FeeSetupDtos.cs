using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // None of these carry a school id: it always comes from the token. Active/inactive is only changed via toggle-status.

    // ---------------- fee types ----------------
    public abstract class FeeTypeRequestBase : IValidatableObject
    {
        [Required(ErrorMessage = "Fee type name is required."), StringLength(100, ErrorMessage = "Fee type name can be at most 100 characters.")]
        public string FeeTypeName { get; set; } = null!;

        [Required(ErrorMessage = "Fee type code is required."), StringLength(30, ErrorMessage = "Fee type code can be at most 30 characters.")]
        [RegularExpression("^[A-Za-z0-9_-]+$", ErrorMessage = "Fee type code can contain only letters, numbers, hyphen and underscore.")]
        public string FeeTypeCode { get; set; } = null!;

        public string? Description { get; set; }

        /// <summary>Optional fees (e.g. Transport) are opted into per student at admission; non-optional ones are always applied.</summary>
        public bool IsOptional { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (FeeTypeName != null && string.IsNullOrWhiteSpace(FeeTypeName))
                yield return new ValidationResult("Fee type name is required.", new[] { nameof(FeeTypeName) });
        }
    }

    public class CreateFeeTypeRequest : FeeTypeRequestBase { }
    public class UpdateFeeTypeRequest : FeeTypeRequestBase { }

    public class FeeTypeResponse
    {
        public long FeeTypeId { get; set; }
        public string SchoolId { get; set; } = null!;
        public string FeeTypeName { get; set; } = null!;
        public string FeeTypeCode { get; set; } = null!;
        public string? Description { get; set; }
        public bool IsOptional { get; set; }
        public bool IsActive { get; set; }
        public string StatusName { get; set; } = null!;   // "Active" / "Inactive"
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    // ---------------- fee frequencies ----------------
    public class FeeFrequencyResponse
    {
        public long FrequencyId { get; set; }
        public string FrequencyName { get; set; } = null!;
        public string FrequencyCode { get; set; } = null!;
    }

    // ---------------- fee structures ----------------
    public class FeeStructureLineRequest
    {
        [Required(ErrorMessage = "Fee type is required."), Range(1, long.MaxValue, ErrorMessage = "Fee type is required.")]
        public long? FeeTypeId { get; set; }

        [Required(ErrorMessage = "Frequency is required."), Range(1, long.MaxValue, ErrorMessage = "Frequency is required.")]
        public long? FrequencyId { get; set; }

        [Required(ErrorMessage = "Amount is required.")]
        [Range(typeof(decimal), "0", "9999999999.99", ErrorMessage = "Amount must be between 0 and 9,999,999,999.99.")]
        public decimal? Amount { get; set; }

        /// <summary>Day of the month (1-31) a recurring (Monthly / Quarterly / Half-Yearly) fee head falls due. Required for those, ignored otherwise.</summary>
        [Range(1, 31, ErrorMessage = "Due day must be between 1 and 31.")]
        public int? DueDay { get; set; }
    }

    public abstract class FeeStructureRequestBase
    {
        [Required(ErrorMessage = "Structure name is required."), StringLength(150, ErrorMessage = "Structure name can be at most 150 characters.")]
        public string StructureName { get; set; } = null!;

        [Required(ErrorMessage = "Add at least one fee line."), MinLength(1, ErrorMessage = "Add at least one fee line."), MaxLength(100)]
        public List<FeeStructureLineRequest> Details { get; set; } = new();
    }

    public class CreateFeeStructureRequest : FeeStructureRequestBase
    {
        [Required(ErrorMessage = "Academic year is required."), Range(1, long.MaxValue, ErrorMessage = "Academic year is required.")]
        public long? AcademicYearId { get; set; }

        [Required(ErrorMessage = "Class is required."), Range(1, long.MaxValue, ErrorMessage = "Class is required.")]
        public long? ClassId { get; set; }
    }

    /// <summary>Academic year and class are fixed once a structure exists (it is "the fee structure of THAT class and year").</summary>
    public class UpdateFeeStructureRequest : FeeStructureRequestBase { }

    public class FeeStructureLineResponse
    {
        public long FeeStructureDetailId { get; set; }
        public long FeeTypeId { get; set; }
        public string FeeTypeName { get; set; } = null!;
        public string FeeTypeCode { get; set; } = null!;
        public long FrequencyId { get; set; }
        public string FrequencyName { get; set; } = null!;
        public decimal Amount { get; set; }
        public int? DueDay { get; set; }
    }

    /// <summary>List row: the header plus how many lines it has.</summary>
    public class FeeStructureSummaryResponse
    {
        public long FeeStructureId { get; set; }
        public string SchoolId { get; set; } = null!;
        public long AcademicYearId { get; set; }
        public string AcademicYearName { get; set; } = null!;
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public string StructureName { get; set; } = null!;
        public bool IsActive { get; set; }
        public string StatusName { get; set; } = null!;
        public int LineCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>The header together with its lines (fee type and frequency names joined), for the structure builder.</summary>
    public class FeeStructureResponse : FeeStructureSummaryResponse
    {
        public List<FeeStructureLineResponse> Details { get; set; } = new();
    }

    // ---------------- fee concessions ----------------
    public abstract class FeeConcessionRequestBase : IValidatableObject
    {
        [Required(ErrorMessage = "Concession name is required."), StringLength(100, ErrorMessage = "Concession name can be at most 100 characters.")]
        public string ConcessionName { get; set; } = null!;

        [Required(ErrorMessage = "Concession code is required."), StringLength(30, ErrorMessage = "Concession code can be at most 30 characters.")]
        [RegularExpression("^[A-Za-z0-9_-]+$", ErrorMessage = "Concession code can contain only letters, numbers, hyphen and underscore.")]
        public string ConcessionCode { get; set; } = null!;

        [Required(ErrorMessage = "Concession type is required.")]
        [RegularExpression("^(PERCENTAGE|FIXED)$", ErrorMessage = "Concession type must be PERCENTAGE or FIXED.")]
        public string ConcessionType { get; set; } = null!;

        [Required(ErrorMessage = "Value is required.")]
        [Range(typeof(decimal), "0", "99999999.99", ErrorMessage = "Value must be between 0 and 99,999,999.99.")]
        public decimal? Value { get; set; }

        /// <summary>Null = applies to the whole structure rather than one fee head.</summary>
        public long? ApplicableFeeTypeId { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (ConcessionName != null && string.IsNullOrWhiteSpace(ConcessionName))
                yield return new ValidationResult("Concession name is required.", new[] { nameof(ConcessionName) });
            if (ConcessionType == "PERCENTAGE" && Value is > 100)
                yield return new ValidationResult("A percentage concession cannot be more than 100.", new[] { nameof(Value) });
            if (Value is <= 0)
                yield return new ValidationResult("Value must be greater than 0.", new[] { nameof(Value) });
        }
    }

    public class CreateFeeConcessionRequest : FeeConcessionRequestBase { }
    public class UpdateFeeConcessionRequest : FeeConcessionRequestBase { }

    public class FeeConcessionResponse
    {
        public long ConcessionId { get; set; }
        public string SchoolId { get; set; } = null!;
        public string ConcessionName { get; set; } = null!;
        public string ConcessionCode { get; set; } = null!;
        public string ConcessionType { get; set; } = null!;
        public decimal Value { get; set; }
        public long? ApplicableFeeTypeId { get; set; }
        public string? ApplicableFeeTypeName { get; set; }
        public bool IsActive { get; set; }
        public string StatusName { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
