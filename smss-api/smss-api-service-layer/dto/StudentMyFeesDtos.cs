namespace smss_api_service_layer.dto
{
    /// <summary>
    /// GET api/Student/me/fees. What a student / parent may see of their own fees, composed from the existing fee services (no fee math here). Deliberately NOT
    /// the staff DTOs: no concession names, remarks or approver, no voided payments, no collector, no payment remarks, no ids that select anything.
    /// </summary>
    public class StudentMyFeesDto
    {
        public string? AcademicYearName { get; set; }
        /// <summary>False when no fee structure / fee items are assigned for the year: the page shows an empty state.</summary>
        public bool HasFees { get; set; }
        /// <summary>Paid / Partially Paid / Pending / Overdue / Not Set: the same rule as the student list (FeeSchedule.Overall).</summary>
        public string OverallStatus { get; set; } = "Not Set";
        public decimal TotalDue { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal Balance { get; set; }
        public decimal OverdueAmount { get; set; }
        public int OverdueCount { get; set; }
        public List<MyFeeHeadDto> Heads { get; set; } = new();
        public List<MyFeeDueDto> Dues { get; set; } = new();
        public List<MyFeePaymentDto> Payments { get; set; } = new();
    }

    public class MyFeeHeadDto
    {
        public string FeeTypeName { get; set; } = null!;
        public string FrequencyName { get; set; } = null!;
        /// <summary>Per period, before any concession.</summary>
        public decimal Amount { get; set; }
        /// <summary>The concession already taken off per period (amount only, nothing about how it was granted).</summary>
        public decimal Concession { get; set; }
        public decimal NetAmount { get; set; }
        /// <summary>For the whole academic year.</summary>
        public decimal AnnualNetAmount { get; set; }
    }

    public class MyFeeDueDto
    {
        public string FeeTypeName { get; set; } = null!;
        public DateOnly DueDate { get; set; }
        public decimal AmountDue { get; set; }
        public decimal AmountPaid { get; set; }
        public decimal Balance { get; set; }
        public string Status { get; set; } = null!;
    }

    public class MyFeePaymentDto
    {
        public DateTime PaymentDate { get; set; }
        public decimal Amount { get; set; }
        public string ReceiptNumber { get; set; } = null!;
        public string PaymentMode { get; set; } = null!;
    }
}
