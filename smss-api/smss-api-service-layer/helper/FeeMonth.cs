using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;

namespace smss_api_service_layer.helper
{
    /// <summary>
    /// "A month's fee" for the class teacher's view, derived ONLY from the existing instalments (tb_student_fee_dues): the instalments whose DUE DATE falls in that month,
    /// with the amount already applied to each (amount_paid, the cached total of the payment allocations: a payment is split across instalments oldest-due-first or by an explicit
    /// split, see section 28). Nothing is allocated or computed here beyond summing and the status rule below. Pure (no database, no clock) so it can be checked with any inputs.
    /// <para>Month status: no instalment due that month = No fee this month; everything paid = Paid; otherwise Overdue when any unpaid instalment's due date has passed (this wins,
    /// as everywhere else; a due date of today is not yet overdue); otherwise, for a month after the current one, Partially paid if something was paid ahead else Not due yet; for the
    /// current or an earlier month, Partially paid if something was paid else Unpaid.</para>
    /// </summary>
    public static class FeeMonth
    {
        public const string Paid = "Paid", Partial = "Partially paid", Unpaid = "Unpaid", Overdue = "Overdue", NotDueYet = "Not due yet", NoFee = "No fee this month";

        public static string Key(int year, int month) => $"{year:0000}-{month:00}";

        public static bool TryParse(string? value, out int year, out int month)
        {
            year = month = 0;
            if (string.IsNullOrWhiteSpace(value) || value.Length != 7 || value[4] != '-') return false;
            return int.TryParse(value.AsSpan(0, 4), out year) && int.TryParse(value.AsSpan(5, 2), out month) && month is >= 1 and <= 12 && year is >= 2000 and <= 2100;
        }

        /// <summary>Every calendar month the academic year touches, in order.</summary>
        public static List<(int Year, int Month)> MonthsOf(DateOnly start, DateOnly end)
        {
            var list = new List<(int, int)>();
            var y = start.Year; var m = start.Month;
            while (y * 12 + m <= end.Year * 12 + end.Month && list.Count < 24) { list.Add((y, m)); if (++m > 12) { m = 1; y++; } }
            return list;
        }

        /// <summary>The month shown when none is asked for: the current month, kept inside the academic year.</summary>
        public static (int Year, int Month) Default(DateOnly today, DateOnly yearStart, DateOnly yearEnd)
        {
            var t = today.Year * 12 + today.Month;
            var lo = yearStart.Year * 12 + yearStart.Month; var hi = yearEnd.Year * 12 + yearEnd.Month;
            var c = Math.Min(Math.Max(t, lo), hi);
            return ((c - 1) / 12, (c - 1) % 12 + 1);
        }

        /// <summary>The status of one student's instalments due in one month (null / empty = nothing due).</summary>
        public static string StatusOf(IReadOnlyCollection<StudentDueRow> inMonth, int year, int month, DateOnly today)
        {
            if (inMonth.Count == 0) return NoFee;
            var due = inMonth.Sum(d => d.AmountDue);
            var paid = inMonth.Sum(d => Math.Min(d.AmountPaid, d.AmountDue));
            if (paid >= due) return Paid;
            if (inMonth.Any(d => d.AmountPaid < d.AmountDue && d.DueDate < today)) return Overdue;
            var future = year * 12 + month > today.Year * 12 + today.Month;
            if (paid > 0) return Partial;
            return future ? NotDueYet : Unpaid;
        }

        /// <summary>One cell of the year grid: P, Partial, U, O, N (not due yet) or - (no fee that month).</summary>
        public static string Code(string status) => status switch { Paid => "P", Partial => "Partial", Unpaid => "U", Overdue => "O", NotDueYet => "N", _ => "-" };

        public static SectionFeeStudentDto Student(string? roll, string name, IReadOnlyList<StudentDueRow> dues, int year, int month, DateOnly today)
        {
            var inMonth = dues.Where(d => d.DueDate.Year == year && d.DueDate.Month == month).ToList();
            var monthEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
            var due = inMonth.Sum(d => d.AmountDue);
            var paid = inMonth.Sum(d => Math.Min(d.AmountPaid, d.AmountDue));
            return new SectionFeeStudentDto
            {
                RollNumber = roll, FullName = name, MonthStatus = StatusOf(inMonth, year, month, today),
                DueThisMonth = due, PaidThisMonth = paid, BalanceThisMonth = due - paid,
                // every earlier unpaid instalment plus this month's: the carry-forward
                TotalOutstandingTillDate = dues.Where(d => d.DueDate <= monthEnd).Sum(d => Math.Max(d.AmountDue - d.AmountPaid, 0))
            };
        }
    }
}
