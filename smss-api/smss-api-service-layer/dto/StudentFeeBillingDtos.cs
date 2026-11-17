using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // None of these carry a school id: it always comes from the token.

    /// <summary>
    /// A fee head choice made while admitting a student: the installment plan (how each billing cycle is split) and whether the fee applies to them. A student has no frequency of
    /// their own any more: it is always the fee line's. Anything left out (or not allowed for that fee head) falls back to the default (plan NONE).
    /// </summary>
    public class FeeItemSelection
    {
        [Range(1, long.MaxValue)] public long FeeTypeId { get; set; }
        /// <summary>NONE, QUARTERLY or MONTHLY, as allowed for the line (see <c>allowedPlans</c> of the preview).</summary>
        [StringLength(12)] public string? InstallmentPlan { get; set; }
        public bool? IsApplicable { get; set; }
    }

    public class UpdateFeeItemRequest
    {
        /// <summary>An installment plan allowed for this fee head (see <c>allowedPlans</c> on the item).</summary>
        [StringLength(12)] public string? InstallmentPlan { get; set; }
        public bool? IsApplicable { get; set; }
    }

    /// <summary>One installment plan a fee line allows, with what it would mean for a student starting on the previewed date.</summary>
    public class InstallmentPlanOptionDto
    {
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
        /// <summary>How many installments each billing cycle is split into (NONE = 1).</summary>
        public int InstallmentsPerCycle { get; set; }
        /// <summary>How many installments a full academic year has under this plan.</summary>
        public int Installments { get; set; }
        /// <summary>Preview only: how many fall on or after the start (later of the line's effective date, the charge-from date and today).</summary>
        public int? RemainingInstallments { get; set; }
        public decimal? RemainingAmount { get; set; }
        /// <summary>Preview only: how many installments dated before today (from the charge-from date on) a back-dated "Charge fees from" would create, and their total.</summary>
        public int? BackdatedInstallments { get; set; }
        public decimal? BackdatedAmount { get; set; }
    }

    /// <summary>One fee head of a student for the year, with its schedule summary.</summary>
    public class StudentFeeItemDto
    {
        public long StudentFeeItemId { get; set; }
        public long FeeTypeId { get; set; }
        public string FeeTypeName { get; set; } = null!;
        public string FeeTypeCode { get; set; } = null!;

        /// <summary>The billing cycle: always the fee line's frequency (the amounts below are per cycle).</summary>
        public long FrequencyId { get; set; }
        public string FrequencyName { get; set; } = null!;
        /// <summary>Same as <see cref="FrequencyName"/> (kept for older clients).</summary>
        public string StructureFrequencyName { get; set; } = null!;
        /// <summary>NONE, QUARTERLY or MONTHLY: how each billing cycle is split into installments.</summary>
        public string InstallmentPlan { get; set; } = "NONE";
        public string InstallmentPlanName { get; set; } = "None (one payment per cycle)";
        public List<InstallmentPlanOptionDto> AllowedPlans { get; set; } = new();
        /// <summary>From when the fee line is charged.</summary>
        public DateOnly? EffectiveFrom { get; set; }
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
        /// <summary>The fee type of the installment (the Collect Payment form matches a concession's applicable fee type against it).</summary>
        public long FeeTypeId { get; set; }
        public string FeeTypeName { get; set; } = null!;
        public string FrequencyName { get; set; } = null!;
        /// <summary>NONE, QUARTERLY or MONTHLY (how the billing cycle is split).</summary>
        public string InstallmentPlan { get; set; } = "NONE";
        /// <summary>AUTO or BACKDATED (created by the explicit "apply to earlier months").</summary>
        public string Source { get; set; } = "AUTO";
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
        /// <summary>From when the line is charged.</summary>
        public DateOnly EffectiveFrom { get; set; }
        public List<InstallmentPlanOptionDto> AllowedPlans { get; set; } = new();
    }

    public class FeePreviewDto
    {
        public long AcademicYearId { get; set; }
        public long ClassId { get; set; }
        /// <summary>False is a normal state: there is no fee structure for the class + year, and Lines is empty.</summary>
        public bool HasFeeStructure { get; set; }
        public long? FeeStructureId { get; set; }
        public string? StructureName { get; set; }
        /// <summary>The date the preview was worked out from (the "charge fees from" date, or today).</summary>
        public DateOnly ChargeFrom { get; set; }
        /// <summary>True when the charge-from date is before today: a back-dated charge, which needs an explicit confirmation and a reason.</summary>
        public bool IsBackdated { get; set; }
        /// <summary>False when a back-dated charge is not possible (not the current academic year, or before its start); see <see cref="BackdatingMessage"/>.</summary>
        public bool BackdatingAllowed { get; set; } = true;
        public string? BackdatingMessage { get; set; }
        public List<FeePreviewLineDto> Lines { get; set; } = new();
    }
}
