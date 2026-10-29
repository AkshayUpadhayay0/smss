namespace smss_api_service_layer.helper
{
    /// <summary>tb_school_subscriptions.status. OVERDUE is allowed in the table but never written: it is derived (see SubscriptionRules.EffectiveStatus).</summary>
    public static class SubscriptionStatuses
    {
        public const string Trial = "TRIAL";
        public const string Active = "ACTIVE";
        public const string Overdue = "OVERDUE";
        public const string Cancelled = "CANCELLED";
        /// <summary>Only in dashboard rows: a school registered before billing existed and has no subscription row.</summary>
        public const string NotSet = "NOT_SET";
    }

    public static class BillingFrequencies
    {
        public const string Monthly = "MONTHLY";
        public const string Yearly = "YEARLY";

        /// <summary>The canonical spelling of what the client sent (any case), or null when it is not a billing frequency.</summary>
        public static string? Canonical(string? value)
        {
            var v = value?.Trim();
            return string.Equals(v, Monthly, StringComparison.OrdinalIgnoreCase) ? Monthly
                 : string.Equals(v, Yearly, StringComparison.OrdinalIgnoreCase) ? Yearly : null;
        }
    }

    /// <summary>Derived, never stored.</summary>
    public static class InvoiceStatuses
    {
        public const string Pending = "Pending";
        public const string Paid = "Paid";
        public const string Overdue = "Overdue";

        public static string? Canonical(string? value) =>
            new[] { Pending, Paid, Overdue }.FirstOrDefault(s => string.Equals(s, value?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Dashboard payment-collection buckets: derived from invoices and Active payments, never stored.</summary>
    public static class CollectionBuckets
    {
        public const string Paid = "Paid";
        public const string PartiallyPaid = "PartiallyPaid";
        public const string Pending = "Pending";
        public const string Overdue = "Overdue";
        public static readonly string[] All = { Paid, Pending, PartiallyPaid, Overdue };
    }

    public static class SubscriptionRules
    {
        /// <summary>Last day of a cycle that starts on <paramref name="start"/>. The next cycle starts the day after, so cycles never overlap or leave a gap.</summary>
        public static DateOnly CycleEnd(DateOnly start, string frequency) =>
            start.AddMonths(frequency == BillingFrequencies.Yearly ? 12 : 1).AddDays(-1);

        /// <summary>
        /// Monthly = rate x billable users. Yearly = rate x billable users x 12, then the yearly discount. Rounded to paise, away from zero. The discount
        /// percent returned is the one actually applied (0 for monthly), which is what gets stored on the invoice.
        /// </summary>
        public static (decimal Gross, decimal Discount, decimal Net, decimal DiscountPercent) Amounts(decimal ratePerStudent, int billableUsers, string frequency, decimal yearlyDiscountPercent)
        {
            var yearly = frequency == BillingFrequencies.Yearly;
            var gross = Math.Round(ratePerStudent * billableUsers * (yearly ? 12 : 1), 2, MidpointRounding.AwayFromZero);
            var percent = yearly ? yearlyDiscountPercent : 0m;
            var discount = Math.Round(gross * percent / 100m, 2, MidpointRounding.AwayFromZero);
            return (gross, discount, gross - discount, percent);
        }

        /// <summary>What a school is billed on: its students, plus its teachers and staff when its terms include them.</summary>
        public static int BillableUsers(int students, int teachers, int staff, bool includeStaff) => students + (includeStaff ? teachers + staff : 0);

        /// <summary>Paid once the Active payments cover the net amount; Overdue once the due date has passed without that; Pending otherwise.</summary>
        public static string InvoiceStatus(decimal netAmount, decimal paidAmount, DateOnly dueDate, DateOnly today) =>
            paidAmount >= netAmount ? InvoiceStatuses.Paid
            : dueDate < today ? InvoiceStatuses.Overdue
            : InvoiceStatuses.Pending;

        /// <summary>
        /// Payment-collection bucket of an invoice. Same order as the fee module: fully covered = Paid; otherwise past due = Overdue (even if part-paid, the
        /// balance is late); otherwise some money in = Partially Paid; otherwise Pending.
        /// </summary>
        public static string CollectionBucket(decimal netAmount, decimal paidAmount, DateOnly dueDate, DateOnly today) =>
            paidAmount >= netAmount ? CollectionBuckets.Paid
            : dueDate < today ? CollectionBuckets.Overdue
            : paidAmount > 0 ? CollectionBuckets.PartiallyPaid
            : CollectionBuckets.Pending;

        /// <summary>
        /// The period the trend is compared with. A month-to-date range (starts on the 1st, ends in that month) is compared with the same days of the previous
        /// month (capped at its length); any other range with the equal-length range just before it.
        /// </summary>
        public static (DateOnly From, DateOnly To) PreviousPeriod(DateOnly from, DateOnly to)
        {
            var days = to.DayNumber - from.DayNumber + 1;
            if (from.Day == 1 && to.Year == from.Year && to.Month == from.Month)
            {
                var prevFrom = from.AddMonths(-1);
                var prevEnd = new DateOnly(prevFrom.Year, prevFrom.Month, DateTime.DaysInMonth(prevFrom.Year, prevFrom.Month));
                var prevTo = prevFrom.AddDays(days - 1);
                return (prevFrom, prevTo > prevEnd ? prevEnd : prevTo);
            }
            var end = from.AddDays(-1);
            return (end.AddDays(-(days - 1)), end);
        }

        /// <summary>Change from <paramref name="previous"/> to <paramref name="current"/> as a percentage of the previous size; null when there is nothing to compare with (previous is 0).</summary>
        public static decimal? TrendPercent(decimal current, decimal previous) =>
            previous == 0 ? null : Math.Round((current - previous) / Math.Abs(previous) * 100m, 1, MidpointRounding.AwayFromZero);

        /// <summary>
        /// The state shown to the Super Admin. Nothing flips a subscription to OVERDUE in the database (no scheduler), so an ACTIVE (or OVERDUE) subscription
        /// with any overdue invoice is shown as OVERDUE, and goes back to ACTIVE by itself once that invoice is paid.
        /// </summary>
        public static string EffectiveStatus(string? storedStatus, bool hasOverdueInvoice) =>
            storedStatus == null ? SubscriptionStatuses.NotSet
            : (storedStatus is SubscriptionStatuses.Active or SubscriptionStatuses.Overdue)
                ? (hasOverdueInvoice ? SubscriptionStatuses.Overdue : SubscriptionStatuses.Active)
            : storedStatus;
    }
}
