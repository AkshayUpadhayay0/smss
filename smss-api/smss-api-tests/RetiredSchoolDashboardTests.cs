using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Stage 1c retired the old School dashboard (GET api/SchoolDashboard/summary, its service and DTOs). These tests keep it retired, and cover the three helpers that moved
    /// beside the dashboards that use them (they had no tests while they lived in the retired service).
    /// </summary>
    public class RetiredSchoolDashboardTests
    {
        private static IEnumerable<Type> AllTypes() =>
            new[] { typeof(smss_api.Controllers.DashboardController).Assembly, typeof(DashboardTrends).Assembly, typeof(ISchoolDashboardRepository).Assembly }.Distinct().SelectMany(a => a.GetTypes());

        [Theory]
        [InlineData("SchoolDashboardController")]
        [InlineData("ISchoolDashboardService")]
        [InlineData("SchoolDashboardService")]
        [InlineData("SchoolDashboardDto")]
        [InlineData("SchoolDashboardParts")]
        public void The_retired_types_are_gone(string name) => Assert.DoesNotContain(AllTypes(), t => t.Name == name);

        [Fact]
        public void No_controller_answers_on_the_retired_route()
        {
            var routes = typeof(smss_api.Controllers.DashboardController).Assembly.GetTypes()
                .Where(t => typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(t))
                .Select(t => t.Name.Replace("Controller", ""));
            Assert.DoesNotContain("SchoolDashboard", routes);
        }

        [Fact]
        public void The_dashboards_that_remain_are_all_still_there()
        {
            foreach (var n in new[] { "DashboardController", "PrincipalDashboardController", "AccountantDashboardController", "TeacherDashboardController", "StudentDashboardController", "AdmissionDashboardController" })
                Assert.Contains(typeof(smss_api.Controllers.DashboardController).Assembly.GetTypes(), t => t.Name == n);
        }

        // ---- the helpers that moved
        [Fact]
        public void Present_percent_is_one_decimal_and_null_when_nothing_is_marked()
        {
            Assert.Equal(66.7m, DashboardTrends.PresentPercent(2, 3));
            Assert.Equal(100m, DashboardTrends.PresentPercent(5, 5));
            Assert.Null(DashboardTrends.PresentPercent(0, 0));
        }

        [Fact]
        public void The_attendance_trend_is_exactly_the_days_asked_for_oldest_first_with_unmarked_days_empty()
        {
            var today = new DateOnly(2026, 11, 24);
            var t = DashboardTrends.AttendanceTrend(today, new[] { new SchoolAttendanceDayRow(today, 10, 9), new SchoolAttendanceDayRow(today.AddDays(-2), 4, 1) });
            Assert.Equal(DashboardTrends.AttendanceTrendDays, t.Count);
            Assert.Equal(today, t[^1].Date);
            Assert.Equal(today.AddDays(-13), t[0].Date);
            Assert.Equal(90.0m, t[^1].PresentPercent);
            Assert.Equal(25.0m, t[^3].PresentPercent);
            Assert.Null(t[^2].PresentPercent);           // nothing marked yesterday: a gap, not 0 %
            Assert.Equal(0, t[^2].Marked);
        }

        [Fact]
        public void The_last_months_end_with_this_month_and_cross_a_year_boundary()
        {
            var m = DashboardTrends.LastMonths(new DateOnly(2027, 2, 17));
            Assert.Equal(DashboardTrends.FeeTrendMonths, m.Count);
            Assert.Equal(new DateOnly(2026, 9, 1), m[0]);
            Assert.Equal(new DateOnly(2027, 2, 1), m[^1]);
        }

        [Fact]
        public void The_fee_trend_adds_up_every_years_dashboard_and_zero_fills_the_empty_months()
        {
            var months = DashboardTrends.LastMonths(new DateOnly(2027, 2, 17));
            var y1 = new FeeDashboardDto { CollectionByMonth = new() { new FeeCollectionMonthDto { Month = new DateOnly(2026, 12, 1), Amount = 700m }, new FeeCollectionMonthDto { Month = new DateOnly(2027, 2, 1), Amount = 100m } } };
            var y2 = new FeeDashboardDto { CollectionByMonth = new() { new FeeCollectionMonthDto { Month = new DateOnly(2027, 2, 1), Amount = 50m } } };
            var trend = DashboardTrends.FeeTrend(months, new[] { y1, y2 });
            Assert.Equal(months.Count, trend.Count);
            Assert.Equal(700m, trend.Single(x => x.Month == new DateOnly(2026, 12, 1)).Amount);
            Assert.Equal(150m, trend[^1].Amount);                                  // the same month in two years' dashboards
            Assert.Equal("Feb 2027", trend[^1].Label);
            Assert.Equal(0m, trend[0].Amount);
        }
    }
}
