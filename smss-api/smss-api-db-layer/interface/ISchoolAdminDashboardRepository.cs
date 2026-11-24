namespace smss_api_db_layer.@interface
{
    public record DashboardYearRow(long AcademicYearId, string YearName, DateOnly StartDate, DateOnly EndDate, bool IsCurrent = false, bool IsClosed = false);
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
        /// <summary>The school's own academic year by id (any year, closed or not), or null when it is not this school's.</summary>
        Task<DashboardYearRow?> YearAsync(string schoolId, long academicYearId, CancellationToken ct);
        /// <summary>Active students with an Active enrolment in the given year (enrolled in the year, not merely "Active in the school").</summary>
        Task<int> EnrolledStudentsAsync(string schoolId, int activeStatusId, long academicYearId, CancellationToken ct);
        /// <summary>Does the year have any fee items at all? False = no fee structure was ever assigned in it, so the fee tiles show an empty state, not zeros.</summary>
        Task<bool> HasFeeItemsAsync(string schoolId, long academicYearId, CancellationToken ct);
        /// <summary>
        /// Dues OF THE GIVEN YEAR (through tb_student_fee_items.academic_year_id) of Active students falling due in monthStart..monthEnd (both included): how many students have at least one,
        /// how many of those still owe on at least one (paid &lt; due; a discounted due counts as paid because the discount is part of amount_paid), and the unpaid balance.
        /// </summary>
        Task<DashboardFeeMonthRow> FeeMonthAsync(string schoolId, int activeStatusId, long academicYearId, DateOnly monthStart, DateOnly monthEnd, CancellationToken ct);
        /// <summary>
        /// Fees received against the given year's installments: allocations of Active (not voided) payments, minus the discounted part, by payment time in [fromUtc, toUtcExclusive)
        /// (both null = the whole session, whenever it was paid).
        /// </summary>
        Task<decimal> CollectedAsync(string schoolId, long academicYearId, DateTime? fromUtc, DateTime? toUtcExclusive, CancellationToken ct);
        /// <summary>Active students whose admission type is NEW_ADMISSION and whose admission date is in from..to (both included).</summary>
        Task<int> NewAdmissionsAsync(string schoolId, int activeStatusId, DateOnly from, DateOnly to, CancellationToken ct);
        Task<DashboardEmployeeRow> EmployeesAsync(string schoolId, int activeStatusId, CancellationToken ct);
        Task<int> ActiveVehiclesAsync(string schoolId, int activeStatusId, CancellationToken ct);
    }
}
