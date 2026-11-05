namespace smss_api_service_layer.dto
{
    /// <summary>
    /// GET api/Fee/section-status. What a class teacher sees of their class's fees: a status and three amounts per student, nothing else. Deliberately no
    /// receipts, payment modes, concessions, discounts, instalment detail or remarks.
    /// </summary>
    public class SectionFeeStatusDto
    {
        public long SectionId { get; set; }
        public string ClassName { get; set; } = null!;
        public string SectionName { get; set; } = null!;
        public long AcademicYearId { get; set; }
        public string AcademicYearName { get; set; } = null!;
        public SectionFeeSummaryDto Summary { get; set; } = new();
        public List<SectionFeeStudentDto> Students { get; set; } = new();
    }

    public class SectionFeeSummaryDto
    {
        public int Students { get; set; }
        /// <summary>Status Paid: every instalment settled (or nothing owed).</summary>
        public int FullyPaid { get; set; }
        /// <summary>Partially Paid: something paid, nothing overdue.</summary>
        public int PartiallyPaid { get; set; }
        /// <summary>Pending: nothing paid yet and nothing overdue.</summary>
        public int Unpaid { get; set; }
        /// <summary>Overdue: at least one instalment is past its due date and not settled (this wins over Partially Paid, as everywhere else).</summary>
        public int Overdue { get; set; }
        /// <summary>No fee structure assigned: not counted in the four above.</summary>
        public int NotSet { get; set; }
    }

    public class SectionFeeStudentDto
    {
        public string? RollNumber { get; set; }
        public string FullName { get; set; } = null!;
        /// <summary>Paid / Partially Paid / Pending / Overdue / Not Set: the same rule as the student list.</summary>
        public string Status { get; set; } = null!;
        public decimal TotalDue { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal Balance { get; set; }
    }
}
