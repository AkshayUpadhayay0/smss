using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using smss_api.Controllers;
using smss_api.Helpers;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Admission Type is a GLOBAL master (lut_admission_type): Super Admin writes, everyone signed in reads the ACTIVE ones, and a type that was
    /// deactivated stays readable on the applications that already use it.
    /// </summary>
    public class AdmissionTypeGlobalTests
    {
        private static readonly string[] SchoolRoles = { "School Admin", "Principal", "Teacher", "Accountant", "Admission Staff", "Student", "Guardian", "Driver" };

        private static IEnumerable<MethodInfo> WritesOf(Type t) =>
            t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes().Any(a => a is HttpPostAttribute or HttpPutAttribute or HttpDeleteAttribute or HttpPatchAttribute));

        // ---------------------------------------------------------------- authorization matrix
        [Fact]
        public void SchoolSide_HasNoWriteEndpoint_AndIsAuthorizeOnly_SoAnonymousGets401()
        {
            Assert.Empty(WritesOf(typeof(AdmissionTypesController)));
            Assert.NotEmpty(typeof(AdmissionTypesController).GetCustomAttributes<AuthorizeAttribute>());
            Assert.Empty(typeof(AdmissionTypesController).GetCustomAttributes<AllowAnonymousAttribute>());
            Assert.Empty(typeof(AdmissionTypesController).GetMethod("GetActive")!.GetCustomAttributes<AllowAnonymousAttribute>());
        }

        [Fact]
        public void MasterData_AdmissionTypeWrites_AreSuperAdminOnly_AndPostOrPut()
        {
            var names = new[] { "CreateAdmissionType", "UpdateAdmissionType", "ToggleAdmissionTypeStatus" };
            foreach (var n in names)
            {
                var m = typeof(MasterDataController).GetMethod(n)!;
                Assert.NotNull(m.GetCustomAttribute<SuperAdminOnlyAttribute>());
                Assert.DoesNotContain(m.GetCustomAttributes(), a => a is HttpPatchAttribute);   // PATCH is blocked in this environment
            }
            Assert.NotNull(typeof(MasterDataController).GetCustomAttribute<AuthorizeAttribute>());   // anonymous = 401
        }

        [Fact]
        public void SuperAdminGuard_Refuses403ToEveryOtherRole_OnAdmissionTypeWrites()
        {
            foreach (var n in new[] { "CreateAdmissionType", "UpdateAdmissionType", "ToggleAdmissionTypeStatus" })
            {
                var attr = typeof(MasterDataController).GetMethod(n)!.GetCustomAttribute<SuperAdminOnlyAttribute>()!;
                foreach (var role in SchoolRoles)
                {
                    var ctx = new Microsoft.AspNetCore.Mvc.Filters.AuthorizationFilterContext(
                        new ActionContext(new Microsoft.AspNetCore.Http.DefaultHttpContext { User = Fake.User("SCH1", 7, role) }, new Microsoft.AspNetCore.Routing.RouteData(),
                            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()), new List<Microsoft.AspNetCore.Mvc.Filters.IFilterMetadata>());
                    attr.OnAuthorization(ctx);
                    Assert.True(ctx.Result is ObjectResult { StatusCode: 403 }, $"{n} must refuse {role}");
                }
            }
        }

        [Fact]
        public async Task EverySignedInRole_ReadsOnlyActiveTypes_OnTheSchoolEndpoint()
        {
            foreach (var role in SchoolRoles.Append("Super Admin"))
            {
                bool? asked = null;
                var svc = Fake.Of<IMasterDataService>(new()
                {
                    ["GetAdmissionTypesAsync"] = (Func<object?[], object?>)(a => { asked = (bool)a[0]!; return Fake.Ok<object>(new List<object>()); })
                });
                var c = Fake.WithUser(new AdmissionTypesController(svc), Fake.User("SCH1", 7, role));
                Assert.Equal(200, Fake.Code(await c.GetActive()));
                Assert.False(asked, $"{role} must only get active types");
            }
        }

        [Fact]
        public async Task InactiveTypes_AreForTheSuperAdminScreenOnly()
        {
            var svc = Fake.Of<IMasterDataService>(new() { ["GetAdmissionTypesAsync"] = Fake.Ok<object>(new List<object>()) });
            foreach (var role in SchoolRoles)
            {
                var c = Fake.WithUser(new MasterDataController(svc), Fake.User("SCH1", 7, role));
                Assert.Equal(403, Fake.Code(await c.GetAdmissionTypes(includeInactive: true)));
                Assert.Equal(200, Fake.Code(await c.GetAdmissionTypes(includeInactive: false)));
            }
            var sa = Fake.WithUser(new MasterDataController(svc), Fake.User(null, 1, "Super Admin"));
            Assert.Equal(200, Fake.Code(await sa.GetAdmissionTypes(includeInactive: true)));
        }

        // ---------------------------------------------------------------- duplicate names
        private static MasterDataService MasterService(Dictionary<string, object?> answers) =>
            new(Fake.Of<IMasterDataRepository>(answers), NullLogger<MasterDataService>.Instance);

        [Fact]
        public async Task Create_DuplicateNameOrCode_Is409_AndNothingIsAdded()
        {
            var repo = Fake.Of<IMasterDataRepository>(new() { ["AdmissionTypeExistsAsync"] = true });
            var svc = new MasterDataService(repo, NullLogger<MasterDataService>.Instance);
            var r = await svc.CreateAdmissionTypeAsync(new CreateAdmissionTypeRequestDto { TypeCode = "new_adm", TypeName = "  new admission " });
            Assert.Equal(409, r.StatusCode);
            Assert.DoesNotContain("AddAdmissionTypeAsync", Fake.ProxyOf(repo).Calls);
        }

        [Fact]
        public async Task Create_LosingARaceToTheUniqueIndex_Is409()
        {
            var svc = MasterService(new() { ["AdmissionTypeExistsAsync"] = false, ["AddAdmissionTypeAsync"] = false });
            Assert.Equal(409, (await svc.CreateAdmissionTypeAsync(new CreateAdmissionTypeRequestDto { TypeCode = "X1", TypeName = "Name" })).StatusCode);
        }

        [Fact]
        public async Task Create_NormalisesCodeToUpperCase_AndTrimsName_AndStartsActive()
        {
            LutAdmissionType? saved = null;
            var svc = MasterService(new()
            {
                ["AdmissionTypeExistsAsync"] = false,
                ["AddAdmissionTypeAsync"] = (Func<object?[], object?>)(a => { saved = (LutAdmissionType)a[0]!; return true; })
            });
            var r = await svc.CreateAdmissionTypeAsync(new CreateAdmissionTypeRequestDto { TypeCode = "rte_adm", TypeName = "  RTE Admission  ", Description = "  " });
            Assert.Equal(201, r.StatusCode);
            Assert.Equal("RTE_ADM", saved!.TypeCode);
            Assert.Equal("RTE Admission", saved.TypeName);
            Assert.Null(saved.Description);
            Assert.True(saved.IsActive);
        }

        [Fact]
        public async Task Update_ToAnotherTypesName_Is409_AndTheCodeStaysImmutable()
        {
            var row = new LutAdmissionType { AdmissionTypeId = 4, TypeCode = "NEW_ADM", TypeName = "New Admission", IsActive = true };
            var svc = MasterService(new() { ["GetAdmissionTypeByIdAsync"] = row, ["AdmissionTypeExistsAsync"] = true });
            Assert.Equal(409, (await svc.UpdateAdmissionTypeAsync(4, new UpdateAdmissionTypeRequestDto { TypeName = "Transfer" })).StatusCode);
            Assert.Equal("New Admission", row.TypeName);
            Assert.Null(typeof(UpdateAdmissionTypeRequestDto).GetProperty("TypeCode"));
        }

        [Fact]
        public async Task Toggle_FlipsTheFlag_NeverDeletes()
        {
            var row = new LutAdmissionType { AdmissionTypeId = 4, TypeCode = "NEW_ADM", TypeName = "New Admission", IsActive = true };
            var repo = Fake.Of<IMasterDataRepository>(new() { ["GetAdmissionTypeByIdAsync"] = row, ["UpdateAdmissionTypeAsync"] = true });
            var svc = new MasterDataService(repo, NullLogger<MasterDataService>.Instance);
            Assert.Equal(200, (await svc.ToggleAdmissionTypeStatusAsync(4)).StatusCode);
            Assert.False(row.IsActive);
            await svc.ToggleAdmissionTypeStatusAsync(4);
            Assert.True(row.IsActive);
            Assert.DoesNotContain(typeof(IMasterDataRepository).GetMethods(), m => m.Name.Contains("AdmissionType") && m.Name.StartsWith("Delete"));
            Assert.Equal(404, (await MasterService(new() { ["GetAdmissionTypeByIdAsync"] = null }).ToggleAdmissionTypeStatusAsync(99)).StatusCode);
        }

        // ---------------------------------------------------------------- deactivated types: hidden on the form, visible on old applications
        private const long ActiveType = 1, DeactivatedType = 2, OtherDeactivatedType = 3;

        private static AdmissionService Admissions(out TbAdmission old)
        {
            old = new TbAdmission
            {
                AdmissionId = 10, SchoolId = "SCH1", ApplicationNumber = "ADM2026-0001", ApplicantFirstName = "Asha", ApplicantLastName = "Rao",
                AdmissionTypeId = DeactivatedType, AcademicYearId = 1, AppliedClassId = 1, StatusId = 1
            };
            var o = old;
            var repo = Fake.Of<IAdmissionRepository>(new()
            {
                ["AcademicYearBelongsToSchoolAsync"] = true,
                ["ClassBelongsToSchoolAsync"] = true,
                ["AdmissionTypeUsableAsync"] = (Func<object?[], object?>)(a => (bool)a[1]! || (long)a[0]! == ActiveType),
                ["GetByIdAsync"] = o,
                ["GetAdmissionStatusNamesAsync"] = new Dictionary<int, string> { [1] = "SUBMITTED" },
                ["GetClassNamesAsync"] = new Dictionary<long, string> { [1] = "Class 1" },
                ["GetAcademicYearNamesAsync"] = new Dictionary<long, string> { [1] = "2026-27" },
                ["GetAdmissionTypeNamesAsync"] = new Dictionary<long, string> { [ActiveType] = "New Admission", [DeactivatedType] = "Old Style" },
            });
            return new AdmissionService(repo, Fake.Of<INotificationService>(), Fake.Of<IStudentFeeBillingService>(), NullLogger<AdmissionService>.Instance);
        }

        private static SubmitAdmissionRequestDto Req(long typeId) => new()
        {
            AcademicYearId = 1, AppliedClassId = 1, AdmissionTypeId = typeId, ApplicantFirstName = "Asha", ApplicantLastName = "Rao",
            DateOfBirth = new DateTime(2015, 1, 1), GuardianName = "G", GuardianRelationship = "Father", GuardianMobileNumber = "9999999999"
        };

        [Fact]
        public async Task NewApplication_CannotUseADeactivatedType()
        {
            var svc = Admissions(out _);
            var r = await svc.SubmitAsync("SCH1", Req(DeactivatedType), CancellationToken.None);
            Assert.Equal(400, r.StatusCode);
            Assert.Contains("admission type", r.Message);
        }

        [Fact]
        public async Task NewApplication_CanUseAnActiveType()
        {
            var svc = Admissions(out _);
            var r = await svc.SubmitAsync("SCH1", Req(ActiveType), CancellationToken.None);
            Assert.DoesNotContain("admission type", r.Message ?? "");   // gets past type validation (the fake has nothing further configured)
        }

        [Fact]
        public async Task EditingAnOldApplication_MayKeepItsNowDeactivatedType_ButNotSwitchToAnotherDeactivatedOne()
        {
            var svc = Admissions(out _);
            var keep = await svc.UpdateAsync("SCH1", 10, Req(DeactivatedType), CancellationToken.None);
            Assert.DoesNotContain("admission type", keep.Message ?? "");
            var switched = await svc.UpdateAsync("SCH1", 10, Req(OtherDeactivatedType), CancellationToken.None);
            Assert.Equal(400, switched.StatusCode);
        }

        [Fact]
        public async Task OldApplication_StillShowsItsDeactivatedTypeName()
        {
            var svc = Admissions(out _);
            var r = await svc.GetByIdAsync("SCH1", 10, CancellationToken.None);
            Assert.Equal(200, r.StatusCode);
            Assert.Equal("Old Style", r.Data!.AdmissionTypeName);
        }

        [Fact]
        public void NameLookups_DoNotFilterOnActive_SoOldRowsStayReadable()
        {
            // the name lookup has no includeInactive switch at all: it is always the full table
            var m = typeof(IAdmissionRepository).GetMethod("GetAdmissionTypeNamesAsync")!;
            Assert.DoesNotContain(m.GetParameters(), p => p.ParameterType == typeof(bool));
        }
    }
}
