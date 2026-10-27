namespace smss_api_service_layer.helper
{
    /// <summary>
    /// The rules that turn a fee frequency into installments: how many, on which dates, for how much, and what status an
    /// installment has. Pure functions, no database.
    /// </summary>
    public static class FeeSchedule
    {
        public const string OneTime = "ONE_TIME";
        public const string Monthly = "MONTHLY";
        public const string Quarterly = "QUARTERLY";
        public const string HalfYearly = "HALF_YEARLY";
        public const string Annual = "ANNUAL";

        public const string StatusPaid = "Paid";
        public const string StatusPartiallyPaid = "Partially Paid";
        public const string StatusPending = "Pending";
        public const string StatusOverdue = "Overdue";

        /// <summary>Installments in a full academic year: Monthly 12, Quarterly 4, Half-Yearly 2, Annual / One Time 1.</summary>
        public static int Installments(string frequencyCode) => frequencyCode switch
        {
            Monthly => 12,
            Quarterly => 4,
            HalfYearly => 2,
            _ => 1
        };

        /// <summary>Recurring frequencies fall due on a day of the month, so a fee structure line using one needs a due day.</summary>
        public static bool IsRecurring(string frequencyCode) => frequencyCode is Monthly or Quarterly or HalfYearly;

        /// <summary>
        /// The frequencies a student may pay a fee head at, given the frequency of the fee structure line.
        /// A One Time or Annual fee is a single charge and stays that way; a recurring fee (Monthly / Quarterly / Half-Yearly)
        /// can be paid at any recurring frequency or once for the whole year.
        /// </summary>
        public static IReadOnlyList<string> AllowedStudentFrequencies(string structureFrequencyCode) =>
            structureFrequencyCode is OneTime or Annual
                ? new[] { structureFrequencyCode }
                : new[] { Monthly, Quarterly, HalfYearly, Annual };

        /// <summary>
        /// The installment dates of a full academic year, starting in the month the year starts: 12 monthly (one per month),
        /// 4 quarterly (every 3rd month), 2 half-yearly (every 6th), 1 for Annual / One Time. The day is the due day, cut back
        /// to the month's length (the 31st in February becomes the 28th/29th). Without a due day: the 1st for recurring
        /// frequencies, the year's own start date for a single charge.
        /// </summary>
        public static List<DateOnly> DueDates(DateOnly academicYearStart, string frequencyCode, int? dueDay)
        {
            var count = Installments(frequencyCode);
            var step = 12 / count;
            var day = dueDay ?? (count == 1 ? academicYearStart.Day : 1);

            var dates = new List<DateOnly>(count);
            for (var i = 0; i < count; i++)
            {
                var month = new DateOnly(academicYearStart.Year, academicYearStart.Month, 1).AddMonths(i * step);
                dates.Add(WithDay(month.Year, month.Month, day));
            }
            return dates;
        }

        /// <summary>A month as a number, for comparing installment months regardless of the day.</summary>
        public static int Slot(DateOnly d) => d.Year * 12 + d.Month;

        /// <summary>
        /// Which installments of a full-year schedule a student actually owes when they start on <paramref name="start"/>: those whose
        /// due date falls ON OR AFTER the start date. So a student admitted on 15 October with a due day of the 1st first owes 1 November
        /// (not an October installment that is already overdue on day one), while one admitted on 5 October with a due day of the 10th
        /// still owes the 10 October installment. A single charge (One Time / Annual, i.e. one installment) is unaffected by when they join.
        /// </summary>
        public static List<int> InstallmentsFrom(IReadOnlyList<DateOnly> fullYearDates, DateOnly start) =>
            fullYearDates.Count == 1
                ? new List<int> { 0 }
                : Enumerable.Range(0, fullYearDates.Count).Where(i => fullYearDates[i] >= start).ToList();

        /// <summary>
        /// The installments a student starting on <paramref name="start"/> actually owes: (index into the full-year schedule, due date).
        ///  - Several installments: those due on or after the start date, same amount each, just fewer of them.
        ///  - None of them left (every configured date this year has already passed, e.g. a Half-Yearly fee due 1 April and 1 October
        ///    for a student admitted on 15 October): exactly ONE installment dated at the start date, for the normal per-installment amount
        ///    (no proration), so the fee is never silently dropped.
        ///  - A single charge (One Time / Annual): its one installment, dated from the start date if that is later.
        /// </summary>
        public static List<(int Index, DateOnly Date)> OwedSchedule(IReadOnlyList<DateOnly> fullYearDates, DateOnly start)
        {
            if (fullYearDates.Count == 1) return new() { (0, fullYearDates[0] < start ? start : fullYearDates[0]) };

            var owed = Enumerable.Range(0, fullYearDates.Count).Where(i => fullYearDates[i] >= start).Select(i => (i, fullYearDates[i])).ToList();
            return owed.Count > 0 ? owed : new() { (0, start) };
        }

        /// <summary>The given day of a month, cut back to the month's length.</summary>
        public static DateOnly WithDay(int year, int month, int day) =>
            new(year, month, Math.Min(Math.Max(day, 1), DateTime.DaysInMonth(year, month)));

        /// <summary>Splits an amount into n parts that add up exactly: equal to the paisa, the leftover paisa on the last one.</summary>
        public static decimal[] Split(decimal total, int parts)
        {
            if (parts <= 0) return Array.Empty<decimal>();
            var each = Math.Round(total / parts, 2, MidpointRounding.ToZero);
            var result = Enumerable.Repeat(each, parts).ToArray();
            result[^1] = total - each * (parts - 1);
            return result;
        }

        public const string StatusNotSet = "Not Set";
        public static readonly string[] OverallStatuses = { StatusOverdue, StatusPartiallyPaid, StatusPending, StatusPaid, StatusNotSet };

        /// <summary>
        /// A student's overall fee state for the list, from their installment totals. In priority order:
        /// Overdue (any installment overdue) > Partially Paid (some money in and a balance left: a part-paid installment, or some installments
        /// paid and others not) > Pending (nothing paid yet) > Paid (nothing left, which includes "has fee items but nothing is owed": every
        /// optional head switched off, or a full concession). Not Set only when no fee items were ever assigned for the year.
        /// </summary>
        public static string Overall(bool hasItems, decimal totalDue, decimal totalPaid, int overdueCount)
        {
            if (!hasItems) return StatusNotSet;
            if (overdueCount > 0) return StatusOverdue;
            if (totalPaid >= totalDue) return StatusPaid;
            return totalPaid > 0 ? StatusPartiallyPaid : StatusPending;
        }

        /// <summary>
        /// Computed, never stored. Paid when fully paid; Overdue when something is still owed and the due date has passed
        /// (a partly paid installment past its due date is Overdue: that is the state that needs action); otherwise Partially Paid
        /// when some has been paid, else Pending. An installment due today is still Pending.
        /// </summary>
        public static string Status(decimal amountDue, decimal amountPaid, DateOnly dueDate, DateOnly today)
        {
            if (amountPaid >= amountDue) return StatusPaid;
            if (dueDate < today) return StatusOverdue;
            return amountPaid > 0 ? StatusPartiallyPaid : StatusPending;
        }
    }

    /// <summary>"Today" for a school: the date in India, not the server's UTC date (which is a day behind until 05:30 IST).</summary>
    public static class SchoolClock
    {
        private static readonly TimeZoneInfo? India = Find("India Standard Time") ?? Find("Asia/Kolkata");

        /// <summary>The UTC instant at which the given calendar day starts in the school's time zone (India), for filtering timestamps by school day.</summary>
        public static DateTime StartOfDayUtc(DateOnly day)
        {
            var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            return India == null ? DateTime.SpecifyKind(local, DateTimeKind.Utc) : DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(local, India), DateTimeKind.Utc);
        }

        public static DateOnly Today() => DateOnly.FromDateTime(India == null ? DateTime.UtcNow : TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, India));

        private static TimeZoneInfo? Find(string id)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { return null; }
            catch (InvalidTimeZoneException) { return null; }
        }
    }
}
