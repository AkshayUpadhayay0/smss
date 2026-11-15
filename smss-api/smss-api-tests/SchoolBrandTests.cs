using System.Reflection;
using Microsoft.AspNetCore.Authorization;
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
    /// <summary>GET SchoolRegistration/me/brand: the school's name + public logo path for the avatar, for EVERY role of the school, school from the token only.</summary>
    public class SchoolBrandTests
    {
        [Fact]
        public async Task EveryRole_GetsTheirOwnSchoolsBrand_FromTheToken()
        {
            foreach (var role in new[] { "Super Admin", "School Admin", "Principal", "Teacher", "Accountant", "Admission Staff", "Student", "Guardian", "Driver" })
            {
                string? asked = null;
                var svc = Fake.Of<ISchoolRegistrationService>(new()
                {
                    ["GetMyBrandAsync"] = (Func<object?[], object?>)(a => { asked = (string?)a[0]; return Fake.Ok<object>(new SchoolBrandResponse { SchoolName = "S", LogoUrl = "/uploads/x/logo/logo_1.png" }); })
                });
                var c = Fake.WithUser(new SchoolRegistrationController(svc), Fake.User("SCH1", 7, role));
                Assert.Equal(200, Fake.Code(await c.GetMyBrand()));
                Assert.Equal("SCH1", asked);
            }
        }

        [Fact]
        public void Endpoint_TakesNoSchoolFromTheClient_AndIsNotAnonymous()
        {
            var m = typeof(SchoolRegistrationController).GetMethod("GetMyBrand")!;
            Assert.Empty(m.GetParameters());
            Assert.NotEmpty(typeof(SchoolRegistrationController).GetCustomAttributes<AuthorizeAttribute>());
            Assert.Empty(m.GetCustomAttributes<AllowAnonymousAttribute>());
        }

        [Fact]
        public void Response_CarriesNothingButNameAndLogo() =>
            Assert.Equal(new[] { "LogoUrl", "SchoolName" }, typeof(SchoolBrandResponse).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray());

        private static SchoolRegistrationService Service(TbSchools? school)
        {
            var repo = Fake.Of<ISchoolRegistrationRepository>(new() { ["GetSchoolByIdAsync"] = school });
            return new SchoolRegistrationService(repo, Fake.Of<IFileStorageService>(), Fake.Of<IEmailService>(), new ConfigurationBuilder().Build(), NullLogger<SchoolRegistrationService>.Instance);
        }

        [Fact]
        public async Task Service_ReturnsOnlyNameAndLogo_400WithoutASchool_404ForAMissingOne()
        {
            var ok = await Service(new TbSchools { SchoolId = "SCH1", SchoolName = "First School", LogoUrl = "/uploads/c/logo/logo_1.png", SchoolGstin = "SECRET" }).GetMyBrandAsync("SCH1");
            Assert.Equal(200, ok.StatusCode);
            var brand = Assert.IsType<SchoolBrandResponse>(ok.Data);
            Assert.Equal("First School", brand.SchoolName);
            Assert.Equal("/uploads/c/logo/logo_1.png", brand.LogoUrl);

            Assert.Equal(400, (await Service(null).GetMyBrandAsync(null)).StatusCode);
            Assert.Equal(400, (await Service(null).GetMyBrandAsync("  ")).StatusCode);
            Assert.Equal(404, (await Service(null).GetMyBrandAsync("SCH1")).StatusCode);
        }
    }
}
