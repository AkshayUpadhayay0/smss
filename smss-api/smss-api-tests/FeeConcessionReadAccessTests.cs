using Microsoft.Extensions.Logging.Abstractions;
using smss_api.Controllers;
using smss_api_db_layer.repository;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>GET /api/FeeConcession (list and by id): School Admin, Accountant and Principal (CanViewFees); every other role 403; anonymous 401; another school's id 404.</summary>
    public class FeeConcessionReadAccessTests
    {
        private static readonly string[] Viewers = { "School Admin", "Accountant", "Principal" };
        private static readonly string[] NotViewers = { "Teacher", "Student", "Guardian", "Admission Staff", "Super Admin", "Driver" };

        private static long NewConcession(TaxDb db, string school, string name, string code) =>
            db.Scalar<long>($"insert into tb_fee_concessions (school_id, concession_name, concession_code, concession_type, value, isactive) values ('{school}', '{name}', '{code}', 'PERCENTAGE', 5, true) returning concession_id");

        private static async Task<int> Read(string call, params string[] roles)
        {
            var svc = Fake.Of<IFeeConcessionService>(new() { ["GetAllAsync"] = Fake.Ok<object>(new List<object>()), ["GetByIdAsync"] = Fake.Ok<object>(new object()) });
            var c = Fake.WithUser(new FeeConcessionController(svc), Fake.User("SCH1", 7, roles));
            var res = call == "list" ? await c.GetAll() : await c.GetById(1);
            var code = Fake.Code(res);
            if (code == 403) Assert.Empty(Fake.ProxyOf(svc).Calls);   // refused before anything was read
            return code;
        }

        [Theory]
        [InlineData("list")]
        [InlineData("byId")]
        public async Task ConcessionReads_AreSchoolAdminAccountantPrincipal_EveryoneElse403(string call)
        {
            foreach (var r in Viewers) Assert.Equal(200, await Read(call, r));
            foreach (var r in NotViewers) Assert.True(403 == await Read(call, r), $"{r} must be refused");
            Assert.Equal(403, await Read(call));   // a login with no role at all
        }

        [Fact]
        public void ConcessionController_RequiresALogin_NoAnonymousEndpoint()
        {
            Assert.NotEmpty(typeof(FeeConcessionController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true));   // anonymous = 401
            Assert.Empty(typeof(FeeConcessionController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), true));
            foreach (var m in typeof(FeeConcessionController).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                Assert.Empty(m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), true));
        }

        [DbFact]
        public async Task AnotherSchoolsConcession_Is404ById_AndNeverInTheList()
        {
            using var db = new TaxDb();
            var mine = NewConcession(db, TaxDb.SchoolA, "Mine", "MINE");
            var theirs = NewConcession(db, TaxDb.SchoolB, "Theirs", "THEIRS");
            var svc = new FeeConcessionService(new FeeConcessionRepository(db.Ctx()), NullLogger<FeeConcessionService>.Instance);
            Assert.Equal(200, (await svc.GetByIdAsync(TaxDb.SchoolA, mine)).StatusCode);
            Assert.Equal(404, (await svc.GetByIdAsync(TaxDb.SchoolA, theirs)).StatusCode);
            var list = System.Text.Json.JsonSerializer.Serialize((await svc.GetAllAsync(TaxDb.SchoolA)).Data);
            Assert.Contains("Mine", list); Assert.DoesNotContain("Theirs", list);
        }
    }
}
