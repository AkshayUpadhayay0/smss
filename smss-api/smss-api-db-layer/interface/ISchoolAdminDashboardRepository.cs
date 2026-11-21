namespace smss_api_db_layer.@interface
{
    public record DashboardYearRow(long AcademicYearId, string YearName, DateOnly StartDate, DateOnly EndDate);
    public record DashboardFeeMonthRow(int StudentsWithDues, int StudentsNotPaid, decimal Outstanding);
    public record DashboardEmployeeRow(int Total, int Teachers, int Accountants, int AdmissionStaff, int Drivers);

    /// <summary>
    /// The School Admin dashboard's figures, all aggregated in the database (nothing is loaded to be counted) and all scoped to ONE school.
    /// Read-only; no personal data leaves it. Called one query after another on the request's DbContext.
    /// </summary>
    public interface ISchoolAdminDashboardRepository
    {
        Task<int?> ActiveStatusIdAsync(CancellationToken ct);
        /// <summary>The school's current (is_current and Active) academic year, or null.</summary>
        Task<DashboardYearRow?> CurrentYearAsync(string schoolId, int activeStatusId, CancellationToken ct);
        Task<int> ActiveStudentsAsync(string schoolId, int activeStatusId, CancellationToken ct);
        /// <summary>
        /// Active students' dues falling due in monthStart..monthEnd (both included): how many students have at least one, how many of those still owe on at least one
        /// (paid &lt; due; a discounted due counts as paid because the discount is part of amount_paid), and the unpaid balance.
        /// </summary>
        Task<DashboardFeeMonthRow> FeeMonthAsync(string schoolId, int activeStatusId, DateOnly monthStart, DateOnly monthEnd, CancellationToken ct);
        /// <summary>Cash collected: allocations of Active (not voided) payments made in [fromUtc, toUtcExclusive), minus the discounted part (the Fee dashboard's definition).</summary>
        Task<decimal> CollectedAsync(string schoolId, DateTime fromUtc, DateTime toUtcExclusive, CancellationToken ct);
        /// <summary>Active students whose admission type is NEW_ADMISSION and whose admission date is in from..to (both included).</summary>
        Task<int> NewAdmissionsAsync(string schoolId, int activeStatusId, DateOnly from, DateOnly to, CancellationToken ct);
        Task<DashboardEmployeeRow> EmployeesAsync(string schoolId, int activeStatusId, CancellationToken ct);
        Task<int> ActiveVehiclesAsync(string schoolId, int activeStatusId, CancellationToken ct);
    }
}
