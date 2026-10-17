namespace smss_api_db_layer.@interface
{
    public record ReportYearRow(long AcademicYearId, string YearName, DateOnly StartDate, DateOnly EndDate);
    public record DashboardTotals(decimal TotalDue, decimal TotalCollected, decimal OverdueAmount, int OverdueCount, decimal UpcomingAmount, int UpcomingCount);
    public record CollectionMonth(int Year, int Month, decimal Amount);
    public record ClassStudentRow(string StudentId, string FirstName, string? MiddleName, string LastName, string? RollNumber, string SectionName);

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

        Task<List<ClassStudentRow>> GetClassStudentsAsync(string schoolId, long academicYearId, long classId, long? sectionId, CancellationToken ct);
    }
}
