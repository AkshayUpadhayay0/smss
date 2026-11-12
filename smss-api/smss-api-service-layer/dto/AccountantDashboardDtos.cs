namespace smss_api_service_layer.dto
{
    /// <summary>Names of the Accountant dashboard parts that could not be loaded: the client shows "Unavailable" for them, never a wrong 0.</summary>
    public static class AccountantDashboardParts
    {
        /// <summary>The Fee dashboard of the current academic year: this month, outstanding, overdue, upcoming and the 6-month chart.</summary>
        public const string Fees = "fees";
        public const string CollectedToday = "collectedToday";
        public const string Payments = "payments";
    }

    public class AccountantDashboardStatsDto
    {
        /// <summary>Active payments received today (India date). 0 on a day nobody has paid yet.</summary>
        public decimal? CollectedToday { get; set; }
        /// <summary>The Fee dashboard's collection figure for the current India calendar month (the last bar of the chart).</summary>
        public decimal? CollectedThisMonth { get; set; }
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
        public long? AcademicYearId { get; set; }
        public string? AcademicYearName { get; set; }
        public AccountantDashboardStatsDto Stats { get; set; } = new();
        /// <summary>The last 6 calendar months, oldest first, this month last, zero-filled. Empty when the fees part is unavailable.</summary>
        public List<FeeCollectionMonthDto> CollectionTrend { get; set; } = new();
        /// <summary>The latest 10 active payments, newest first. Empty when there are none yet.</summary>
        public List<DashboardPaymentDto> RecentPayments { get; set; } = new();
        public List<string> PartsUnavailable { get; set; } = new();
    }
}
