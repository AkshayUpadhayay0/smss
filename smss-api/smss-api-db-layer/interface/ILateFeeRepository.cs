namespace smss_api_db_layer.@interface
{
    public class LateFeeRuleRow
    {
        public bool IsEnabled { get; set; }
        public string? RuleType { get; set; }
        public decimal? Amount { get; set; }
        public decimal? Percent { get; set; }
        public int GraceDays { get; set; }
        public decimal? CapPerDue { get; set; }
        public bool TaxApplies { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedByName { get; set; }
    }

    public class LateFeeTypeRow
    {
        public long FeeTypeId { get; set; }
        public string Name { get; set; } = null!;
        public bool IsActive { get; set; }
        public bool AppliesLateFee { get; set; }
    }

    /// <summary>A student's due with what the late fee needs to know about its fee type.</summary>
    public class LateFeeDueRow
    {
        public long FeeDueId { get; set; }
        public DateOnly DueDate { get; set; }
        public decimal AmountDue { get; set; }
        public decimal AmountPaid { get; set; }
        public string FeeTypeName { get; set; } = null!;
        public bool AppliesLateFee { get; set; }
    }

    public class LateFeeCollectedRow
    {
        public long FeeDueId { get; set; }
        public decimal Amount { get; set; }
    }

    public class LateFeeTaxRow
    {
        public string Name { get; set; } = null!;
        public decimal RatePercent { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>The late fee rule, the fee type flags and what the late fee preview reads. Every method takes the school id.</summary>
    public interface ILateFeeRepository
    {
        Task<LateFeeRuleRow?> GetRuleAsync(string schoolId, CancellationToken ct);
        Task SaveRuleAsync(string schoolId, long userId, bool enabled, string? ruleType, decimal? amount, decimal? percent, int graceDays, decimal? capPerDue, bool taxApplies, CancellationToken ct);
        Task<List<LateFeeTypeRow>> ListFeeTypesAsync(string schoolId, CancellationToken ct);
        /// <summary>Sets the flag of EVERY fee type of the school: on for the ids listed, off for the rest. Returns how many of the ids belong to the school.</summary>
        Task<int> SetFlagsAsync(string schoolId, IReadOnlyCollection<long> flaggedIds, CancellationToken ct);
        Task<bool> StudentExistsAsync(string schoolId, string studentId, CancellationToken ct);
        /// <summary>The student's dues that still have something to pay, oldest first (no lock: the preview only reads).</summary>
        Task<List<LateFeeDueRow>> ListOutstandingDuesAsync(string schoolId, string studentId, CancellationToken ct);
        Task<List<LateFeeDueRow>> GetDuesAsync(string schoolId, string studentId, IReadOnlyCollection<long> dueIds, CancellationToken ct);
        /// <summary>Late fee already collected per due by NON-VOIDED payments (a voided payment frees its amounts again).</summary>
        Task<List<LateFeeCollectedRow>> GetCollectedAsync(string schoolId, IReadOnlyCollection<long> dueIds, CancellationToken ct);
        Task<LateFeeTaxRow?> GetTaxTypeAsync(string schoolId, long taxTypeId, CancellationToken ct);
    }
}
