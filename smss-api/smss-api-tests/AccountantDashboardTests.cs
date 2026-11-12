using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using smss_api.Controllers;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    public class AccountantDashboardTests
    {
        private const string School = "SCH1";

        private static FeeDashboardDto FeeYear(long id, params (int Month, decimal Amount)[] months)
        {
            var today = SchoolClock.Today();
            return new FeeDashboardDto
            {
                AcademicYearId = id, TotalOutstanding = 40000m, OverdueCount = 6, OverdueAmount = 12000m, UpcomingCount = 4, UpcomingAmount = 8000m,
                CollectionByMonth = months.Select(m => new FeeCollectionMonthDto { Month = new DateOnly(today.Year, m.Month, 1), Amount = m.Amount, Label = "m" }).ToList(),
            };
        }

        private static AccountantDashboardService Service(Dictionary<string, object?>? fees = null, Dictionary<string, object?>? repo = null, Dictionary<string, object?>? years = null)
        {
            var today = SchoolClock.Today();
            return new AccountantDashboardService(
                Fake.Of<IFeeReportService>(fees ?? new() { ["GetDashboardAsync"] = Fake.Ok(FeeYear(7, (today.Month, 15000m))) }),
                Fake.Of<IAccountantDashboardRepository>(repo ?? new()
                {
                    ["GetCollectedBetweenAsync"] = 2500m,
                    ["GetYearContainingAsync"] = new AccountantYearRow(7, "2026-27"),
                    ["GetRecentPaymentsAsync"] = Enumerable.Range(1, 10).Select(i => new AccountantPaymentRow(i, "RCP-2026-000" + i, "S" + i, "Student " + i, 100m * i, "Cash", DateTime.UtcNow.AddHours(-i))).ToList(),
                }),
                Fake.Of<ISchoolDashboardRepository>(years ?? new() { ["GetYearsOverlappingAsync"] = new List<SchoolYearSpanRow>() }),
                NullLogger<AccountantDashboardService>.Instance);
        }

        [Fact]
        public async Task The_figures_equal_the_fee_dashboard_and_the_payment_queries()
        {
            var d = (await Service().GetSummaryAsync(School, default)).Data!;
            Assert.Empty(d.PartsUnavailable);
            Assert.Equal(2500m, d.Stats.CollectedToday);
            Assert.Equal(15000m, d.Stats.CollectedThisMonth);
            Assert.Equal((40000m, 6, 12000m), (d.Stats.Outstanding, d.Stats.OverdueCount, d.Stats.OverdueAmount));
            Assert.Equal((4, 8000m, 7), (d.Stats.UpcomingCount, d.Stats.UpcomingAmount, d.Stats.UpcomingDays));
            Assert.Equal(("2026-27", 7L), (d.AcademicYearName, d.AcademicYearId!.Value));
            Assert.Equal(10, d.RecentPayments.Count);
            Assert.Equal("RCP-2026-0001", d.RecentPayments[0].ReceiptNumber);
        }

        [Fact]
        public async Task The_chart_is_exactly_six_months_oldest_first_this_month_last_and_zero_filled()
        {
            var today = SchoolClock.Today();
            var d = (await Service().GetSummaryAsync(School, default)).Data!;
            Assert.Equal(6, d.CollectionTrend.Count);
            Assert.Equal(new DateOnly(today.Year, today.Month, 1), d.CollectionTrend[^1].Month);
            Assert.True(d.CollectionTrend.Zip(d.CollectionTrend.Skip(1), (a, b) => a.Month < b.Month).All(x => x));
            Assert.Equal(15000m, d.CollectionTrend[^1].Amount);
            Assert.Equal(0m, d.CollectionTrend[0].Amount);
            Assert.Equal(d.Stats.CollectedThisMonth, d.CollectionTrend[^1].Amount);   // the card and the chart can never disagree
        }

        [Fact]
        public async Task A_six_month_window_that_crosses_an_academic_year_adds_the_previous_years_payments()
        {
            var today = SchoolClock.Today();
            var prevMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
            var current = FeeYear(7, (today.Month, 1000m));
            var previous = new FeeDashboardDto { AcademicYearId = 6, CollectionByMonth = new() { new FeeCollectionMonthDto { Month = prevMonth, Amount = 700m, Label = "p" } } };
            var d = (await Service(
                fees: new() { ["GetDashboardAsync"] = new Func<object?[], object?>(a => Fake.Ok((long)a[1]! == 7 ? current : previous)) },
                years: new() { ["GetYearsOverlappingAsync"] = new List<SchoolYearSpanRow> { new(6, prevMonth.AddMonths(-9), prevMonth.AddDays(10)), new(7, prevMonth.AddDays(11), prevMonth.AddMonths(11)) } }).GetSummaryAsync(School, default)).Data!;
            Assert.Equal(700m, d.CollectionTrend[^2].Amount);
            Assert.Equal(1000m, d.CollectionTrend[^1].Amount);
        }

        [Fact]
        public async Task A_failing_fee_dashboard_marks_fees_unavailable_and_leaves_the_fee_numbers_null()
        {
            var d = (await Service(fees: new()).GetSummaryAsync(School, default)).Data!;
            Assert.Equal(new[] { "fees" }, d.PartsUnavailable);
            Assert.Null(d.Stats.CollectedThisMonth); Assert.Null(d.Stats.Outstanding); Assert.Null(d.Stats.OverdueCount); Assert.Null(d.Stats.UpcomingCount);
            Assert.Empty(d.CollectionTrend);
            Assert.Equal(2500m, d.Stats.CollectedToday);   // independent parts still show
            Assert.Equal(10, d.RecentPayments.Count);
        }

        [Fact]
        public async Task A_throwing_payment_query_only_marks_its_own_part()
        {
            var today = SchoolClock.Today();
            var d = (await Service(repo: new()
            {
                ["GetCollectedBetweenAsync"] = new InvalidOperationException("boom"), ["GetRecentPaymentsAsync"] = new InvalidOperationException("boom"),
                ["GetYearContainingAsync"] = new AccountantYearRow(7, "2026-27"),
            }).GetSummaryAsync(School, default)).Data!;
            Assert.Equal(new[] { "collectedToday", "payments" }, d.PartsUnavailable.OrderBy(x => x));
            Assert.Null(d.Stats.CollectedToday);
            Assert.Empty(d.RecentPayments);
            Assert.Equal(40000m, d.Stats.Outstanding);
        }

        [Fact]
        public async Task No_academic_year_for_today_makes_fees_unavailable_but_today_and_payments_still_show()
        {
            var d = (await Service(repo: new() { ["GetCollectedBetweenAsync"] = 0m, ["GetRecentPaymentsAsync"] = new List<AccountantPaymentRow>() }).GetSummaryAsync(School, default)).Data!;
            Assert.Equal(new[] { "fees" }, d.PartsUnavailable);
            Assert.Equal(0m, d.Stats.CollectedToday);
        }

        [Fact]
        public async Task A_day_with_no_payments_is_zero_collected_and_an_empty_list_not_unavailable()
        {
            var d = (await Service(repo: new()
            {
                ["GetCollectedBetweenAsync"] = 0m, ["GetRecentPaymentsAsync"] = new List<AccountantPaymentRow>(), ["GetYearContainingAsync"] = new AccountantYearRow(7, "2026-27"),
            }).GetSummaryAsync(School, default)).Data!;
            Assert.Empty(d.PartsUnavailable);
            Assert.Equal(0m, d.Stats.CollectedToday);
            Assert.Empty(d.RecentPayments);
        }

        [Fact]
        public async Task Today_is_the_india_calendar_day()
        {
            DateTime? from = null, to = null;
            var svc = Service(repo: new()
            {
                ["GetCollectedBetweenAsync"] = new Func<object?[], object?>(a => { from = (DateTime)a[1]!; to = (DateTime)a[2]!; return 0m; }),
                ["GetYearContainingAsync"] = new AccountantYearRow(7, "y"), ["GetRecentPaymentsAsync"] = new List<AccountantPaymentRow>(),
            });
            await svc.GetSummaryAsync(School, default);
            var today = SchoolClock.Today();
            Assert.Equal(SchoolClock.StartOfDayUtc(today), from);
            Assert.Equal(SchoolClock.StartOfDayUtc(today.AddDays(1)), to);
        }

        [Fact]
        public async Task The_school_comes_from_the_caller_and_a_missing_school_is_400()
        {
            string? seen = null;
            await Service(repo: new()
            {
                ["GetCollectedBetweenAsync"] = new Func<object?[], object?>(a => { seen = (string)a[0]!; return 0m; }),
                ["GetYearContainingAsync"] = new AccountantYearRow(7, "y"), ["GetRecentPaymentsAsync"] = new List<AccountantPaymentRow>(),
            }).GetSummaryAsync("SCH-X", default);
            Assert.Equal("SCH-X", seen);
            Assert.Equal(400, (await Service().GetSummaryAsync(null, default)).StatusCode);
        }

        // ---- authorization matrix ----

        private static (AccountantDashboardController C, Fake.Proxy Service) Controller(System.Security.Claims.ClaimsPrincipal user)
        {
            var svc = Fake.Of<IAccountantDashboardService>(new() { ["GetSummaryAsync"] = Fake.Ok(new AccountantDashboardDto()) });
            return (Fake.WithUser(new AccountantDashboardController(svc), user), Fake.ProxyOf(svc));
        }

        [Theory]
        [InlineData("Super Admin", 403)] [InlineData("School Admin", 403)] [InlineData("Principal", 403)] [InlineData("Teacher", 403)]
        [InlineData("Accountant", 200)] [InlineData("Admission Staff", 403)] [InlineData("Student", 403)] [InlineData("Guardian", 403)]
        public async Task Only_the_Accountant_gets_the_dashboard(string role, int expected)
        {
            var (c, svc) = Controller(Fake.User(School, 5, role));
            Assert.Equal(expected, Fake.Code(await c.Summary(default)));
            Assert.Equal(expected == 200, svc.Calls.Contains("GetSummaryAsync"));
        }

        [Fact]
        public async Task A_user_with_no_role_is_refused()
        {
            Assert.Equal(403, Fake.Code(await Controller(Fake.User(School, 5)).C.Summary(default)));
        }

        [Fact]
        public void Anonymous_callers_get_401_from_the_authorize_filter()
        {
            Assert.NotNull(typeof(AccountantDashboardController).GetCustomAttributes(typeof(AuthorizeAttribute), true).FirstOrDefault());
            Assert.Empty(typeof(AccountantDashboardController).GetMethod(nameof(AccountantDashboardController.Summary))!.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        }
    }
}
