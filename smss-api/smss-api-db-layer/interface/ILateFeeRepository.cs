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

    /// <summary>One due's late fee as charged by one payment (what is saved in tb_fee_payment_late_fees).</summary>
    public class LateFeeRowWrite
    {
        public long FeeDueId { get; set; }
        public decimal Amount { get; set; }
        public int DaysLate { get; set; }
        public int DaysCharged { get; set; }
        public decimal BaseAmount { get; set; }
        public decimal ComputedTotal { get; set; }
        public decimal AlreadyCollected { get; set; }
    }

    /// <summary>A saved late fee row with what a receipt line needs.</summary>
    public class PaymentLateFeeLineRow
    {
        public long PaymentId { get; set; }
        public long FeeDueId { get; set; }
        public string FeeTypeName { get; set; } = null!;
        public DateOnly DueDate { get; set; }
        public int DaysLate { get; set; }
        public int DaysCharged { get; set; }
        public decimal BaseAmount { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>The late fee rule, the fee type flags, what the late fee preview reads and what a payment writes. Every method takes the school id.</summary>
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
        /// <summary>Writes one row per late due for a payment (inside the payment's transaction, after the payment row exists), with the rule that was in force.</summary>
        Task InsertPaymentLateFeesAsync(string schoolId, long paymentId, string ruleType, decimal? ruleAmount, decimal? rulePercent, int graceDays, decimal? capPerDue, IReadOnlyList<LateFeeRowWrite> rows, CancellationToken ct);
        /// <summary>The late fee rows of these payments, for receipts and the history.</summary>
        Task<List<PaymentLateFeeLineRow>> GetPaymentLateFeeLinesAsync(string schoolId, IReadOnlyCollection<long> paymentIds, CancellationToken ct);
    }
}
