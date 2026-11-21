using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using smss_api.Controllers;
using smss_api.Helpers;
using smss_api_db_layer.@interface;
using smss_api_db_layer.entity;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.service;
using smss_api_service_layer.@interface;
using Xunit;

namespace smss_api_tests
{
    /// <summary>Public "Our Partner Schools" list + the Super Admin website-visibility switch.</summary>
    public class PartnerSchoolTests
    {
        private sealed class FakeRepo : IPartnerSchoolRepository
        {
            public List<PartnerSchoolRow> Rows = new();
            public int ListCalls;
            public int? LastMax;
            public (string Id, bool Show)? Set;
            public bool Exists = true;
            public bool Platform;
            public Exception? Throw;

            public Task<List<PartnerSchoolRow>> GetListedAsync(string activeName, string statusType, int max, CancellationToken ct)
            {
                ListCalls++;
                LastMax = max;
                if (Throw != null) throw Throw;
                return Task.FromResult(Rows.Take(max).ToList());
            }

            public Task<bool> IsPlatformSchoolAsync(string schoolId, CancellationToken ct) => Task.FromResult(Platform);

            public Task<bool> SetShowOnWebsiteAsync(string schoolId, bool show, DateTime nowUtc, CancellationToken ct)
            {
                if (!Exists) return Task.FromResult(false);
                Set = (schoolId, show);
                return Task.FromResult(true);
            }
        }

        private static IConfiguration Config(int? max = null, int? seconds = null)
        {
            var d = new Dictionary<string, string?>();
            if (max != null) d["PartnerSchools:MaxItems"] = max.ToString();
            if (seconds != null) d["PartnerSchools:CacheSeconds"] = seconds.ToString();
            return new ConfigurationBuilder().AddInMemoryCollection(d).Build();
        }

        private static PartnerSchoolService Svc(FakeRepo repo, IConfiguration? cfg = null, IMemoryCache? cache = null)
        {
            cache ??= new MemoryCache(new MemoryCacheOptions());
            return new(repo, cache, new PartnerSchoolCache(cache), cfg ?? Config(), NullLogger<PartnerSchoolService>.Instance);
        }

        private static PartnerSchoolRow Row(string n) => new(n, "/uploads/x/logo/logo_1.png", "Dehradun", "Uttarakhand");

        // ------------------------------------------------------------ what the public sees
        [Fact]
        public void PublicDto_HasOnlyNameLogoCityState_NothingInternal()
        {
            var names = typeof(PartnerSchoolDto).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "City", "LogoUrl", "Name", "State" }, names);
            var rowNames = typeof(PartnerSchoolRow).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "City", "LogoUrl", "Name", "State" }, rowNames);
        }

        [Fact]
        public async Task PublicList_SerialisesNoForbiddenField()
        {
            var repo = new FakeRepo { Rows = { Row("Alpha School") } };
            var r = await Svc(repo).GetPublicListAsync(default);
            var json = System.Text.Json.JsonSerializer.Serialize(r, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }).ToLowerInvariant();
            foreach (var forbidden in new[] { "schoolid", "schoolcode", "email", "mobile", "phone", "contact", "statusid", "gstin", "pan\"", "pincode", "address", "showonwebsite" })
                Assert.DoesNotContain(forbidden, json);
            Assert.Contains("alpha school", json);
        }

        [Fact]
        public void PublicEndpoint_IsAnonymous_ReadOnly_TakesNoInput_AndRateLimited()
        {
            var t = typeof(PublicPartnerSchoolsController);
            Assert.NotEmpty(t.GetCustomAttributes<AllowAnonymousAttribute>());
            var list = t.GetMethod("List")!;
            Assert.NotNull(list.GetCustomAttribute<HttpGetAttribute>());
            Assert.Equal(new[] { typeof(CancellationToken) }, list.GetParameters().Select(p => p.ParameterType).ToArray());   // no query parameter can pick a school
            Assert.Equal(PublicPartnerSchoolsController.RateLimitPolicy, list.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
            Assert.NotEqual(PublicEnquiriesController.RateLimitPolicy, PublicPartnerSchoolsController.RateLimitPolicy);   // its own, separately configured policy
        }

        [Fact]
        public async Task PublicEndpoint_SendsCacheControl_OnSuccess_AndNoStoreOnFailure()
        {
            var ok = new PublicPartnerSchoolsController(Svc(new FakeRepo()), Config(seconds: 120)) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
            Assert.Equal(200, Fake.Code(await ok.List(default)));
            Assert.Equal("public, max-age=120", ok.Response.Headers.CacheControl.ToString());

            var bad = new PublicPartnerSchoolsController(Svc(new FakeRepo { Throw = new InvalidOperationException("secret db detail") }), Config()) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
            var res = await bad.List(default);
            Assert.Equal(500, Fake.Code(res));
            Assert.Equal("no-store", bad.Response.Headers.CacheControl.ToString());
            var body = (ApiResponse<List<PartnerSchoolDto>>)((ObjectResult)res).Value!;
            Assert.DoesNotContain("secret", body.Message);   // generic message; the detail only goes to the log
        }

        // ------------------------------------------------------------ cap + cache
        [Fact]
        public async Task Cap_ComesFromConfig_AndIsRespected()
        {
            var repo = new FakeRepo { Rows = Enumerable.Range(1, 100).Select(i => Row($"S{i:000}")).ToList() };
            var r = await Svc(repo, Config(max: 7)).GetPublicListAsync(default);
            Assert.Equal(7, repo.LastMax);
            Assert.Equal(7, r.Data!.Count);

            var def = new FakeRepo();
            await Svc(def).GetPublicListAsync(default);
            Assert.Equal(60, def.LastMax);   // the default cap
        }

        [Fact]
        public async Task Answer_IsCached_UntilTheSuperAdminChangesAFlag()
        {
            var repo = new FakeRepo { Rows = { Row("Alpha") } };
            var svc = Svc(repo, Config(seconds: 300));
            await svc.GetPublicListAsync(default);
            await svc.GetPublicListAsync(default);
            Assert.Equal(1, repo.ListCalls);

            var set = await svc.SetVisibilityAsync("sch1", new SetWebsiteVisibilityRequest { Show = false }, default);
            Assert.Equal(200, set.StatusCode);
            await svc.GetPublicListAsync(default);
            Assert.Equal(2, repo.ListCalls);
        }

        [Fact]
        public async Task CacheSecondsZero_MeansNoCaching()
        {
            var repo = new FakeRepo();
            var svc = Svc(repo, Config(seconds: 0));
            await svc.GetPublicListAsync(default);
            await svc.GetPublicListAsync(default);
            Assert.Equal(2, repo.ListCalls);
        }

        [Fact]
        public async Task FailureIsNotCached_AndIsGeneric()
        {
            var repo = new FakeRepo { Throw = new InvalidOperationException("boom") };
            var svc = Svc(repo);
            var r = await svc.GetPublicListAsync(default);
            Assert.False(r.Status);
            Assert.Equal(500, r.StatusCode);
            Assert.DoesNotContain("boom", r.Message);
            repo.Throw = null;
            Assert.True((await svc.GetPublicListAsync(default)).Status);
        }

        // ------------------------------------------------------------ Super Admin switch
        [Fact]
        public async Task SetVisibility_SetsTheFlag_ForTheRouteSchoolOnly()
        {
            var repo = new FakeRepo();
            var r = await Svc(repo).SetVisibilityAsync("sch1", new SetWebsiteVisibilityRequest { Show = true }, default);
            Assert.True(r.Status);
            Assert.Equal(("sch1", true), repo.Set);
            Assert.True(r.Data!.ShowOnWebsite);
        }

        [Fact]
        public async Task SetVisibility_UnknownSchool_Is404_AndMissingShowIs400()
        {
            var svc = Svc(new FakeRepo { Exists = false });
            Assert.Equal(404, (await svc.SetVisibilityAsync("nope", new SetWebsiteVisibilityRequest { Show = true }, default)).StatusCode);
            Assert.Equal(400, (await Svc(new FakeRepo()).SetVisibilityAsync("sch1", new SetWebsiteVisibilityRequest(), default)).StatusCode);
        }

        [Fact]
        public void SetVisibilityEndpoint_IsPost_AuthorizedAndSuperAdminOnly_SchoolFromRouteOnly()
        {
            var t = typeof(SuperAdminSchoolsController);
            Assert.NotEmpty(t.GetCustomAttributes<AuthorizeAttribute>());        // anonymous -> 401
            Assert.NotEmpty(t.GetCustomAttributes<SuperAdminOnlyAttribute>());   // other roles -> 403
            var m = t.GetMethod("SetWebsiteVisibility")!;
            Assert.NotNull(m.GetCustomAttribute<HttpPostAttribute>());
            Assert.Null(m.GetCustomAttribute<HttpPatchAttribute>());
            Assert.Empty(m.GetCustomAttributes<AllowAnonymousAttribute>());
            Assert.DoesNotContain(typeof(SetWebsiteVisibilityRequest).GetProperties(), p => p.Name.Contains("School", StringComparison.OrdinalIgnoreCase));   // the body cannot name a school
        }

        [Fact]
        public void SetVisibilityEndpoint_Refuses403_ToEveryRoleExceptSuperAdmin_AndLetsSuperAdminThrough()
        {
            var attr = typeof(SuperAdminSchoolsController).GetCustomAttribute<SuperAdminOnlyAttribute>()!;
            foreach (var role in Fake.AllRoles.Where(r => r != "Super Admin").Concat(new[] { "Driver" }))
            {
                var ctx = Ctx(Fake.User("SCH1", 7, role));
                attr.OnAuthorization(ctx);
                Assert.True(ctx.Result is ObjectResult { StatusCode: 403 }, $"must refuse {role}");
            }
            var ok = Ctx(Fake.User(null, 1, "Super Admin"));
            attr.OnAuthorization(ok);
            Assert.Null(ok.Result);
        }

        private static AuthorizationFilterContext Ctx(System.Security.Claims.ClaimsPrincipal user) =>
            new(new ActionContext(new DefaultHttpContext { User = user }, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()), new List<IFilterMetadata>());

        // ------------------------------------------------------------ the platform's own school
        [Fact]
        public async Task SetVisibility_RefusesThePlatformSchool_With400_AndWritesNothing()
        {
            var repo = new FakeRepo { Platform = true };
            foreach (var show in new[] { true, false })
            {
                var r = await Svc(repo).SetVisibilityAsync("ANY_ID", new SetWebsiteVisibilityRequest { Show = show }, default);
                Assert.False(r.Status);
                Assert.Equal(400, r.StatusCode);
                Assert.Null(r.Data);
            }
            Assert.Null(repo.Set);
        }

        // ------------------------------------------------------------ the cache is cleared when a school changes
        private static (SchoolRegistrationService Svc, IMemoryCache Cache, TbSchools School) RegistrationService()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            cache.Set(PartnerSchoolCache.CacheKey, new List<PartnerSchoolDto> { new() { Name = "Stale" } });
            var school = new TbSchools { SchoolId = "SCH1", SchoolCode = "C1", SchoolName = "First School", LogoUrl = "/uploads/C1/logo/logo_1.png", SchoolStatusId = 1 };
            var repo = Fake.Of<ISchoolRegistrationRepository>(new()
            {
                ["GetSchoolByIdAsync"] = school,
                ["GetStatusIdByNameAsync"] = (Func<object?[], object?>)(a => (string)a[0]! == "Active" ? (int?)1 : (int?)2),
                ["GetDuplicateFieldsAsync"] = new List<string>(),
            });
            var svc = new SchoolRegistrationService(repo, Fake.Of<IFileStorageService>(), Fake.Of<IEmailService>(), new ConfigurationBuilder().Build(), new PartnerSchoolCache(cache), NullLogger<SchoolRegistrationService>.Instance);
            return (svc, cache, school);
        }

        private static bool Cached(IMemoryCache c) => c.TryGetValue(PartnerSchoolCache.CacheKey, out _);

        [Fact]
        public async Task Deactivating_And_Activating_ASchool_ClearsThePartnerListCache()
        {
            var (svc, cache, school) = RegistrationService();
            Assert.True(Cached(cache));
            Assert.Equal(200, (await svc.ToggleSchoolStatusAsync("SCH1")).StatusCode);   // Active -> Inactive
            Assert.Equal(2, school.SchoolStatusId);
            Assert.False(Cached(cache));

            cache.Set(PartnerSchoolCache.CacheKey, new List<PartnerSchoolDto>());
            Assert.Equal(200, (await svc.ToggleSchoolStatusAsync("SCH1")).StatusCode);   // Inactive -> Active
            Assert.Equal(1, school.SchoolStatusId);
            Assert.False(Cached(cache));
        }

        [Fact]
        public async Task UpdatingASchool_ClearsThePartnerListCache()
        {
            var (svc, cache, _) = RegistrationService();
            var r = await svc.UpdateSchoolAsync("SCH1", new UpdateSchoolRequest { SchoolName = "Renamed School", Contacts = new() });
            Assert.True(r.Status, r.Message);
            Assert.False(Cached(cache));
        }

        [Fact]
        public async Task RemovingTheLogo_ClearsThePartnerListCache_ButANoOpDoesNot()
        {
            var (svc, cache, school) = RegistrationService();
            await svc.RemoveLogoAsync("SCH1");   // (the file delete afterwards is a void call the test double cannot answer; the DB change and the cache clear come first)
            Assert.Null(school.LogoUrl);
            Assert.False(Cached(cache));

            cache.Set(PartnerSchoolCache.CacheKey, new List<PartnerSchoolDto>());
            await svc.RemoveLogoAsync("SCH1");   // already no logo: nothing changed
            Assert.True(Cached(cache));
        }

        // ------------------------------------------------------------ the real query (scratch PostgreSQL only; see DbFact)
        // The template needs tb_schools (with show_on_website), lut_status, lut_state and lut_city: see Docs/smss-partner-schools.sql.
        private static void Seed(TaxDb db, string id, string name, int? status, bool show, string? logo = null, int? city = null)
        {
            var cityCols = city == null ? "" : ", country_id, state_id, district_id, city_id";
            var cityVals = city == null ? "" : ", 91, 5, 50, " + city;   // an existing LGD chain of the template (Uttarakhand / Roorkee)
            db.Exec($"insert into tb_schools (school_id, school_code, school_name, school_status_id, show_on_website, logo_url{cityCols}) " +
                    $"values ('{id}', 'C{id}', '{name}', {(status == null ? "null" : status.ToString())}, {(show ? "true" : "false")}, {(logo == null ? "null" : "'" + logo + "'")}{cityVals})");
        }

        private static (int Active, int Inactive) Statuses(TaxDb db)
        {
            db.Exec("insert into lut_status (sname, stype) select 'Active', 'general status' where not exists (select 1 from lut_status where sname='Active' and stype='general status')");
            db.Exec("insert into lut_status (sname, stype) select 'Inactive', 'general status' where not exists (select 1 from lut_status where sname='Inactive' and stype='general status')");
            return (db.Scalar<int>("select sid from lut_status where sname='Active' and stype='general status'"), db.Scalar<int>("select sid from lut_status where sname='Inactive' and stype='general status'"));
        }

        [DbFact]
        public async Task Query_ReturnsOnlyActiveAndOptedInSchools_ByName_WithCityAndState()
        {
            using var db = new TaxDb();
            var (active, inactive) = Statuses(db);
            db.Exec("update tb_schools set show_on_website = false");   // a full template already holds schools (with dependants): switch them off instead of deleting
            var cityName = db.Scalar<string>("select city_name from lut_city where cid = 91 and sid = 5 and did = 50 and city_id = 353");
            var stateName = db.Scalar<string>("select sname from lut_state where cid = 91 and sid = 5");
            Seed(db, "S1", "Beta School", active, true, "/uploads/b/logo/logo_1.png", 353);
            Seed(db, "S2", "Alpha School", active, true);
            Seed(db, "S3", "Opted Out School", active, false);
            Seed(db, "S4", "Inactive School", inactive, true);
            Seed(db, "S5", "No Status School", null, true);

            var rows = await new PartnerSchoolRepository(db.Ctx()).GetListedAsync("Active", "general status", 60, default);
            Assert.Equal(new[] { "Alpha School", "Beta School" }, rows.Select(r => r.Name).ToArray());   // sorted by name; opted-out, inactive and status-less schools are absent
            var beta = rows.Single(r => r.Name == "Beta School");
            Assert.Equal(cityName, beta.City);
            Assert.Equal(stateName, beta.State);
            Assert.Equal("/uploads/b/logo/logo_1.png", beta.LogoUrl);
            Assert.Null(rows.Single(r => r.Name == "Alpha School").City);
        }

        [DbFact]
        public async Task Query_ExcludesThePlatformSchool_ByItsSuperAdminLogin_NotByItsId()
        {
            using var db = new TaxDb();
            var (active, _) = Statuses(db);
            db.Exec("update tb_schools set show_on_website = false");
            db.Exec("insert into lut_roles (role_id, role_code, role_name) overriding system value values (1, 'SUPER_ADMIN', 'Super Admin'), (2, 'SCHOOL_ADMIN', 'School Admin') on conflict do nothing");
            Seed(db, "PLATFORM1", "Platform Row", active, true);   // no id is hard-coded anywhere: it is found through its Super Admin login
            Seed(db, "REAL1", "Real School", active, true);
            db.Exec("insert into tb_users (school_id, org_user_id, user_type, status_id) values ('PLATFORM1', 'SUAD001', 'Super Admin', " + active + "), ('REAL1', 'REAL1', 'School', " + active + ")");
            db.Exec("insert into tb_user_roles (user_id, role_id) select user_id, case when school_id = 'PLATFORM1' then 1 else 2 end from tb_users where school_id in ('PLATFORM1', 'REAL1')");

            var repo = new PartnerSchoolRepository(db.Ctx());
            var rows = await repo.GetListedAsync("Active", "general status", 60, default);
            Assert.Equal(new[] { "Real School" }, rows.Select(r => r.Name).ToArray());
            Assert.True(await repo.IsPlatformSchoolAsync("PLATFORM1", default));
            Assert.False(await repo.IsPlatformSchoolAsync("REAL1", default));
            Assert.False(await repo.IsPlatformSchoolAsync("NOPE", default));
        }

        [DbFact]
        public async Task Query_RespectsTheCap_AndSwitchingTheFlagChangesTheAnswer()
        {
            using var db = new TaxDb();
            var (active, _) = Statuses(db);
            db.Exec("update tb_schools set show_on_website = false");   // a full template already holds schools (with dependants): switch them off instead of deleting
            for (var i = 1; i <= 5; i++) Seed(db, $"S{i}", $"School {i}", active, true);

            var repo = () => new PartnerSchoolRepository(db.Ctx());
            Assert.Equal(3, (await repo().GetListedAsync("Active", "general status", 3, default)).Count);

            Assert.True(await repo().SetShowOnWebsiteAsync("S1", false, DateTime.UtcNow, default));
            var names = (await repo().GetListedAsync("Active", "general status", 60, default)).Select(r => r.Name).ToList();
            Assert.DoesNotContain("School 1", names);
            Assert.Equal(4, names.Count);
            Assert.False(await repo().SetShowOnWebsiteAsync("NOPE", true, DateTime.UtcNow, default));
        }
    }
}
