namespace smss_api_service_layer.dto
{
    public class DashboardSessionDto
    {
        public long AcademicYearId { get; set; }
        /// <summary>The academic year's own name, e.g. "2026-27". The client shows "Academic Year 2026-27".</summary>
        public string Name { get; set; } = null!;
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        /// <summary>True when this is the school's current year (the one new fees and enrolments go to).</summary>
        public bool IsCurrent { get; set; }
        /// <summary>A closed year is read-only history.</summary>
        public bool IsClosed { get; set; }
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
        /// <summary>The academic year being VIEWED (the one asked for, else the current Active year); null when the school has none (the session figures are then null).</summary>
        public DashboardSessionDto? Session { get; set; }
        /// <summary>The school's current year name, so the client can say "viewing 2027-28, current is 2026-27". Null when there is none.</summary>
        public string? CurrentYearName { get; set; }
        /// <summary>False = no fee item was ever assigned in the viewed year: the fee figures are then not zeros but "no fee structure yet" (they are 0 here and must not be shown as real).</summary>
        public bool HasFeeSetup { get; set; }
        /// <summary>True when today is outside the viewed year, so "this month" was moved into the year's range (the first month before it starts, the last month after it ends).</summary>
        public bool MonthClamped { get; set; }
        /// <summary>A ready sentence for the client when MonthClamped, e.g. "Showing March 2027: today is after the academic year 2026-27."</summary>
        public string? MonthNote { get; set; }

        /// <summary>Active students with an Active enrolment in the VIEWED year.</summary>
        public int TotalStudents { get; set; }
        /// <summary>Students with at least one due this month, all of them fully paid.</summary>
        public int StudentsPaidThisMonth { get; set; }
        /// <summary>Students with at least one due this month that is unpaid or part-paid. Students with no due this month are in neither count.</summary>
        public int StudentsNotPaidThisMonth { get; set; }
        /// <summary>Fees received this month against the VIEWED year's installments (not total cash received: another year's installments paid this month are not here).</summary>
        public decimal FeeCollectedThisMonth { get; set; }
        public decimal FeeOutstandingThisMonth { get; set; }

        /// <summary>Fees received this month / for the whole viewed session, against that year's installments. Null session figure = no year.</summary>
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
        /// <summary>True when the school has no WhatsApp number on record: the page shows a prompt linking to the school profile. It never blocks anything.</summary>
        public bool WhatsAppMissing { get; set; }
    }
}
