namespace smss_api_service_layer.dto
{
    public class FeeCollectionMonthDto
    {
        /// <summary>First day of the month, e.g. 2026-10-01.</summary>
        public DateOnly Month { get; set; }
        public string Label { get; set; } = null!;
        public decimal Amount { get; set; }
    }

    public class FeeTaxBreakdownDto
    {
        public long TaxTypeId { get; set; }
        public string TaxName { get; set; } = null!;
        public string TaxCode { get; set; } = null!;
        public decimal TaxAmount { get; set; }
        public int PaymentCount { get; set; }
    }

    public class FeeConcessionBreakdownDto
    {
        public long ConcessionId { get; set; }
        public string ConcessionName { get; set; } = null!;
        public string ConcessionCode { get; set; } = null!;
        public decimal ConcessionAmount { get; set; }
        public int PaymentCount { get; set; }
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
        /// <summary>CASH received against those installments (Active payments only; voided ones are already taken back off): what the installments count as paid, minus the concession given.</summary>
        public decimal TotalCollected { get; set; }
        /// <summary>Discount given at payment time on Active payments in the same scope. NOT part of <see cref="TotalCollected"/>: TotalDue = TotalCollected + ConcessionGiven + TotalOutstanding.</summary>
        public decimal ConcessionGiven { get; set; }
        public List<FeeConcessionBreakdownDto> ConcessionByType { get; set; } = new();
        /// <summary>Tax collected on Active payments in the same scope. Reported on its own: it is NOT part of <see cref="TotalCollected"/> (which is fee only).</summary>
        public decimal TaxCollected { get; set; }
        public List<FeeTaxBreakdownDto> TaxByType { get; set; } = new();
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
        /// <summary>Cash collected: paid against the installments minus the concession given.</summary>
        public decimal TotalCollected { get; set; }
        /// <summary>Discount given at payment time (Active payments, this year).</summary>
        public decimal Concession { get; set; }
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
        public decimal TotalConcession { get; set; }
        public decimal TotalOutstanding { get; set; }
        public Dictionary<string, int> StatusCounts { get; set; } = new();
        public bool CanManage { get; set; }
    }

    public class ConcessionGivenRowDto
    {
        public long PaymentId { get; set; }
        public string ReceiptNumber { get; set; } = null!;
        public DateTime PaymentDate { get; set; }
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public string ConcessionName { get; set; } = null!;
        public string ConcessionCode { get; set; } = null!;
        /// <summary>PERCENTAGE or FIXED, and its value at the time (a later edit of the concession never changes this row).</summary>
        public string ConcessionType { get; set; } = null!;
        public decimal ConcessionValue { get; set; }
        public decimal ConcessionAmount { get; set; }
        public string? Reason { get; set; }
        public string? AppliedByName { get; set; }
        /// <summary>Active or Voided: a voided payment stays in the list (it is the audit trail) but is not in the total.</summary>
        public string Status { get; set; } = "Active";
    }

    public class ConcessionsGivenDto
    {
        public List<ConcessionGivenRowDto> Items { get; set; } = new();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        /// <summary>Sum of the Active payments' concession over the whole date range (not only this page).</summary>
        public decimal TotalAmount { get; set; }
    }
}
