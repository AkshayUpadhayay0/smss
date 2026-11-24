using System.Globalization;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;

namespace smss_api_service_layer.helper
{
    /// <summary>The attendance and fee-collection series the Principal and Accountant dashboards draw. Pure functions, no database.</summary>
    public static class DashboardTrends
    {
        public const int AttendanceTrendDays = 14;
        public const int FeeTrendMonths = 6;

        /// <summary>Present / marked x 100 to one decimal; null when nothing is marked.</summary>
        public static decimal? PresentPercent(int present, int marked) =>
            marked <= 0 ? null : Math.Round(present * 100m / marked, 1, MidpointRounding.AwayFromZero);

        /// <summary>Exactly <paramref name="days"/> calendar days ending today (oldest first), each with its marks from <paramref name="rows"/> (a day without any has 0 / 0 and a null percentage).</summary>
        public static List<AttendanceTrendPointDto> AttendanceTrend(DateOnly today, IEnumerable<SchoolAttendanceDayRow> rows, int days = AttendanceTrendDays)
        {
            var byDate = rows.ToDictionary(r => r.Date);
            return Enumerable.Range(0, days).Select(i => today.AddDays(i - (days - 1))).Select(d =>
            {
                byDate.TryGetValue(d, out var r);
                return new AttendanceTrendPointDto { Date = d, Marked = r?.Marked ?? 0, Present = r?.Present ?? 0, PresentPercent = PresentPercent(r?.Present ?? 0, r?.Marked ?? 0) };
            }).ToList();
        }

        /// <summary>The first day of each of the last <paramref name="months"/> calendar months, oldest first, the month of <paramref name="today"/> last.</summary>
        public static List<DateOnly> LastMonths(DateOnly today, int months = FeeTrendMonths)
        {
            var thisMonth = new DateOnly(today.Year, today.Month, 1);
            return Enumerable.Range(0, months).Select(i => thisMonth.AddMonths(i - (months - 1))).ToList();
        }

        /// <summary>
        /// The month-by-month collection series for <paramref name="months"/>, summed over every academic year's dashboard given (a payment belongs to the year its installments are in,
        /// so a window that crosses a year boundary needs both). Months nobody paid in are zero.
        /// </summary>
        public static List<FeeCollectionMonthDto> FeeTrend(IEnumerable<DateOnly> months, IEnumerable<FeeDashboardDto> yearDashboards)
        {
            var sums = new Dictionary<DateOnly, decimal>();
            foreach (var m in yearDashboards.SelectMany(d => d.CollectionByMonth))
                sums[m.Month] = sums.GetValueOrDefault(m.Month) + m.Amount;
            return months.Select(m => new FeeCollectionMonthDto { Month = m, Label = m.ToString("MMM yyyy", CultureInfo.InvariantCulture), Amount = sums.GetValueOrDefault(m) }).ToList();
        }
    }
}
