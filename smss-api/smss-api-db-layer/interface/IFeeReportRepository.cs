namespace smss_api_db_layer.@interface
{
    public record ReportYearRow(long AcademicYearId, string YearName, DateOnly StartDate, DateOnly EndDate);
    /// <summary>TotalPaid = what the installments count as paid (amount_paid: cash AND discounted part). Cash collected = TotalPaid - the concession given.</summary>
    public record DashboardTotals(decimal TotalDue, decimal TotalPaid, decimal OverdueAmount, int OverdueCount, decimal UpcomingAmount, int UpcomingCount);
    public record CollectionMonth(int Year, int Month, decimal Amount);
    public record TaxCollectedRow(long TaxTypeId, string TaxName, string TaxCode, decimal Amount, int PaymentCount);
    public record ConcessionGivenRow(long ConcessionId, string ConcessionName, string ConcessionCode, decimal Amount, int PaymentCount);
    /// <summary>One payment that carried a concession, with the names the audit list shows.</summary>
    public record ConcessionLogRow(long PaymentId, string ReceiptNumber, DateTime PaymentDate, string StudentId, string StudentName, long ConcessionId, string ConcessionName,
        string ConcessionCode, string ConcessionType, decimal ConcessionValue, decimal ConcessionAmount, string? Reason, string? AppliedByName, string Status);
    public record ConcessionLogPage(List<ConcessionLogRow> Rows, int TotalRows, decimal ActiveTotal);
    public record ClassStudentRow(string StudentId, string FirstName, string? MiddleName, string? LastName, string? RollNumber, string SectionName);

    // Read-only. Every method takes the school id; the installments' status rules live in FeeSchedule.Status and the SQL conditions here mirror it.
    public interface IFeeReportRepository
    {
        Task<ReportYearRow?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct);
        Task<string?> GetClassNameAsync(string schoolId, long classId, CancellationToken ct);
        Task<bool> SectionBelongsToClassAsync(string schoolId, long classId, long sectionId, CancellationToken ct);

        /// <summary>Query 1 of the dashboard: all the totals in one aggregate over the installments in scope (no rows come back per student).</summary>
        Task<DashboardTotals> GetDashboardTotalsAsync(string schoolId, long academicYearId, long? classId, long? sectionId, DateOnly today, DateOnly upcomingUntil, CancellationToken ct);
        /// <summary>Query 2 of the dashboard: Active payments' allocations to installments in scope, summed per month of the payment date (India time).</summary>
        Task<List<CollectionMonth>> GetCollectionByMonthAsync(string schoolId, long academicYearId, long? classId, long? sectionId, CancellationToken ct);
        /// <summary>
        /// Tax collected per tax type in the same scope as the dashboard: ACTIVE payments with at least one allocation to an installment of a student item in scope
        /// (a payment counts once, with its whole tax: a payment is for one student and in practice one academic year). Voided payments are excluded.
        /// </summary>
        Task<List<TaxCollectedRow>> GetTaxCollectedAsync(string schoolId, long academicYearId, long? classId, long? sectionId, CancellationToken ct);

        /// <summary>
        /// Concession given per concession, same scope as the dashboard: the per-allocation concession_amount of ACTIVE payments, on installments of student items in scope
        /// (exact per installment, so a payment spanning two years is split correctly). Voided payments are excluded.
        /// </summary>
        Task<List<ConcessionGivenRow>> GetConcessionGivenAsync(string schoolId, long academicYearId, long? classId, long? sectionId, CancellationToken ct);
        /// <summary>Concession given per student for the class report: Active payments' allocation concession_amount on that year's installments of these students.</summary>
        Task<Dictionary<string, decimal>> GetConcessionGivenByStudentAsync(string schoolId, long academicYearId, IReadOnlyCollection<string> studentIds, CancellationToken ct);
        /// <summary>The audit list: every payment of this school that carried a concession in the date range (null bound = open), newest first, one page. ActiveTotal sums the Active ones over the whole range.</summary>
        Task<ConcessionLogPage> GetConcessionLogAsync(string schoolId, DateTime? fromUtc, DateTime? toExclusiveUtc, int page, int pageSize, CancellationToken ct);

        Task<List<ClassStudentRow>> GetClassStudentsAsync(string schoolId, long academicYearId, long classId, long? sectionId, CancellationToken ct);
    }
}
