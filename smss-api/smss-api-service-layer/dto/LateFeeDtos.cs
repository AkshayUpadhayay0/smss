using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    public class LateFeeTypeDto
    {
        public long FeeTypeId { get; set; }
        public string Name { get; set; } = null!;
        public bool IsActive { get; set; }
        /// <summary>Only fee types with this on are charged a late fee (every flag is off until the School Admin turns it on).</summary>
        public bool AppliesLateFee { get; set; }
    }

    /// <summary>The school's late fee rule and which fee types it applies to. A school that never set one has the defaults: off.</summary>
    public class LateFeeRuleDto
    {
        public bool Enabled { get; set; }
        /// <summary>FLAT, PER_DAY or PERCENT (null while no rule was ever saved).</summary>
        public string? RuleType { get; set; }
        /// <summary>FLAT: the amount per late due. PER_DAY: the amount for each day beyond the grace days.</summary>
        public decimal? Amount { get; set; }
        /// <summary>PERCENT: of the amount paid against the due in the payment.</summary>
        public decimal? Percent { get; set; }
        public int GraceDays { get; set; }
        /// <summary>The most a single due can ever collect (null = no cap).</summary>
        public decimal? CapPerDue { get; set; }
        /// <summary>Whether the tax chosen at payment is also charged on the late fee.</summary>
        public bool TaxApplies { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
        public List<LateFeeTypeDto> FeeTypes { get; set; } = new();
    }

    /// <summary>The whole rule is saved at once. The school comes from the token; nothing here names one.</summary>
    public class SaveLateFeeRuleRequest
    {
        public bool Enabled { get; set; }
        [StringLength(10)] public string? RuleType { get; set; }
        [Range(0.01, 9999999999.99)] public decimal? Amount { get; set; }
        [Range(0.01, 100)] public decimal? Percent { get; set; }
        [Range(0, 365)] public int GraceDays { get; set; }
        [Range(0.01, 9999999999.99)] public decimal? CapPerDue { get; set; }
        public bool TaxApplies { get; set; }
        /// <summary>The fee types that charge a late fee: exactly these are on, every other fee type of the school is turned off.</summary>
        [MaxLength(500)] public List<long>? FeeTypeIds { get; set; }
    }

    public class LateFeeAllocationInput
    {
        [Range(1, long.MaxValue)] public long FeeDueId { get; set; }
        [Range(0.01, 9999999999.99)] public decimal Amount { get; set; }
    }

    /// <summary>The same shape the payment screen already sends (no late fee amount, ever: the server works it out).</summary>
    public class LateFeePreviewRequest
    {
        /// <summary>Convenience mode: this amount goes to the oldest dues first. Ignored when allocations are given.</summary>
        [Range(0.01, 9999999999.99)] public decimal? Amount { get; set; }
        [MaxLength(200)] public List<LateFeeAllocationInput>? Allocations { get; set; }
        /// <summary>Only to show the tax on the late fee, when the school's rule says tax applies.</summary>
        [Range(1, long.MaxValue)] public long? TaxTypeId { get; set; }
    }

    public class LateFeePreviewLineDto
    {
        public long FeeDueId { get; set; }
        public string FeeType { get; set; } = null!;
        public DateOnly DueDate { get; set; }
        public int DaysLate { get; set; }
        /// <summary>The days beyond the grace days: the ones that are charged.</summary>
        public int DaysCharged { get; set; }
        /// <summary>The amount this payment applies to the due.</summary>
        public decimal BaseAmount { get; set; }
        /// <summary>The late fee of this due as of today (capped), before taking off what was already collected.</summary>
        public decimal ComputedTotal { get; set; }
        public decimal AlreadyCollected { get; set; }
        /// <summary>What this payment would charge on this due.</summary>
        public decimal Amount { get; set; }
    }

    /// <summary>What late fee a payment like this WOULD charge. Read-only: nothing is saved and nothing is sent by the client as an amount.</summary>
    public class LateFeePreviewDto
    {
        /// <summary>The school's date in India that the days were counted to.</summary>
        public DateOnly Today { get; set; }
        public bool RuleEnabled { get; set; }
        public string? RuleType { get; set; }
        public int GraceDays { get; set; }
        public List<LateFeePreviewLineDto> Lines { get; set; } = new();
        public decimal Total { get; set; }
        /// <summary>The tax on the late fee: only when the rule says tax applies and a tax type was given.</summary>
        public string? TaxName { get; set; }
        public decimal TaxAmount { get; set; }
        public string? Note { get; set; }
    }
}
