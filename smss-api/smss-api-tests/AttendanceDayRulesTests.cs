using smss_api_db_layer.@interface;
using smss_api_service_layer.helper;
using Xunit;

namespace smss_api_tests
{
    public class AttendanceDayRulesTests
    {
        private static readonly DateOnly Start = new(2026, 4, 1), End = new(2027, 3, 31);
        private static readonly List<HolidayRow> Holidays = new() { new("Diwali", new DateOnly(2026, 11, 8), new DateOnly(2026, 11, 10)) };

        [Fact] public void Sunday_is_not_a_working_day() { var c = AttendanceDayRules.Classify(new DateOnly(2026, 11, 1), Start, End, Holidays); Assert.False(c.IsWorkingDay); Assert.Equal("Sunday", c.NonWorkingReason); }
        [Fact] public void Active_holiday_is_not_a_working_day() { var c = AttendanceDayRules.Classify(new DateOnly(2026, 11, 9), Start, End, Holidays); Assert.False(c.IsWorkingDay); Assert.Equal("Holiday", c.NonWorkingReason); Assert.Equal("Diwali", c.HolidayName); }
        [Fact] public void Day_outside_the_year_is_not_a_working_day() { var c = AttendanceDayRules.Classify(new DateOnly(2026, 3, 16), Start, End, Holidays); Assert.False(c.IsWorkingDay); Assert.Equal("Outside academic year", c.NonWorkingReason); }
        [Fact] public void Ordinary_weekday_and_saturday_are_working_days()
        {
            Assert.True(AttendanceDayRules.Classify(new DateOnly(2026, 11, 2), Start, End, Holidays).IsWorkingDay);
            Assert.True(AttendanceDayRules.Classify(new DateOnly(2026, 11, 7), Start, End, Holidays).IsWorkingDay);
        }
    }
}
