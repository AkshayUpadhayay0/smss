namespace smss_api_db_layer.@interface
{
    /// <summary>The money of the non-voided payments in a time range, split the way a receipt is: fee, late fee, tax, and the total (= fee + late fee + tax). The tax includes the tax on the late fee.</summary>
    public class LateFeeReceivedRow
    {
        public decimal FeeAmount { get; set; }
        public decimal LateFeeAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }
    }

    /// <summary>Late fee and tax of one calendar month (India time) inside the scope of a year, class or section.</summary>
    public class LateFeeMonthRow
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal LateFee { get; set; }
        public decimal Tax { get; set; }
    }

    public class LateFeeReportSummaryRow
    {
        public int ChargedCount { get; set; }
        public decimal ChargedAmount { get; set; }
        public int WaivedCount { get; set; }
        public decimal WaivedAmount { get; set; }
    }

    /// <summary>One late fee charged by one payment on one due.</summary>
    public class LateFeeChargedRow
    {
        public long PaymentId { get; set; }
        public string ReceiptNumber { get; set; } = null!;
        public DateTime PaymentDate { get; set; }
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public string? ClassName { get; set; }
        public string FeeType { get; set; } = null!;
        public DateOnly DueDate { get; set; }
        public int DaysLate { get; set; }
        public int DaysCharged { get; set; }
        public decimal BaseAmount { get; set; }
        public decimal Amount { get; set; }
        public int TotalRows { get; set; }
    }

    /// <summary>A late fee a payment would have charged and the cashier waived.</summary>
    public class LateFeeWaivedRow
    {
        public long PaymentId { get; set; }
        public string ReceiptNumber { get; set; } = null!;
        public DateTime PaymentDate { get; set; }
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public string? ClassName { get; set; }
        public decimal WouldHaveBeen { get; set; }
        public string? WaivedBy { get; set; }
        public string? Note { get; set; }
        public int TotalRows { get; set; }
    }

    /// <summary>Reads for the late fee figures and the Late fee report. Read-only; every method takes the school id and leaves out VOIDED payments.</summary>
    public interface ILateFeeReportRepository
    {
        Task<LateFeeReceivedRow> GetReceivedBetweenAsync(string schoolId, DateTime fromUtc, DateTime toExclusiveUtc, CancellationToken ct);
        /// <summary>Late fee collected on dues of the academic year in a payment-time range (no range = the whole year). Excludes the tax on it.</summary>
        Task<decimal> GetLateFeeCollectedAsync(string schoolId, long academicYearId, DateTime? fromUtc, DateTime? toExclusiveUtc, CancellationToken ct);
        Task<List<LateFeeMonthRow>> GetByMonthAsync(string schoolId, long academicYearId, long? classId, long? sectionId, CancellationToken ct);
        Task<bool> ClassExistsAsync(string schoolId, long classId, CancellationToken ct);
        Task<LateFeeReportSummaryRow> GetSummaryAsync(string schoolId, DateTime fromUtc, DateTime toExclusiveUtc, long? classId, CancellationToken ct);
        Task<List<LateFeeChargedRow>> ListChargedAsync(string schoolId, DateTime fromUtc, DateTime toExclusiveUtc, long? classId, int offset, int limit, CancellationToken ct);
        Task<List<LateFeeWaivedRow>> ListWaivedAsync(string schoolId, DateTime fromUtc, DateTime toExclusiveUtc, long? classId, int offset, int limit, CancellationToken ct);
    }
}
