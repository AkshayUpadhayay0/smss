namespace smss_api_service_layer.dto
{
    public class DashboardSessionDto
    {
        public long AcademicYearId { get; set; }
        /// <summary>The academic year's own name, e.g. "2026-27". The client shows "Academic Year 2026-27".</summary>
        public string Name { get; set; } = null!;
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
    }

    public class DashboardEmployeesDto
    {
        public int Total { get; set; }
        public int Teachers { get; set; }
        public int Accountants { get; set; }
        public int AdmissionStaff { get; set; }
        public int Drivers { get; set; }
        public int Other { get; set; }
    }

    /// <summary>The redesigned School Admin dashboard (GET /api/Dashboard/school-admin). Numbers only: no names, no personal data.</summary>
    public class SchoolAdminDashboardDto
    {
        /// <summary>Today in India. "This month" is the calendar month of this date.</summary>
        public DateOnly Today { get; set; }
        public DateOnly MonthStart { get; set; }
        public DateOnly MonthEnd { get; set; }
        /// <summary>The current Active academic year; null when the school has none (the session figures are then null).</summary>
        public DashboardSessionDto? Session { get; set; }

        public int TotalStudents { get; set; }
        /// <summary>Students with at least one due this month, all of them fully paid.</summary>
        public int StudentsPaidThisMonth { get; set; }
        /// <summary>Students with at least one due this month that is unpaid or part-paid. Students with no due this month are in neither count.</summary>
        public int StudentsNotPaidThisMonth { get; set; }
        public decimal FeeCollectedThisMonth { get; set; }
        public decimal FeeOutstandingThisMonth { get; set; }

        /// <summary>Fee collected from the start of the month / of the session until today. Null session figure = no current academic year.</summary>
        public decimal IncomeThisMonth { get; set; }
        public decimal? IncomeThisSession { get; set; }
        /// <summary>No school-level expenses data model exists yet: false means "Not set up yet", never a zero.</summary>
        public bool ExpensesSetUp { get; set; }
        public decimal? ExpensesThisMonth { get; set; }
        public decimal? ExpensesThisSession { get; set; }
        /// <summary>No other-income data model exists yet: income is fee collection only.</summary>
        public bool OtherIncomeSetUp { get; set; }

        /// <summary>Marks written today school-wide and how many are Present. Marked = 0 means "Not marked yet".</summary>
        public int AttendanceMarkedToday { get; set; }
        public int AttendancePresentToday { get; set; }
        /// <summary>Active students admitted as NEW_ADMISSION with an admission date inside the current academic year; null with no current year.</summary>
        public int? NewAdmissionsThisSession { get; set; }
        public DashboardEmployeesDto Employees { get; set; } = new();
        /// <summary>Active vehicles. 0 = the school has no transport data: the client hides the card.</summary>
        public int ActiveVehicles { get; set; }
    }
}
