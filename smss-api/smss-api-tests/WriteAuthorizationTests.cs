using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using smss_api.Controllers;
using smss_api.Helpers;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Security fix: every write endpoint of the school master-data controllers needs a School Admin, and the platform-level writes (the school registry
    /// and the global lookups) need the Super Admin. Endpoints are found by reflection, so a NEW write added to one of these controllers without a guard fails here.
    /// Controllers are built with fake services: a guard that works answers 403 before any service is touched.
    /// </summary>
    public class WriteAuthorizationTests
    {
        private const string School = "SCH1";
        // "Driver" is the role name the Transport module will seed; the check is name-based, so the literal is enough here.
        private static readonly string[] NotAllowed = { "Student", "Driver", "Teacher", "Accountant", "Principal", "Guardian", "Admission Staff", "Super Admin" };

        public static IEnumerable<object[]> SchoolSideControllers() =>
            new[]
            {
                typeof(ClassesController), typeof(SectionsController), typeof(SubjectsController), typeof(AcademicYearController), typeof(ClassSubjectsController),
                typeof(AdmissionTypesController), typeof(EmployeeDesignationsController), typeof(EmployeeDepartmentsController), typeof(DocumentTypeController),
                typeof(MasterImportController),
            }.Select(t => new object[] { t });

        private static bool IsWrite(MethodInfo m) =>
            m.GetCustomAttributes().Any(a => a is HttpPostAttribute or HttpPutAttribute or HttpDeleteAttribute);

        private static IEnumerable<MethodInfo> Writes(Type controller) =>
            controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(IsWrite);

        private static ControllerBase Build(Type controller, params string[] roles)
        {
            var ctor = controller.GetConstructors().Single();
            var args = ctor.GetParameters().Select(p => typeof(Fake).GetMethod(nameof(Fake.Of))!.MakeGenericMethod(p.ParameterType).Invoke(null, new object?[] { null })).ToArray();
            var c = (ControllerBase)ctor.Invoke(args);
            return Fake.WithUser(c, Fake.User(School, 7, roles));
        }

        private static async Task<int> Call(ControllerBase controller, MethodInfo m)
        {
            var args = m.GetParameters().Select(p =>
                p.ParameterType == typeof(string) ? (p.Name == "schoolId" ? School : "x")
                : p.ParameterType == typeof(CancellationToken) ? CancellationToken.None
                : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
            var result = m.Invoke(controller, args);
            var action = result is Task<IActionResult> t ? await t : (IActionResult)result!;
            return Fake.Code(action);
        }

        [Theory]
        [MemberData(nameof(SchoolSideControllers))]
        public async Task SchoolSideWrites_RefuseEveryoneButSchoolAdmin(Type controller)
        {
            var writes = Writes(controller).ToList();
            Assert.NotEmpty(writes);
            foreach (var m in writes)
            {
                foreach (var role in NotAllowed)
                    Assert.True(403 == await Call(Build(controller, role), m), $"{controller.Name}.{m.Name} must refuse {role}");
                Assert.True(403 == await Call(Build(controller), m), $"{controller.Name}.{m.Name} must refuse a login with no role");
                // the intended role gets past the guard (the fake service then answers 500 "not configured", which is not a 403)
                Assert.NotEqual(403, await Call(Build(controller, "School Admin"), m));
            }
        }

        [Fact]
        public async Task SchoolAdmin_StillCreatesAClass()
        {
            var svc = Fake.Of<smss_api_service_layer.@interface.IClassService>(new() { ["CreateAsync"] = Fake.Ok<object>(new object()) });
            var c = Fake.WithUser(new ClassesController(svc), Fake.User(School, 1, "School Admin"));
            var r = await c.Create(new smss_api_service_layer.dto.CreateClassRequest());
            Assert.Equal(200, Fake.Code(r));
            Assert.Contains("CreateAsync", Fake.ProxyOf(svc).Calls);
        }

        [Fact]
        public async Task RefusedWrite_NeverReachesTheService()
        {
            var svc = Fake.Of<smss_api_service_layer.@interface.IClassService>();
            var c = Fake.WithUser(new ClassesController(svc), Fake.User(School, 1, "Student"));
            Assert.Equal(403, Fake.Code(await c.Create(new smss_api_service_layer.dto.CreateClassRequest())));
            Assert.Equal(403, Fake.Code(await c.ToggleStatus(5)));
            Assert.Empty(Fake.ProxyOf(svc).Calls);
        }

        // ---------------------------------------------------------------- the school's own profile (token school only)
        [Theory]
        [InlineData("UpdateMySchool")]
        [InlineData("UploadMyLogo")]
        [InlineData("RemoveMyLogo")]
        public async Task OwnSchoolWrites_NeedASchoolAdmin(string name)
        {
            var m = typeof(SchoolRegistrationController).GetMethod(name)!;
            foreach (var role in NotAllowed)
                Assert.True(403 == await Call(Build(typeof(SchoolRegistrationController), role), m), $"{name} must refuse {role}");
            Assert.NotEqual(403, await Call(Build(typeof(SchoolRegistrationController), "School Admin"), m));
        }

        // ---------------------------------------------------------------- Super Admin only: the school registry and the global lookups
        public static IEnumerable<object[]> SuperAdminOnlyMethods()
        {
            foreach (var m in typeof(MasterDataController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(IsWrite))
                yield return new object[] { typeof(MasterDataController), m.Name };
            foreach (var n in new[] { "GetAllSchools", "GetSchoolById", "RegisterSchool", "UpdateSchool", "ToggleSchoolStatus", "UploadLogo", "RemoveLogo" })
                yield return new object[] { typeof(SchoolRegistrationController), n };
        }

        private static AuthorizationFilterContext FilterContext(System.Security.Claims.ClaimsPrincipal user) =>
            new(new ActionContext(new DefaultHttpContext { User = user }, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());

        [Fact]
        public void EveryMasterDataWrite_IsSuperAdminOnly()
        {
            var writes = typeof(MasterDataController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(IsWrite).ToList();
            Assert.Equal(27, writes.Count);
            foreach (var m in writes) Assert.True(m.GetCustomAttribute<SuperAdminOnlyAttribute>() != null, $"MasterDataController.{m.Name} needs [SuperAdminOnly]");
        }

        [Fact]
        public void MasterDataReads_StayOpenToEveryLogin()
        {
            // dropdowns for every role read these: they must NOT be Super Admin only
            foreach (var m in typeof(MasterDataController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => !IsWrite(m)))
                Assert.Null(m.GetCustomAttribute<SuperAdminOnlyAttribute>());
        }

        [Theory]
        [MemberData(nameof(SuperAdminOnlyMethods))]
        public void SuperAdminOnlyEndpoints_Answer403BeforeBinding_ForEveryOtherRole(Type controller, string method)
        {
            var m = controller.GetMethod(method)!;
            var attr = m.GetCustomAttribute<SuperAdminOnlyAttribute>();
            Assert.NotNull(attr);
            foreach (var role in new[] { "Student", "Driver", "Teacher", "Accountant", "Principal", "Guardian", "Admission Staff", "School Admin" })
            {
                var ctx = FilterContext(Fake.User(School, 7, role));
                attr!.OnAuthorization(ctx);
                Assert.True(ctx.Result is ObjectResult { StatusCode: 403 }, $"{controller.Name}.{method} must refuse {role}");
            }
            var ok = FilterContext(Fake.User(null, 1, "Super Admin"));
            attr!.OnAuthorization(ok);
            Assert.Null(ok.Result);
        }

        [Fact]
        public void SchoolRegistration_HasNoClientSuppliedSchoolOnTheOwnSchoolEndpoints()
        {
            foreach (var name in new[] { "GetMySchool", "UpdateMySchool", "UploadMyLogo", "RemoveMyLogo" })
                Assert.DoesNotContain(typeof(SchoolRegistrationController).GetMethod(name)!.GetParameters(), p => p.Name!.Contains("school", StringComparison.OrdinalIgnoreCase));
        }
    }
}
