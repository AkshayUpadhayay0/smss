namespace smss_api_db_layer.@interface
{
    /// <summary>One recent fee payment: who paid, how much, how and when.</summary>
    public record AccountantPaymentRow(long PaymentId, string ReceiptNumber, string StudentId, string StudentName, decimal Amount, string PaymentMode, DateTime PaymentDate);

    /// <summary>The academic year that contains a date.</summary>
    public record AccountantYearRow(long AcademicYearId, string YearName);

    // Used by AccountantDashboardService only, and read-only. Holds the two things no existing service gives the Accountant dashboard: the money received in a time range across the
    // whole school, and the school's latest payments (the fee-payment service reads one student at a time). No new table: tb_fee_payments and tb_students.
    public interface IAccountantDashboardRepository
    {
        /// <summary>The sum of ACTIVE payments (voided ones excluded) whose payment time is in [fromUtc, toExclusiveUtc).</summary>
        Task<decimal> GetCollectedBetweenAsync(string schoolId, DateTime fromUtc, DateTime toExclusiveUtc, CancellationToken ct);

        /// <summary>The academic year whose dates contain <paramref name="date"/> (the same rule Attendance and the Fee dashboard use), or null when none does.</summary>
        Task<AccountantYearRow?> GetYearContainingAsync(string schoolId, DateOnly date, CancellationToken ct);

        /// <summary>The latest ACTIVE payments of the school, newest first.</summary>
        Task<List<AccountantPaymentRow>> GetRecentPaymentsAsync(string schoolId, int take, CancellationToken ct);
    }
}
