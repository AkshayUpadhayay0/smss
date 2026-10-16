using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // None of these carry a school id: it always comes from the token.

    /// <summary>
    /// A fee head choice made while admitting a student: the frequency the student will pay it at and whether it applies
    /// to them. Anything left out (or not valid for that fee head) falls back to the fee structure's default.
    /// </summary>
    public class FeeItemSelection
    {
        [Range(1, long.MaxValue)] public long FeeTypeId { get; set; }
        public long? FrequencyId { get; set; }
        public bool? IsApplicable { get; set; }
    }

    public class UpdateFeeItemRequest
    {
        /// <summary>A frequency allowed for this fee head (see <c>allowedFrequencies</c> on the item).</summary>
        public long? FrequencyId { get; set; }
        public bool? IsApplicable { get; set; }
    }

    public class FeeFrequencyOptionDto
    {
        public long FrequencyId { get; set; }
        public string FrequencyName { get; set; } = null!;
        public string FrequencyCode { get; set; } = null!;
        /// <summary>How many installments a full academic year has at this frequency.</summary>
        public int Installments { get; set; }
        /// <summary>Preview only: how many of them fall on or after the student's start month (a mid-year admission pays fewer).</summary>
        public int? RemainingInstallments { get; set; }
    }

    /// <summary>One fee head of a student for the year, with its schedule summary.</summary>
    public class StudentFeeItemDto
    {
        public long StudentFeeItemId { get; set; }
        public long FeeTypeId { get; set; }
        public string FeeTypeName { get; set; } = null!;
        public string FeeTypeCode { get; set; } = null!;

        /// <summary>The frequency THIS student pays it at.</summary>
        public long FrequencyId { get; set; }
        public string FrequencyName { get; set; } = null!;
        /// <summary>The frequency the fee structure charges it at (the amounts below are per period of this).</summary>
        public string StructureFrequencyName { get; set; } = null!;
        public List<FeeFrequencyOptionDto> AllowedFrequencies { get; set; } = new();
        public int? DueDay { get; set; }

        public decimal BaseAmount { get; set; }
        public decimal ConcessionAmount { get; set; }
        public decimal NetAmount { get; set; }
        public bool IsOptional { get; set; }
        /// <summary>Net for the whole academic year: net per period x periods in a year of the structure's frequency.</summary>
        public decimal AnnualNetAmount { get; set; }

        public bool IsApplicable { get; set; }

        public int DueCount { get; set; }
        public DateOnly? NextDueDate { get; set; }
        public decimal TotalDue { get; set; }
        public decimal TotalPaid { get; set; }
    }

    public class StudentFeeItemsDto
    {
        public string StudentId { get; set; } = null!;
        public long? AcademicYearId { get; set; }
        public string? AcademicYearName { get; set; }
        public List<StudentFeeItemDto> Items { get; set; } = new();
        /// <summary>Year total of the applicable items (this is a real total: every item is already for the whole year).</summary>
        public decimal AnnualNetTotal { get; set; }
    }

    public class StudentFeeDueDto
    {
        public long FeeDueId { get; set; }
        public long StudentFeeItemId { get; set; }
        public string FeeTypeName { get; set; } = null!;
        public string FrequencyName { get; set; } = null!;
        public DateOnly DueDate { get; set; }
        public decimal AmountDue { get; set; }
        public decimal AmountPaid { get; set; }
        public decimal Outstanding { get; set; }
        /// <summary>Paid / Partially Paid / Pending / Overdue. Computed on every read, never stored.</summary>
        public string Status { get; set; } = null!;
    }

    public class StudentFeeDuesDto
    {
        public string StudentId { get; set; } = null!;
        public long? AcademicYearId { get; set; }
        public string? AcademicYearName { get; set; }
        public decimal TotalDue { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal Outstanding { get; set; }
        public decimal OverdueAmount { get; set; }
        public int OverdueCount { get; set; }
        public List<StudentFeeDueDto> Dues { get; set; } = new();
    }

    /// <summary>One fee head of the structure that applies to a class, for the admission form (nothing is created yet).</summary>
    public class FeePreviewLineDto
    {
        public long FeeTypeId { get; set; }
        public string FeeTypeName { get; set; } = null!;
        public string FeeTypeCode { get; set; } = null!;
        /// <summary>True: the admission form offers an include / exclude checkbox. False: always applied, no toggle.</summary>
        public bool IsOptional { get; set; }
        /// <summary>Per period of the structure's frequency.</summary>
        public decimal Amount { get; set; }
        public long StructureFrequencyId { get; set; }
        public string StructureFrequencyName { get; set; } = null!;
        public int PeriodsPerYear { get; set; }
        public int? DueDay { get; set; }
        public List<FeeFrequencyOptionDto> AllowedFrequencies { get; set; } = new();
    }

    public class FeePreviewDto
    {
        public long AcademicYearId { get; set; }
        public long ClassId { get; set; }
        /// <summary>False is a normal state: there is no fee structure for the class + year, and Lines is empty.</summary>
        public bool HasFeeStructure { get; set; }
        public long? FeeStructureId { get; set; }
        public string? StructureName { get; set; }
        public List<FeePreviewLineDto> Lines { get; set; } = new();
    }
}
