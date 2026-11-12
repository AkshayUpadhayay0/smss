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
    public class AdmissionDashboardTests
    {
        private const string School = "SCH1";
        private static DateTime Day(int back) => SchoolClock.Today().AddDays(-back).ToDateTime(TimeOnly.MinValue);

        private static AdmissionResponseDto App(long id, string status, int daysAgo) =>
            new() { AdmissionId = id, ApplicationNumber = "APS26-" + id.ToString("D4"), ApplicantFullName = "Applicant " + id, StatusName = status, ApplicationDate = Day(daysAgo), AppliedClassName = "Class 1" };

        private static List<AdmissionResponseDto> Sample() => new()
        {
            App(1, "APPLICATION_SUBMITTED", 0), App(2, "APPLICATION_SUBMITTED", 3), App(3, "UNDER_REVIEW", 6), App(4, "UNDER_REVIEW", 7), App(5, "APPROVED", 20),
            App(6, "REJECTED", 30), App(7, "ADMITTED", 40), App(8, "ADMITTED", 41), App(9, "ADMITTED", 42), App(10, "CANCELLED", 50), App(11, "APPLICATION_SUBMITTED", 1),
            App(12, "UNDER_REVIEW", 2),
        };

        private static AdmissionDashboardService Service(Dictionary<string, object?>? admissions = null, Dictionary<string, object?>? repo = null) =>
            new(Fake.Of<IAdmissionService>(admissions ?? new() { ["GetAllAsync"] = Fake.Ok(Sample()) }),
                Fake.Of<IAdmissionDashboardRepository>(repo ?? new()
                {
                    ["GetMissingDocumentsAsync"] = new List<AdmissionMissingDocsRow>
                    {
                        new(11, "APS26-0011", "Applicant 11", "APPLICATION_SUBMITTED", Day(1), new() { "Birth Certificate" }),
                        new(3, "APS26-0003", "Applicant 3", "UNDER_REVIEW", Day(6), new() { "Birth Certificate", "Transfer Certificate" }),
                    }
                }),
                NullLogger<AdmissionDashboardService>.Instance);

        [Fact]
        public async Task The_status_counts_list_all_six_in_pipeline_order()
        {
            var d = (await Service().GetSummaryAsync(School, default)).Data!;
            Assert.Empty(d.PartsUnavailable);
            Assert.Equal(new[] { "APPLICATION_SUBMITTED", "UNDER_REVIEW", "APPROVED", "ADMITTED", "REJECTED", "CANCELLED" }, d.ByStatus.Select(s => s.StatusName));
            Assert.Equal(new[] { 3, 3, 1, 3, 1, 1 }, d.ByStatus.Select(s => s.Count));
            Assert.Equal(12, d.TotalApplications);
            Assert.Equal(d.TotalApplications, d.ByStatus.Sum(s => s.Count));   // the six add up to the total
        }

        [Fact]
        public async Task A_status_nobody_is_in_is_zero_not_missing()
        {
            var d = (await Service(new() { ["GetAllAsync"] = Fake.Ok(new List<AdmissionResponseDto> { App(1, "APPROVED", 0) }) }).GetSummaryAsync(School, default)).Data!;
            Assert.Equal(6, d.ByStatus.Count);
            Assert.Equal(1, d.ByStatus.Single(s => s.StatusName == "APPROVED").Count);
            Assert.Equal(0, d.ByStatus.Single(s => s.StatusName == "UNDER_REVIEW").Count);
        }

        [Fact]
        public async Task New_this_week_is_the_last_seven_days_including_today()
        {
            var d = (await Service().GetSummaryAsync(School, default)).Data!;
            // applications dated 0, 1, 2, 3 and 6 days ago are in; 7 days ago and older are out
            Assert.Equal(5, d.NewThisWeek);
        }

        [Fact]
        public void The_week_boundary_is_exact()
        {
            var today = SchoolClock.Today();
            var apps = new[] { App(1, "APPROVED", 6), App(2, "APPROVED", 7), App(3, "APPROVED", 0) };
            Assert.Equal(2, AdmissionDashboardService.NewThisWeek(apps, today));
            Assert.Equal(0, AdmissionDashboardService.NewThisWeek(Array.Empty<AdmissionResponseDto>(), today));
        }

        [Fact]
        public async Task The_last_ten_applications_come_newest_first()
        {
            var many = Enumerable.Range(1, 15).Select(i => App(i, "APPLICATION_SUBMITTED", 15 - i)).ToList();
            var d = (await Service(new() { ["GetAllAsync"] = Fake.Ok(many) }).GetSummaryAsync(School, default)).Data!;
            Assert.Equal(10, d.RecentApplications.Count);
            Assert.Equal(15, d.RecentApplications[0].AdmissionId);
            Assert.Equal(6, d.RecentApplications[^1].AdmissionId);
        }

        [Fact]
        public async Task Missing_documents_are_counted_and_listed_with_what_is_missing()
        {
            var d = (await Service().GetSummaryAsync(School, default)).Data!;
            Assert.Equal(2, d.MissingDocumentsCount);
            Assert.Equal(new long[] { 11, 3 }, d.MissingDocuments.Select(m => m.AdmissionId));
            Assert.Equal(new[] { "Birth Certificate", "Transfer Certificate" }, d.MissingDocuments[1].MissingDocuments);
        }

        [Fact]
        public async Task Only_open_applications_are_asked_about_documents()
        {
            IReadOnlyCollection<string>? asked = null;
            await Service(repo: new() { ["GetMissingDocumentsAsync"] = new Func<object?[], object?>(a => { asked = (IReadOnlyCollection<string>)a[1]!; return new List<AdmissionMissingDocsRow>(); }) }).GetSummaryAsync(School, default);
            Assert.Equal(new[] { "APPLICATION_SUBMITTED", "UNDER_REVIEW", "APPROVED" }, asked);   // rejected, withdrawn and admitted applications are frozen
        }

        [Fact]
        public async Task The_list_failing_marks_applications_unavailable_but_documents_still_show()
        {
            var d = (await Service(admissions: new()).GetSummaryAsync(School, default)).Data!;
            Assert.Equal(new[] { "applications" }, d.PartsUnavailable);
            Assert.Null(d.TotalApplications); Assert.Null(d.NewThisWeek);
            Assert.Empty(d.ByStatus); Assert.Empty(d.RecentApplications);
            Assert.Equal(2, d.MissingDocumentsCount);
        }

        [Fact]
        public async Task A_throwing_document_query_marks_documents_unavailable_not_zero()
        {
            var d = (await Service(repo: new() { ["GetMissingDocumentsAsync"] = new InvalidOperationException("boom") }).GetSummaryAsync(School, default)).Data!;
            Assert.Equal(new[] { "documents" }, d.PartsUnavailable);
            Assert.Null(d.MissingDocumentsCount);
            Assert.Equal(12, d.TotalApplications);
        }

        [Fact]
        public async Task No_applications_is_empty_not_unavailable()
        {
            var d = (await Service(new() { ["GetAllAsync"] = Fake.Ok(new List<AdmissionResponseDto>()) }, new() { ["GetMissingDocumentsAsync"] = new List<AdmissionMissingDocsRow>() }).GetSummaryAsync(School, default)).Data!;
            Assert.Empty(d.PartsUnavailable);
            Assert.Equal((0, 0, 0), (d.TotalApplications, d.NewThisWeek, d.MissingDocumentsCount));
            Assert.Equal(6, d.ByStatus.Count);
            Assert.All(d.ByStatus, s => Assert.Equal(0, s.Count));
            Assert.Empty(d.RecentApplications); Assert.Empty(d.MissingDocuments);
        }

        [Fact]
        public async Task The_school_comes_from_the_caller_and_a_missing_school_is_400()
        {
            string? seen = null;
            await Service(new() { ["GetAllAsync"] = new Func<object?[], object?>(a => { seen = (string)a[0]!; return Fake.Ok(new List<AdmissionResponseDto>()); }) }).GetSummaryAsync("SCH-X", default);
            Assert.Equal("SCH-X", seen);
            Assert.Equal(400, (await Service().GetSummaryAsync(null, default)).StatusCode);
        }

        // ---- authorization matrix ----

        private static (AdmissionDashboardController C, Fake.Proxy Service) Controller(System.Security.Claims.ClaimsPrincipal user)
        {
            var svc = Fake.Of<IAdmissionDashboardService>(new() { ["GetSummaryAsync"] = Fake.Ok(new AdmissionDashboardDto()) });
            return (Fake.WithUser(new AdmissionDashboardController(svc), user), Fake.ProxyOf(svc));
        }

        [Theory]
        [InlineData("Super Admin", 403)] [InlineData("School Admin", 403)] [InlineData("Principal", 403)] [InlineData("Teacher", 403)]
        [InlineData("Accountant", 403)] [InlineData("Admission Staff", 200)] [InlineData("Student", 403)] [InlineData("Guardian", 403)]
        public async Task Only_Admission_Staff_get_the_dashboard(string role, int expected)
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
            Assert.NotNull(typeof(AdmissionDashboardController).GetCustomAttributes(typeof(AuthorizeAttribute), true).FirstOrDefault());
            Assert.Empty(typeof(AdmissionDashboardController).GetMethod(nameof(AdmissionDashboardController.Summary))!.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        }
    }
}
