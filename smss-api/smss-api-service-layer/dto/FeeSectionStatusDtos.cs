namespace smss_api_service_layer.dto
{
    /// <summary>
    /// GET api/Fee/section-status?month=YYYY-MM. One month of a class's fees, for the class teacher: "has THIS month's fee been submitted". A status and amounts per student, nothing else:
    /// no receipts, payment modes, concessions, discounts or instalment detail. "A month" = the instalments whose due date falls in it (fees are not all monthly: quarterly and annual
    /// heads show in the month they fall due); "paid" = what the existing payment allocations have applied to those instalments.
    /// </summary>
    public class SectionFeeStatusDto
    {
        public long SectionId { get; set; }
        public string ClassName { get; set; } = null!;
        public string SectionName { get; set; } = null!;
        public long AcademicYearId { get; set; }
        public string AcademicYearName { get; set; } = null!;
        /// <summary>yyyy-MM of the month shown.</summary>
        public string Month { get; set; } = null!;
        public string MonthLabel { get; set; } = null!;
        /// <summary>False when the month asked for lies outside the academic year: nothing is listed then (the screen shows an empty state).</summary>
        public bool MonthInAcademicYear { get; set; } = true;
        public bool IsCurrentMonth { get; set; }
        /// <summary>The months of the academic year (for the picker), in order.</summary>
        public List<SectionFeeMonthDto> Months { get; set; } = new();
        public SectionFeeSummaryDto Summary { get; set; } = new();
        public List<SectionFeeStudentDto> Students { get; set; } = new();
    }

    public class SectionFeeMonthDto
    {
        public string Month { get; set; } = null!;
        public string Label { get; set; } = null!;
        public bool IsCurrent { get; set; }
    }

    /// <summary>Counts and totals for the SELECTED month.</summary>
    public class SectionFeeSummaryDto
    {
        public int Students { get; set; }
        public int Paid { get; set; }
        public int Partial { get; set; }
        public int Unpaid { get; set; }
        public int Overdue { get; set; }
        public int NotDueYet { get; set; }
        public int NoFeeThisMonth { get; set; }
        /// <summary>Students with no fee structure at all (a subset of NoFeeThisMonth), so the screen can say "no fee structure assigned".</summary>
        public int NoFeeAssigned { get; set; }
        public decimal DueTotal { get; set; }
        public decimal CollectedTotal { get; set; }
    }

    public class SectionFeeStudentDto
    {
        public string? RollNumber { get; set; }
        public string FullName { get; set; } = null!;
        /// <summary>Paid / Partially paid / Unpaid / Overdue / Not due yet / No fee this month</summary>
        public string MonthStatus { get; set; } = null!;
        public decimal DueThisMonth { get; set; }
        public decimal PaidThisMonth { get; set; }
        public decimal BalanceThisMonth { get; set; }
        /// <summary>All unpaid instalments due up to the end of the selected month (earlier months plus this one).</summary>
        public decimal TotalOutstandingTillDate { get; set; }
    }

    /// <summary>GET api/Fee/section-status/months: every student x every month of the academic year, one status code per cell and no amounts.</summary>
    public class SectionFeeGridDto
    {
        public long SectionId { get; set; }
        public string ClassName { get; set; } = null!;
        public string SectionName { get; set; } = null!;
        public long AcademicYearId { get; set; }
        public string AcademicYearName { get; set; } = null!;
        public List<SectionFeeMonthDto> Months { get; set; } = new();
        public List<SectionFeeGridRowDto> Students { get; set; } = new();
    }

    public class SectionFeeGridRowDto
    {
        public string? RollNumber { get; set; }
        public string FullName { get; set; } = null!;
        /// <summary>One code per month, in the order of Months: P paid, Partial, U unpaid, O overdue, N not due yet, - no fee that month.</summary>
        public List<string> Cells { get; set; } = new();
    }
}
