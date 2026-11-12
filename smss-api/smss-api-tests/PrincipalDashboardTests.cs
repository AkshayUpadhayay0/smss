using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using smss_api.Controllers;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    public class PrincipalDashboardTests
    {
        private const string School = "SCH1";

        private static PrincipalDashboardService Service(Dictionary<string, object?>? attendance = null, Dictionary<string, object?>? employees = null, Dictionary<string, object?>? fees = null,
            Dictionary<string, object?>? leave = null, Dictionary<string, object?>? admissions = null, Dictionary<string, object?>? results = null, Dictionary<string, object?>? events = null,
            Dictionary<string, object?>? notices = null, Dictionary<string, object?>? repo = null)
        {
            var today = smss_api_service_layer.helper.SchoolClock.Today();
            return new PrincipalDashboardService(
                Fake.Of<IAttendanceService>(attendance ?? new() { ["GetMySectionsAsync"] = Fake.Ok(new MySectionsDto { AcademicYearId = 1, AcademicYearName = "2026-27", Sections = new() { new() { RosterCount = 30 }, new() { RosterCount = 25 } } }) }),
                Fake.Of<IFeeReportService>(fees ?? new() { ["GetDashboardAsync"] = Fake.Ok(new FeeDashboardDto { TotalDue = 100000m, TotalCollected = 60000m, TotalOutstanding = 40000m }) }),
                Fake.Of<IEmployeeService>(employees ?? new() { ["ListAsync"] = Fake.Ok(new EmployeePagedDto { Total = 12 }) }),
                Fake.Of<IEmployeeLeaveService>(leave ?? new() { ["ListAsync"] = Fake.Ok(new List<EmployeeLeaveDto> { new(), new(), new() }) }),
                Fake.Of<IAdmissionService>(admissions ?? new()
                {
                    ["GetAllAsync"] = Fake.Ok(new List<AdmissionResponseDto>
                    {
                        new() { StatusName = "UNDER_REVIEW" }, new() { StatusName = "UNDER_REVIEW" }, new() { StatusName = "APPLICATION_SUBMITTED" }, new() { StatusName = "APPROVED" }, new() { StatusName = "REJECTED" },
                    })
                }),
                Fake.Of<IReportResultService>(results ?? new()
                {
                    ["ListAsync"] = Fake.Ok(Enumerable.Range(1, 7).Select(i => new PublicationDto { PublicationId = i, ClassName = "Class " + i, SectionName = "A", Scope = "TERM", TermName = "Term 1", Version = 1, Status = "PUBLISHED", PublishedAt = new DateTime(2026, 10, i, 0, 0, 0, DateTimeKind.Utc), StudentCount = 30 }).ToList())
                }),
                Fake.Of<ISchoolEventService>(events ?? new() { ["GetUpcomingAsync"] = Fake.Ok(new List<SchoolEventDto> { new() { EventId = 1, EventTitle = "PTM" }, new() { EventId = 2, EventTitle = "Sports day" } }) }),
                Fake.Of<INoticeService>(notices ?? new()
                {
                    ["ListAsync"] = Fake.Ok(Enumerable.Range(1, 8).Select(i => new NoticeDto { NoticeId = i, Title = "N" + i, Audience = "ALL", PublishedAt = new DateTime(2026, 10, i, 0, 0, 0, DateTimeKind.Utc) }).ToList())
                }),
                Fake.Of<ISchoolDashboardRepository>(repo ?? new() { ["GetAttendanceByDayAsync"] = new List<SchoolAttendanceDayRow> { new(today, 50, 45), new(today.AddDays(-1), 40, 30) } }),
                NullLogger<PrincipalDashboardService>.Instance);
        }

        [Fact]
        public async Task The_figures_come_from_the_existing_services()
        {
            var r = await Service().GetSummaryAsync(School, 5, default);
            Assert.Equal(200, r.StatusCode);
            var d = r.Data!;
            Assert.Empty(d.PartsUnavailable);
            Assert.Equal((55, 12), (d.Stats.TotalStudents, d.Stats.TotalStaff));
            Assert.Equal((50, 45, 90.0m), (d.Stats.AttendanceMarkedToday, d.Stats.AttendancePresentToday, d.Stats.AttendancePercentToday));
            Assert.Equal((100000m, 60000m, 40000m), (d.Stats.FeeTotalDue, d.Stats.FeeCollected, d.Stats.FeeOutstanding));
            Assert.Equal(3, d.Alerts.PendingEmployeeLeaves);
            Assert.Equal((2, 1), (d.Alerts.AdmissionsToDecide, d.Alerts.AdmissionsAwaitingReview));
            Assert.Equal(PrincipalDashboardService.AttendanceTrendDays, d.AttendanceTrend.Count);
            Assert.Equal(90.0m, d.AttendanceTrend[^1].PresentPercent);
            Assert.Equal(75.0m, d.AttendanceTrend[^2].PresentPercent);
            Assert.Equal(new long?[] { 1, 1 }, new long?[] { d.AcademicYearId, 1 });
        }

        [Fact]
        public async Task The_latest_five_results_and_notices_come_newest_first()
        {
            var d = (await Service().GetSummaryAsync(School, 5, default)).Data!;
            Assert.Equal(new long[] { 7, 6, 5, 4, 3 }, d.RecentPublications.Select(p => p.PublicationId));
            Assert.Equal(new long[] { 8, 7, 6, 5, 4 }, d.RecentNotices.Select(n => n.NoticeId));
            Assert.Equal(2, d.UpcomingEvents.Count);
        }

        [Fact]
        public async Task A_service_that_throws_only_marks_its_own_part()
        {
            var d = (await Service(fees: new() { ["GetDashboardAsync"] = new InvalidOperationException("db down") }).GetSummaryAsync(School, 5, default)).Data!;
            Assert.Equal(new[] { "fees" }, d.PartsUnavailable);
            Assert.Null(d.Stats.FeeCollected);
            Assert.Equal(55, d.Stats.TotalStudents);
        }

        [Fact]
        public async Task A_failed_response_from_one_service_only_marks_that_part()
        {
            var fail = new Dictionary<string, object?>();   // nothing configured: the stand-in answers with a failed ApiResponse
            var d = (await Service(fees: fail, leave: fail, results: fail).GetSummaryAsync(School, 5, default)).Data!;
            Assert.Equal(new[] { "fees", "employeeLeave", "results" }.OrderBy(x => x), d.PartsUnavailable.OrderBy(x => x));
            Assert.Null(d.Stats.FeeCollected); Assert.Null(d.Stats.FeeOutstanding); Assert.Null(d.Alerts.PendingEmployeeLeaves);
            Assert.Empty(d.RecentPublications);
            Assert.Equal(55, d.Stats.TotalStudents);   // everything else still shows
            Assert.Equal(12, d.Stats.TotalStaff);
        }

        [Fact]
        public async Task A_failing_attendance_query_marks_attendance_unavailable_and_blanks_its_numbers()
        {
            var d = (await Service(repo: new() { ["GetAttendanceByDayAsync"] = new InvalidOperationException("boom") }).GetSummaryAsync(School, 5, default)).Data!;
            Assert.Contains("attendance", d.PartsUnavailable);
            Assert.Null(d.Stats.AttendancePercentToday); Assert.Null(d.Stats.AttendanceMarkedToday);
            Assert.Empty(d.AttendanceTrend);
            Assert.Equal(12, d.Stats.TotalStaff);
        }

        [Fact]
        public async Task Empty_categories_are_empty_not_unavailable()
        {
            var empty = Service(
                leave: new() { ["ListAsync"] = Fake.Ok(new List<EmployeeLeaveDto>()) }, admissions: new() { ["GetAllAsync"] = Fake.Ok(new List<AdmissionResponseDto>()) },
                results: new() { ["ListAsync"] = Fake.Ok(new List<PublicationDto>()) }, events: new() { ["GetUpcomingAsync"] = Fake.Ok(new List<SchoolEventDto>()) },
                notices: new() { ["ListAsync"] = Fake.Ok(new List<NoticeDto>()) }, repo: new() { ["GetAttendanceByDayAsync"] = new List<SchoolAttendanceDayRow>() });
            var d = (await empty.GetSummaryAsync(School, 5, default)).Data!;
            Assert.Empty(d.PartsUnavailable);
            Assert.Equal((0, 0, 0), (d.Alerts.PendingEmployeeLeaves, d.Alerts.AdmissionsToDecide, d.Alerts.AdmissionsAwaitingReview));
            Assert.Empty(d.RecentPublications); Assert.Empty(d.UpcomingEvents); Assert.Empty(d.RecentNotices);
            Assert.Equal(0, d.Stats.AttendanceMarkedToday);
            Assert.Null(d.Stats.AttendancePercentToday);   // nothing marked yet today: no percentage, not 0 %
            Assert.Equal(14, d.AttendanceTrend.Count);
        }

        [Fact]
        public async Task No_academic_year_for_today_makes_the_students_and_fees_unavailable()
        {
            var d = (await Service(attendance: new()).GetSummaryAsync(School, 5, default)).Data!;
            Assert.Contains("students", d.PartsUnavailable); Assert.Contains("fees", d.PartsUnavailable);
            Assert.Null(d.Stats.TotalStudents); Assert.Null(d.Stats.FeeCollected);
        }

        [Fact]
        public async Task A_caller_without_a_school_gets_400()
        {
            Assert.Equal(400, (await Service().GetSummaryAsync(null, 5, default)).StatusCode);
            Assert.Equal(400, (await Service().GetSummaryAsync("  ", 5, default)).StatusCode);
        }

        [Fact]
        public async Task The_school_comes_from_the_caller_only_and_the_fee_dashboard_is_asked_read_only()
        {
            string? seenSchool = null; bool? canManage = null;
            var svc = Service(fees: new() { ["GetDashboardAsync"] = new Func<object?[], object?>(a => { seenSchool = (string?)a[0]; canManage = (bool)a[5]!; return Fake.Ok(new FeeDashboardDto()); }) });
            await svc.GetSummaryAsync("SCH-X", 5, default);
            Assert.Equal("SCH-X", seenSchool);
            Assert.False(canManage);
        }

        // ---- authorization matrix: every role against the endpoint ----

        private static (PrincipalDashboardController C, Fake.Proxy Service) Controller(System.Security.Claims.ClaimsPrincipal user)
        {
            var svc = Fake.Of<IPrincipalDashboardService>(new() { ["GetSummaryAsync"] = Fake.Ok(new PrincipalDashboardDto()) });
            return (Fake.WithUser(new PrincipalDashboardController(svc), user), Fake.ProxyOf(svc));
        }

        [Theory]
        [InlineData("Super Admin", 403)] [InlineData("School Admin", 403)] [InlineData("Principal", 200)] [InlineData("Teacher", 403)]
        [InlineData("Accountant", 403)] [InlineData("Admission Staff", 403)] [InlineData("Student", 403)] [InlineData("Guardian", 403)]
        public async Task Only_the_Principal_gets_the_dashboard(string role, int expected)
        {
            var (c, svc) = Controller(Fake.User(School, 5, role));
            Assert.Equal(expected, Fake.Code(await c.Summary(default)));
            Assert.Equal(expected == 200, svc.Calls.Contains("GetSummaryAsync"));   // a refused role never reaches the service
        }

        [Fact]
        public async Task A_principal_who_is_also_something_else_still_gets_it_and_a_user_with_no_role_does_not()
        {
            Assert.Equal(200, Fake.Code(await Controller(Fake.User(School, 5, "Principal", "Teacher")).C.Summary(default)));
            Assert.Equal(403, Fake.Code(await Controller(Fake.User(School, 5)).C.Summary(default)));
        }

        [Fact]
        public async Task A_token_without_a_user_id_is_unauthorized()
        {
            var user = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Principal") }, "test"));
            Assert.Equal(401, Fake.Code(await Controller(user).C.Summary(default)));
        }

        [Fact]
        public void Anonymous_callers_get_401_from_the_authorize_filter()
        {
            Assert.NotNull(typeof(PrincipalDashboardController).GetCustomAttributes(typeof(AuthorizeAttribute), true).FirstOrDefault());
            Assert.Empty(typeof(PrincipalDashboardController).GetMethod(nameof(PrincipalDashboardController.Summary))!.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        }
    }
}
