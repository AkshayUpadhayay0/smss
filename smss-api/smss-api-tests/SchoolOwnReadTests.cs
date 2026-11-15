using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using smss_api.Controllers;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>GET SchoolRegistration/me: the whole record only for School Admin / Super Admin; every other role gets the public profile (no GSTIN / PAN / contact persons).</summary>
    public class SchoolOwnReadTests
    {
        public static IEnumerable<object[]> Roles() => new[]
        {
            new object[] { "Super Admin", true }, new object[] { "School Admin", true },
            new object[] { "Principal", false }, new object[] { "Teacher", false }, new object[] { "Accountant", false }, new object[] { "Admission Staff", false },
            new object[] { "Student", false }, new object[] { "Guardian", false }, new object[] { "Driver", false },
        };

        [Theory]
        [MemberData(nameof(Roles))]
        public async Task Controller_AsksForTheFullRecordOnlyForAdmins_AndUsesTheTokenSchool(string role, bool expectFull)
        {
            bool? full = null; string? school = null;
            var svc = Fake.Of<ISchoolRegistrationService>(new()
            {
                ["GetMySchoolAsync"] = (Func<object?[], object?>)(a => { school = (string?)a[0]; full = (bool)a[1]!; return Fake.Ok<object>(new object()); })
            });
            var c = Fake.WithUser(new SchoolRegistrationController(svc), Fake.User("SCH1", 7, role));
            Assert.Equal(200, Fake.Code(await c.GetMySchool()));
            Assert.Equal("SCH1", school);
            Assert.Equal(expectFull, full);
        }

        [Fact]
        public async Task ALoginWithNoRole_AndAnAdminWhoIsAlsoATeacher()
        {
            bool? full = null;
            var svc = Fake.Of<ISchoolRegistrationService>(new() { ["GetMySchoolAsync"] = (Func<object?[], object?>)(a => { full = (bool)a[1]!; return Fake.Ok<object>(new object()); }) });
            await Fake.WithUser(new SchoolRegistrationController(svc), Fake.User("SCH1", 7)).GetMySchool();
            Assert.False(full);
            await Fake.WithUser(new SchoolRegistrationController(svc), Fake.User("SCH1", 7, "Teacher", "School Admin")).GetMySchool();
            Assert.True(full);
        }

        private static SchoolRegistrationService Service() =>
            new(Fake.Of<ISchoolRegistrationRepository>(new()
            {
                ["GetSchoolByIdAsync"] = new TbSchools
                {
                    SchoolId = "SCH1", SchoolCode = "12345678901", SchoolName = "First School", SchoolGstin = "29ABCDE1234F1Z5", SchoolPan = "ABCDE1234F", LogoUrl = "/uploads/c/logo/logo_1.png",
                    AddressLine1 = "1 Main Rd", Email = "info@school.test", MobileNumber = "9876543210", Website = "https://school.test", SchoolStatusId = 1,
                    Contacts = new List<TbSchoolContacts> { new() { ContactName = "Secret Person", Email = "p@x.test" } }
                }
            }), Fake.Of<IFileStorageService>(), Fake.Of<IEmailService>(), new ConfigurationBuilder().Build(), NullLogger<SchoolRegistrationService>.Instance);

        [Fact]
        public async Task NonAdminResponse_HasNameLogoAddressContact_ButNoTaxNumbersOrContactPersons()
        {
            var r = await Service().GetMySchoolAsync("SCH1", full: false);
            var p = Assert.IsType<SchoolPublicProfileResponse>(r.Data);
            Assert.Equal("First School", p.SchoolName);
            Assert.Equal("/uploads/c/logo/logo_1.png", p.LogoUrl);
            Assert.Equal("1 Main Rd", p.AddressLine1);
            Assert.Equal("info@school.test", p.Email);
            Assert.Equal("9876543210", p.MobileNumber);

            var json = System.Text.Json.JsonSerializer.Serialize(r.Data);
            foreach (var secret in new[] { "29ABCDE1234F1Z5", "ABCDE1234F", "Secret Person", "p@x.test" }) Assert.DoesNotContain(secret, json);
            foreach (var name in typeof(SchoolPublicProfileResponse).GetProperties().Select(x => x.Name))
                foreach (var banned in new[] { "Gst", "Pan", "Tax", "Registration", "Contacts", "Status", "Subscription" })
                    Assert.DoesNotContain(banned, name, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task AdminResponse_IsTheFullRecord_AndMissingOrBlankSchoolIsRefused()
        {
            var full = Assert.IsType<SchoolResponse>((await Service().GetMySchoolAsync("SCH1", full: true)).Data);
            Assert.Equal("29ABCDE1234F1Z5", full.SchoolGstin);
            Assert.Single(full.Contacts);
            Assert.Equal(400, (await Service().GetMySchoolAsync(null, false)).StatusCode);
            Assert.Equal(400, (await Service().GetMySchoolAsync(" ", true)).StatusCode);
        }
    }
}
