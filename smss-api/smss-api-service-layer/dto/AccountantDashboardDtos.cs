namespace smss_api_service_layer.dto
{
    /// <summary>Names of the Accountant dashboard parts that could not be loaded: the client shows "Unavailable" for them, never a wrong 0.</summary>
    public static class AccountantDashboardParts
    {
        /// <summary>The Fee dashboard of the current academic year: this month, outstanding, overdue, upcoming and the 6-month chart.</summary>
        public const string Fees = "fees";
        public const string CollectedToday = "collectedToday";
        public const string Payments = "payments";
        /// <summary>The late fee and total received tiles (they read the payments' late fee columns).</summary>
        public const string LateFee = "lateFee";
    }

    public class AccountantDashboardStatsDto
    {
        /// <summary>CASH received today (India date) on payments that are not voided, whatever year's installments they paid: all-years cash, not a year figure. 0 on a day nobody has paid yet.</summary>
        public decimal? CollectedToday { get; set; }
        /// <summary>CASH received in the current India calendar month, summed over every year's installments (the last bar of the chart): all-years cash, not a year figure.</summary>
        public decimal? CollectedThisMonth { get; set; }
        /// <summary>LATE FEE collected today / this month (India dates, all years, non-voided payments), without the tax on it. A separate figure: "Cash collected" above is fee only and does not include it.</summary>
        public decimal? LateFeeToday { get; set; }
        public decimal? LateFeeThisMonth { get; set; }
        /// <summary>TOTAL RECEIVED today / this month: what the non-voided receipts add up to (fee + late fee + tax), so the cash drawer reconciles against the receipts.</summary>
        public decimal? TotalReceivedToday { get; set; }
        public decimal? TotalReceivedThisMonth { get; set; }
        public decimal? Outstanding { get; set; }
        public int? OverdueCount { get; set; }
        public decimal? OverdueAmount { get; set; }
        /// <summary>Installments falling due from today up to UpcomingDays days ahead.</summary>
        public int? UpcomingCount { get; set; }
        public decimal? UpcomingAmount { get; set; }
        public int UpcomingDays { get; set; }
    }

    public class DashboardPaymentDto
    {
        public long PaymentId { get; set; }
        public string ReceiptNumber { get; set; } = null!;
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public decimal Amount { get; set; }
        public string PaymentMode { get; set; } = null!;
        public DateTime PaymentDate { get; set; }
    }

    public class AccountantDashboardDto
    {
        public DateOnly Today { get; set; }
        /// <summary>The academic year being VIEWED: it decides the DUES figures (outstanding, overdue, upcoming). The cash figures are always all-years cash.</summary>
        public long? AcademicYearId { get; set; }
        public string? AcademicYearName { get; set; }
        public bool AcademicYearIsCurrent { get; set; }
        public bool AcademicYearIsClosed { get; set; }
        public string? CurrentYearName { get; set; }
        /// <summary>A sentence saying which year is shown when it is not simply the current one; null otherwise.</summary>
        public string? ViewingNote { get; set; }
        /// <summary>False = the viewed year has no fee items: the dues figures are null and the client says "No fee structure yet".</summary>
        public bool HasFeeSetup { get; set; }
        public AccountantDashboardStatsDto Stats { get; set; } = new();
        /// <summary>The last 6 calendar months, oldest first, this month last, zero-filled. Empty when the fees part is unavailable.</summary>
        public List<FeeCollectionMonthDto> CollectionTrend { get; set; } = new();
        /// <summary>The latest 10 active payments, newest first. Empty when there are none yet.</summary>
        public List<DashboardPaymentDto> RecentPayments { get; set; } = new();
        public List<string> PartsUnavailable { get; set; } = new();
    }
}
