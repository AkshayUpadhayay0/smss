using smss_api_db_layer.@interface;

namespace smss_api_service_layer.helper
{
    /// <summary>One calendar day as the attendance rules see it.</summary>
    public readonly record struct DayClass(DateOnly Date, int DayOfWeek, bool IsWorkingDay, string? NonWorkingReason, string? HolidayName);

    /// <summary>
    /// The single definition of which days count for attendance, shared by the monthly attendance report and the report card's term attendance, so the two can never disagree.
    /// <para>A day is NON-working when it is a Sunday (the school week is Monday to Saturday, as in the Timetable), falls inside an active Holiday calendar event, or lies outside
    /// the academic year. For one student a day also does not count before their enrolment date. Pure: no database, no clock.</para>
    /// </summary>
    public static class AttendanceDayRules
    {
        public const string SundayReason = "Sunday", HolidayReason = "Holiday", OutsideYearReason = "Outside academic year";

        public static DayClass Classify(DateOnly d, DateOnly yearStart, DateOnly yearEnd, IReadOnlyList<HolidayRow> holidays)
        {
            var dow = d.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)d.DayOfWeek;
            var hol = holidays.Where(h => h.StartDate <= d && d <= h.EndDate).Select(h => h.Title).Distinct().ToList();
            string? reason = dow == 7 ? SundayReason : hol.Count > 0 ? HolidayReason : d < yearStart || d > yearEnd ? OutsideYearReason : null;
            return new DayClass(d, dow, reason == null, reason, hol.Count > 0 ? string.Join(", ", hol) : null);
        }

        /// <summary>A working day on or after the student's enrolment date: the only days that can be present, absent or "not marked" for them.</summary>
        public static bool CountsForStudent(DayClass day, DateOnly enrolledFrom) => day.IsWorkingDay && day.Date >= enrolledFrom;
    }
}
