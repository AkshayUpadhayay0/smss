namespace smss_api_service_layer.dto
{
    public class FeeCollectionMonthDto
    {
        /// <summary>First day of the month, e.g. 2026-10-01.</summary>
        public DateOnly Month { get; set; }
        public string Label { get; set; } = null!;
        public decimal Amount { get; set; }
    }

    public class FeeDashboardDto
    {
        public long AcademicYearId { get; set; }
        public string AcademicYearName { get; set; } = null!;
        public long? ClassId { get; set; }
        public long? SectionId { get; set; }
        /// <summary>The "upcoming" window used: dues falling between today and today + this many days.</summary>
        public int UpcomingDays { get; set; }
        public DateOnly AsOf { get; set; }

        /// <summary>Everything billed for the year in this scope.</summary>
        public decimal TotalDue { get; set; }
        /// <summary>Money received against those installments (Active payments only; voided ones are already taken back off).</summary>
        public decimal TotalCollected { get; set; }
        public decimal TotalOutstanding { get; set; }
        public decimal OverdueAmount { get; set; }
        public int OverdueCount { get; set; }
        public decimal UpcomingAmount { get; set; }
        public int UpcomingCount { get; set; }
        /// <summary>Collected per month (by payment date, India time) across the academic year, zero-filled.</summary>
        public List<FeeCollectionMonthDto> CollectionByMonth { get; set; } = new();
        /// <summary>True for School Admin / Accountant. The Principal sees the same numbers read-only (no drill-down links).</summary>
        public bool CanManage { get; set; }
    }

    public class ClassFeeRowDto
    {
        public string StudentId { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string? RollNumber { get; set; }
        public string SectionName { get; set; } = null!;
        public decimal TotalDue { get; set; }
        public decimal TotalCollected { get; set; }
        public decimal Outstanding { get; set; }
        /// <summary>Overdue | Partially Paid | Pending | Paid | Not Set: the same rule as the student list.</summary>
        public string Status { get; set; } = "Not Set";
    }

    public class ClassFeeReportDto
    {
        public long AcademicYearId { get; set; }
        public string AcademicYearName { get; set; } = null!;
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public long? SectionId { get; set; }
        public List<ClassFeeRowDto> Students { get; set; } = new();
        /// <summary>The class total row.</summary>
        public decimal TotalDue { get; set; }
        public decimal TotalCollected { get; set; }
        public decimal TotalOutstanding { get; set; }
        public Dictionary<string, int> StatusCounts { get; set; } = new();
        public bool CanManage { get; set; }
    }
}
