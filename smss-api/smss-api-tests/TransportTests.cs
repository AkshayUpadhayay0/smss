using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using smss_api.Controllers;
using smss_api_db_layer.entity;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    // ======================================================================================================================================
    // Controllers: who may call what. Every Transport endpoint is found by reflection, so a new action without a role check fails here.
    // ======================================================================================================================================
    public class TransportAuthorizationTests
    {
        private const string School = "SCH1";
        private static readonly string[] Never = { "Student", "Driver", "Teacher", "Accountant", "Admission Staff", "Guardian", "Super Admin" };

        public static IEnumerable<object[]> Controllers() =>
            new[] { typeof(TransportAreasController), typeof(VehiclesController), typeof(DriversController) }.Select(t => new object[] { t });

        private static IEnumerable<MethodInfo> Endpoints(Type c) =>
            c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.GetCustomAttributes().Any(a => a is HttpMethodAttribute));
        private static bool IsRead(MethodInfo m) => m.GetCustomAttributes().Any(a => a is HttpGetAttribute);

        private static (ControllerBase Controller, List<object> Services) Build(Type controller, params string[] roles)
        {
            var ctor = controller.GetConstructors().Single();
            var args = ctor.GetParameters().Select(p => p.ParameterType == typeof(IDriverPhotoService)
                ? Fake.Of<IDriverPhotoService>(new() { ["GetAsync"] = new StudentPhotoResult { StatusCode = 404, Message = "none" } })
                : typeof(Fake).GetMethod(nameof(Fake.Of))!.MakeGenericMethod(p.ParameterType).Invoke(null, new object?[] { null })!).ToList();
            var c = (ControllerBase)ctor.Invoke(args.ToArray());
            return (Fake.WithUser(c, Fake.User(School, 7, roles)), args!);
        }

        private static async Task<int> Call(ControllerBase controller, MethodInfo m)
        {
            var args = m.GetParameters().Select(p => p.ParameterType == typeof(string) ? "DPS001DR001" : p.ParameterType == typeof(CancellationToken) ? CancellationToken.None
                : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
            var result = m.Invoke(controller, args);
            return Fake.Code(result is Task<IActionResult> t ? await t : (IActionResult)result!);
        }

        [Theory]
        [MemberData(nameof(Controllers))]
        public async Task EveryEndpoint_RefusesEveryRole_ExceptTheOnesTheSpecAllows(Type controller)
        {
            var endpoints = Endpoints(controller).ToList();
            Assert.True(endpoints.Count >= 5);
            foreach (var m in endpoints)
            {
                foreach (var role in Never)
                    Assert.True(403 == await Call(Build(controller, role).Controller, m), $"{controller.Name}.{m.Name} must refuse {role}");
                Assert.True(403 == await Call(Build(controller).Controller, m), $"{controller.Name}.{m.Name} must refuse a login with no role");

                // School Admin gets past the guard everywhere; the Principal only on reads
                Assert.NotEqual(403, await Call(Build(controller, "School Admin").Controller, m));
                var principal = await Call(Build(controller, "Principal").Controller, m);
                if (IsRead(m)) Assert.NotEqual(403, principal); else Assert.True(403 == principal, $"{controller.Name}.{m.Name} is a write: the Principal is read-only");
            }
        }

        [Theory]
        [MemberData(nameof(Controllers))]
        public async Task ARefusedCall_NeverReachesAService_SoTheRoleCheckIsTheFirstStatement(Type controller)
        {
            foreach (var m in Endpoints(controller))
                foreach (var role in new[] { "Student", "Driver", "Teacher", "Accountant", "Admission Staff" })
                {
                    var (c, services) = Build(controller, role);
                    await Call(c, m);
                    Assert.True(services.All(s => Fake.ProxyOf(s).Calls.Count == 0), $"{controller.Name}.{m.Name} touched a service before refusing {role}");
                }
        }

        [Fact]
        public async Task NoEndpoint_TakesASchoolIdOrStatus_FromTheClient()
        {
            foreach (var t in new[] { typeof(TransportAreasController), typeof(VehiclesController), typeof(DriversController) })
                foreach (var m in Endpoints(t))
                    Assert.DoesNotContain(m.GetParameters(), p => p.Name!.Contains("school", StringComparison.OrdinalIgnoreCase) || p.Name!.Equals("statusId", StringComparison.OrdinalIgnoreCase));
            await Task.CompletedTask;
        }

        [Fact]
        public void Roles_DriverIsOnItsOwnAllowList_AndNeverOnTheEmployeeOne()
        {
            Assert.Equal(new[] { "DRIVER" }, RoleCodes.AssignableToDrivers.ToArray());
            Assert.DoesNotContain("DRIVER", RoleCodes.AssignableToEmployees);          // the Employee screen can never hand out the Driver role
            foreach (var never in new[] { "SUPER_ADMIN", "STUDENT", "GUARDIAN", "SCHOOL_ADMIN", "TEACHER" }) Assert.DoesNotContain(never, RoleCodes.AssignableToDrivers);
            Assert.Equal("Driver", RoleNames.Driver);
            Assert.Equal("Driver", UserTypes.Driver);
        }
    }

    // ======================================================================================================================================
    // The deny-list fix: a Driver is neither staff nor a Student / Guardian, so the student and employee reads must refuse it too.
    // ======================================================================================================================================
    public class DriverRefusalTests
    {
        private const string School = "SCH1";
        private static ControllerBase Build(Type controller, params string[] roles)
        {
            var ctor = controller.GetConstructors().Single();
            var args = ctor.GetParameters().Select(p => p.ParameterType == typeof(IStudentPhotoService)
                ? Fake.Of<IStudentPhotoService>(new() { ["GetAsync"] = new StudentPhotoResult { StatusCode = 404, Message = "none" } })
                : p.ParameterType == typeof(IEmployeePhotoService) ? Fake.Of<IEmployeePhotoService>(new() { ["GetAsync"] = new StudentPhotoResult { StatusCode = 404, Message = "none" } })
                : typeof(Fake).GetMethod(nameof(Fake.Of))!.MakeGenericMethod(p.ParameterType).Invoke(null, new object?[] { null })!).ToArray();
            return Fake.WithUser((ControllerBase)ctor.Invoke(args), Fake.User(School, 7, roles));
        }

        private static async Task<int> Call(ControllerBase controller, MethodInfo m)
        {
            var args = m.GetParameters().Select(p => p.ParameterType == typeof(string) ? (p.Name == "schoolId" ? School : "DPS001ST001") : p.ParameterType == typeof(CancellationToken) ? CancellationToken.None
                : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
            var result = m.Invoke(controller, args);
            return Fake.Code(result is Task<IActionResult> t ? await t : (IActionResult)result!);
        }

        [Fact]
        public async Task EveryStudentRead_RefusesADriver_WhileASchoolAdminStillGetsThrough()
        {
            var reads = typeof(StudentController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes().Any(a => a is HttpGetAttribute)).ToList();
            Assert.NotEmpty(reads);
            foreach (var m in reads)
            {
                Assert.True(403 == await Call(Build(typeof(StudentController), "Driver"), m), $"StudentController.{m.Name} must refuse a Driver");
                // the "me" and "my ..." reads belong to a Student or a Teacher login, so a School Admin is refused there by design
                if (!m.Name.StartsWith("GetM", StringComparison.Ordinal)) Assert.True(403 != await Call(Build(typeof(StudentController), "School Admin"), m), $"StudentController.{m.Name} must still allow a School Admin");
            }
        }

        [Fact]
        public async Task StudentPhoto_And_EmployeeMe_And_EmployeePhoto_RefuseADriver()
        {
            Assert.Equal(403, await Call(Build(typeof(StudentController), "Driver"), typeof(StudentController).GetMethod("GetPhoto")!));
            Assert.Equal(403, await Call(Build(typeof(EmployeeController), "Driver"), typeof(EmployeeController).GetMethod("GetMe")!));
            Assert.Equal(403, await Call(Build(typeof(EmployeeController), "Driver"), typeof(EmployeeController).GetMethod("GetPhoto")!));
            // the roles that are allowed there are untouched
            Assert.NotEqual(403, await Call(Build(typeof(StudentController), "Teacher"), typeof(StudentController).GetMethod("GetPhoto")!));
            Assert.NotEqual(403, await Call(Build(typeof(EmployeeController), "Teacher"), typeof(EmployeeController).GetMethod("GetMe")!));
            Assert.NotEqual(403, await Call(Build(typeof(EmployeeController), "Teacher"), typeof(EmployeeController).GetMethod("GetPhoto")!));
        }

        [Fact]
        public void TheDriverRoleHelper_MatchesByName_CaseInsensitively()
        {
            Assert.True(Fake.User(School, 1, "driver").IsDriverForTest());
            Assert.False(Fake.User(School, 1, "School Admin", "Teacher").IsDriverForTest());
        }
    }

    internal static class ClaimsTestExt
    {
        public static bool IsDriverForTest(this System.Security.Claims.ClaimsPrincipal u) => smss_api.Helpers.ClaimsPrincipalExtensions.IsDriver(u);
    }

    // ======================================================================================================================================
    // Pure rules
    // ======================================================================================================================================
    public class TransportRulesTests
    {
        private static readonly DateOnly Today = new(2026, 11, 13);

        [Theory]
        [InlineData(-1, "EXPIRED", -1)]
        [InlineData(0, "EXPIRING", 0)]
        [InlineData(1, "EXPIRING", 1)]
        [InlineData(30, "EXPIRING", 30)]
        [InlineData(31, "OK", 31)]
        [InlineData(365, "OK", 365)]
        public void Expiry_BoundariesAreComputedFromToday(int offset, string expected, int days)
        {
            Assert.Equal(expected, TransportRules.Expiry(Today.AddDays(offset), Today, out var left));
            Assert.Equal(days, left);
        }

        [Fact]
        public void Expiry_NoDate_IsNotSet_WithNoDaysLeft()
        {
            Assert.Equal("NOT_SET", TransportRules.Expiry(null, Today, out var left));
            Assert.Null(left);
        }

        [Theory]
        [InlineData("up80 ab 1234", "UP80AB1234")]
        [InlineData("  UP80AB1234 ", "UP80AB1234")]
        [InlineData("up-80-ab", "UP-80-AB")]
        public void Plate_IsTrimmedUpperCasedAndSpacesRemoved(string raw, string expected) => Assert.Equal(expected, TransportRules.NormalizePlate(raw));

        [Theory]
        [InlineData("UP80AB1234", true)]
        [InlineData("AB1", false)]
        [InlineData("UP80 AB!", false)]
        [InlineData("THIS-PLATE-IS-WAY-TOO-LONG", false)]
        public void Plate_Validity(string normalized, bool ok) => Assert.Equal(ok, TransportRules.IsValidPlate(normalized));

        [Theory]
        [InlineData("dl-04 2011 0149646", "DL-0420110149646")]
        public void Dl_IsNormalised(string raw, string expected) => Assert.Equal(expected, TransportRules.NormalizeDl(raw));

        [Fact]
        public void DriverId_IsShortCodeDRnnn() => Assert.Equal("DPS001DR007", DriverIdHelper.Build("DPS001", 7));
    }

    // ======================================================================================================================================
    // Services (in-memory repositories that filter by school like the real ones)
    // ======================================================================================================================================
    public class TransportAreaServiceTests
    {
        private static (TransportAreaService Svc, TransportStore Store) Make() { var s = new TransportStore(); return (new TransportAreaService(new FakeAreaRepo(s), NullLogger<TransportAreaService>.Instance), s); }
        private static TransportAreaRequest Req(string name, decimal fee = 700) => new() { AreaName = name, TransportFee = fee };
        private const string A = TransportStore.SchoolA, B = TransportStore.SchoolB;

        [Fact]
        public async Task Create_TrimsTheName_StartsActive_AndKeepsTheFee()
        {
            var (svc, _) = Make();
            var r = await svc.CreateAsync(A, new TransportAreaRequest { AreaName = "  North Gate ", TransportFee = 700.555m, DistanceKm = 4.5m, Remarks = "  " }, default);
            Assert.Equal(201, r.StatusCode);
            Assert.Equal("North Gate", r.Data!.AreaName); Assert.Equal("Active", r.Data.StatusName);
            Assert.Equal(700.56m, r.Data.TransportFee); Assert.Null(r.Data.Remarks);
        }

        [Fact]
        public async Task ActiveNameMustBeUniquePerSchool_CaseInsensitively_ButOtherSchoolsAndInactiveNamesAreFree()
        {
            var (svc, s) = Make();
            Assert.Equal(201, (await svc.CreateAsync(A, Req("North"), default)).StatusCode);
            var dup = await svc.CreateAsync(A, Req("  NORTH "), default);
            Assert.Equal(409, dup.StatusCode); Assert.Contains("already exists", dup.Message);
            Assert.Equal(201, (await svc.CreateAsync(B, Req("North"), default)).StatusCode);               // another school
            s.AddArea(A, "Old", TransportStore.Inactive);
            Assert.Equal(201, (await svc.CreateAsync(A, Req("Old"), default)).StatusCode);                  // an inactive area's name is free
        }

        [Fact]
        public async Task Update_ToAnotherActiveAreasName_Is409_AndKeepingYourOwnNameIsFine()
        {
            var (svc, s) = Make();
            var north = s.AddArea(A, "North"); s.AddArea(A, "South");
            Assert.Equal(409, (await svc.UpdateAsync(A, north.AreaId, Req("south"), default)).StatusCode);
            Assert.Equal(200, (await svc.UpdateAsync(A, north.AreaId, Req("North", 900), default)).StatusCode);
        }

        [Fact]
        public async Task Deactivate_IsBlockedWhileAnActiveDriverHasTheArea_AndNamesTheDriver()
        {
            var (svc, s) = Make();
            var area = s.AddArea(A, "North");
            s.AddDriver(A, "DPS001DR001", "Ramesh Kumar"); s.Link(A, "DPS001DR001", area.AreaId);
            var r = await svc.ToggleStatusAsync(A, area.AreaId, default);
            Assert.Equal(409, r.StatusCode);
            Assert.Contains("Ramesh Kumar (DPS001DR001)", r.Message);
            Assert.Equal(TransportStore.Active, area.StatusId);
        }

        [Fact]
        public async Task Deactivate_Works_WhenOnlyAnInactiveDriverHasTheArea_AndReactivateChecksTheNameAgain()
        {
            var (svc, s) = Make();
            var area = s.AddArea(A, "North");
            s.AddDriver(A, "DPS001DR001", "Old Driver", TransportStore.Inactive); s.Link(A, "DPS001DR001", area.AreaId);
            Assert.Equal(200, (await svc.ToggleStatusAsync(A, area.AreaId, default)).StatusCode);
            Assert.Equal(TransportStore.Inactive, area.StatusId);
            s.AddArea(A, "north");                                                                             // someone took the name meanwhile
            var again = await svc.ToggleStatusAsync(A, area.AreaId, default);
            Assert.Equal(409, again.StatusCode); Assert.Equal(TransportStore.Inactive, area.StatusId);
        }

        [Fact]
        public async Task AnotherSchoolsArea_IsA404_ForGet_Update_AndToggle_AndNeverAppearsInTheList()
        {
            var (svc, s) = Make();
            var theirs = s.AddArea(B, "Their Area"); s.AddArea(A, "Mine");
            Assert.Equal(404, (await svc.GetAsync(A, theirs.AreaId, default)).StatusCode);
            Assert.Equal(404, (await svc.UpdateAsync(A, theirs.AreaId, Req("Hijack"), default)).StatusCode);
            Assert.Equal(404, (await svc.ToggleStatusAsync(A, theirs.AreaId, default)).StatusCode);
            Assert.Equal("Their Area", theirs.AreaName); Assert.Equal(TransportStore.Active, theirs.StatusId);
            var list = await svc.ListAsync(A, null, "All", 1, 10, default);
            Assert.Equal(new[] { "Mine" }, list.Data!.Items.Select(i => i.AreaName).ToArray());
        }

        [Fact]
        public async Task NoSchoolInTheToken_Is400_OnEveryMethod()
        {
            var (svc, _) = Make();
            Assert.Equal(400, (await svc.ListAsync(null, null, null, 1, 10, default)).StatusCode);
            Assert.Equal(400, (await svc.GetAsync("", 1, default)).StatusCode);
            Assert.Equal(400, (await svc.CreateAsync(null, Req("x"), default)).StatusCode);
            Assert.Equal(400, (await svc.UpdateAsync(null, 1, Req("x"), default)).StatusCode);
            Assert.Equal(400, (await svc.ToggleStatusAsync(" ", 1, default)).StatusCode);
        }

        [Theory]
        [InlineData(0, 10)]
        [InlineData(1, 0)]
        [InlineData(1, 101)]
        public async Task List_RejectsBadPaging(int page, int size) => Assert.Equal(400, (await Make().Svc.ListAsync(A, null, null, page, size, default)).StatusCode);

        [Fact]
        public async Task List_FiltersByStatusAndSearch_AndRejectsAnUnknownStatus()
        {
            var (svc, s) = Make();
            s.AddArea(A, "North"); s.AddArea(A, "Northeast", TransportStore.Inactive); s.AddArea(A, "South");
            Assert.Equal(2, (await svc.ListAsync(A, "north", "All", 1, 10, default)).Data!.Total);
            Assert.Equal(1, (await svc.ListAsync(A, "north", "active", 1, 10, default)).Data!.Total);
            Assert.Equal(400, (await svc.ListAsync(A, null, "bogus", 1, 10, default)).StatusCode);
        }
    }

    public class VehicleServiceTests
    {
        private static (VehicleService Svc, TransportStore Store) Make() { var s = new TransportStore(); return (new VehicleService(new FakeVehicleRepo(s), NullLogger<VehicleService>.Instance), s); }
        private const string A = TransportStore.SchoolA, B = TransportStore.SchoolB;
        private static VehicleRequest Req(string label = "Bus 3", string plate = "UP80 AB 1234", long type = 1, long ownership = 1) => new() { VehicleLabel = label, RegistrationNumber = plate, VehicleTypeId = type, OwnershipId = ownership };

        [Fact]
        public async Task Create_NormalisesThePlate()
        {
            var (svc, _) = Make();
            var r = await svc.CreateAsync(A, Req(), default);
            Assert.Equal(201, r.StatusCode); Assert.Equal("UP80AB1234", r.Data!.RegistrationNumber); Assert.Equal("Bus", r.Data.VehicleTypeName);
        }

        [Fact]
        public async Task PlateAndLabel_MustBeUniqueAmongActiveVehicles_PerSchool()
        {
            var (svc, s) = Make();
            Assert.Equal(201, (await svc.CreateAsync(A, Req(), default)).StatusCode);
            var samePlate = await svc.CreateAsync(A, Req("Bus 9", "up80ab1234"), default);
            Assert.Equal(409, samePlate.StatusCode); Assert.Contains("UP80AB1234", samePlate.Message);
            var sameLabel = await svc.CreateAsync(A, Req("BUS 3", "UP80ZZ9999"), default);
            Assert.Equal(409, sameLabel.StatusCode); Assert.Contains("Bus", sameLabel.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(201, (await svc.CreateAsync(B, Req(), default)).StatusCode);                          // another school may reuse both
            s.AddVehicle(A, "Old Van", "MH12XX0001", TransportStore.Inactive);
            Assert.Equal(201, (await svc.CreateAsync(A, Req("Old Van", "MH12XX0001"), default)).StatusCode);   // inactive ones are free
        }

        [Fact]
        public async Task OwnerDetails_AreOnlyForRentedVehicles()
        {
            var (svc, _) = Make();
            var own = Req(); own.OwnerName = "Vendor";
            var refused = await svc.CreateAsync(A, own, default);
            Assert.Equal(400, refused.StatusCode); Assert.Contains("rented", refused.Message);
            var rented = Req(ownership: 2); rented.OwnerName = "City Travels"; rented.OwnerContact = "+91 98765 43210";
            var ok = await svc.CreateAsync(A, rented, default);
            Assert.Equal(201, ok.StatusCode); Assert.True(ok.Data!.IsRented); Assert.Equal("City Travels", ok.Data.OwnerName);
            var bad = Req("Bus 4", "UP80AB0004", ownership: 2); bad.OwnerContact = "abc";
            Assert.Equal(400, (await svc.CreateAsync(A, bad, default)).StatusCode);
        }

        [Fact]
        public async Task ExpiryBadges_AreDerivedAtReadTime_InTheSchoolTimeZone()
        {
            var (svc, _) = Make();
            var today = SchoolClock.Today();
            var r = Req(); r.PucExpiryDate = today.AddDays(-1); r.InsuranceExpiryDate = today.AddDays(10);
            var created = (await svc.CreateAsync(A, r, default)).Data!;
            Assert.Equal("EXPIRED", created.PucStatus); Assert.Equal(-1, created.PucDaysLeft);
            Assert.Equal("EXPIRING", created.InsuranceStatus); Assert.Equal(10, created.InsuranceDaysLeft);
            var none = (await svc.CreateAsync(A, Req("Bus 5", "UP80AB0005"), default)).Data!;
            Assert.Equal("NOT_SET", none.PucStatus); Assert.Null(none.InsuranceDaysLeft);
            var far = Req("Bus 6", "UP80AB0006"); far.PucExpiryDate = today.AddDays(200);
            Assert.Equal("OK", (await svc.CreateAsync(A, far, default)).Data!.PucStatus);
        }

        [Fact]
        public async Task Validation_RejectsAnUnknownTypeOrOwnership_AFutureServiceDate_AndAFutureModelYear()
        {
            var (svc, _) = Make();
            Assert.Equal(400, (await svc.CreateAsync(A, Req(type: 99), default)).StatusCode);
            Assert.Equal(400, (await svc.CreateAsync(A, Req(ownership: 99), default)).StatusCode);
            var future = Req(); future.LastServiceDate = SchoolClock.Today().AddDays(1);
            Assert.Equal(400, (await svc.CreateAsync(A, future, default)).StatusCode);
            var year = Req(); year.ModelYear = (short)(SchoolClock.Today().Year + 2);
            Assert.Equal(400, (await svc.CreateAsync(A, year, default)).StatusCode);
            Assert.Equal(400, (await svc.CreateAsync(A, Req(plate: "!!"), default)).StatusCode);
        }

        [Fact]
        public async Task Deactivate_IsBlockedWhileAnActiveDriverIsAssigned_AndNamesThem_ButNotForAnInactiveDriver()
        {
            var (svc, s) = Make();
            var bus = s.AddVehicle(A, "Bus 3", "UP80AB1234");
            s.AddDriver(A, "DPS001DR001", "Ramesh Kumar", vehicleId: bus.VehicleId);
            s.AddDriver(A, "DPS001DR002", "Suresh Rao", vehicleId: bus.VehicleId, dl: "DL2");
            var r = await svc.ToggleStatusAsync(A, bus.VehicleId, default);
            Assert.Equal(409, r.StatusCode);
            Assert.Contains("Ramesh Kumar (DPS001DR001)", r.Message); Assert.Contains("Suresh Rao (DPS001DR002)", r.Message);
            s.Drivers.ForEach(d => d.StatusId = TransportStore.Inactive);
            Assert.Equal(200, (await svc.ToggleStatusAsync(A, bus.VehicleId, default)).StatusCode);
            Assert.Equal(TransportStore.Inactive, bus.StatusId);
        }

        [Fact]
        public async Task Reactivate_IsBlockedWhenAnotherActiveVehicleTookTheLabelOrPlate()
        {
            var (svc, s) = Make();
            var old = s.AddVehicle(A, "Bus 3", "UP80AB1234", TransportStore.Inactive);
            s.AddVehicle(A, "Bus 7", "UP80AB1234");
            Assert.Equal(409, (await svc.ToggleStatusAsync(A, old.VehicleId, default)).StatusCode);
            Assert.Equal(TransportStore.Inactive, old.StatusId);
        }

        [Fact]
        public async Task AnotherSchoolsVehicle_IsA404_ForGet_Update_AndToggle()
        {
            var (svc, s) = Make();
            var theirs = s.AddVehicle(B, "Bus 1", "KA01AB0001"); s.AddVehicle(A, "Mine", "UP80AB0001");
            Assert.Equal(404, (await svc.GetAsync(A, theirs.VehicleId, default)).StatusCode);
            Assert.Equal(404, (await svc.UpdateAsync(A, theirs.VehicleId, Req("Hijack", "UP80ZZ0001"), default)).StatusCode);
            Assert.Equal(404, (await svc.ToggleStatusAsync(A, theirs.VehicleId, default)).StatusCode);
            Assert.Equal("Bus 1", theirs.VehicleLabel);
            Assert.Equal(new[] { "Mine" }, (await svc.ListAsync(A, null, "All", null, 1, 10, default)).Data!.Items.Select(i => i.VehicleLabel).ToArray());
        }

        [Fact]
        public async Task Search_FindsByLabelOrByPlate_EvenWithSpacesTyped()
        {
            var (svc, s) = Make();
            s.AddVehicle(A, "Bus 3", "UP80AB1234"); s.AddVehicle(A, "Van 1", "MH12CD5678");
            Assert.Single((await svc.ListAsync(A, "bus", null, null, 1, 10, default)).Data!.Items);
            Assert.Single((await svc.ListAsync(A, "up80 ab", null, null, 1, 10, default)).Data!.Items);
        }

        [Fact]
        public async Task Lookups_FlagWhichOwnershipIsRented()
        {
            var (svc, _) = Make();
            var l = (await svc.GetLookupsAsync(A, default)).Data!;
            Assert.Equal(new[] { "Bus", "Van" }, l.VehicleTypes.Select(t => t.Name).ToArray());
            Assert.Equal(new[] { false, true }, l.Ownerships.Select(o => o.IsRented).ToArray());
        }
    }

    public class DriverServiceTests
    {
        private const string A = TransportStore.SchoolA, B = TransportStore.SchoolB;

        private static (DriverService Svc, TransportStore Store, FakeNotifier Notify) Make()
        {
            var s = new TransportStore(); var n = new FakeNotifier();
            return (new DriverService(new FakeDriverRepo(s), Fake.Of<IFileStorageService>(), n, NullLogger<DriverService>.Instance), s, n);
        }
        private static DriverRequest Req(string name = "Ramesh Kumar", string dl = "DL-0420110149646", string? email = null, long? vehicle = null, params long[] areas) => new()
        {
            FullName = name, MobileNumber = "9876543210", Email = email, Address = "12 Main Road", DlNumber = dl, DlValidUpto = new DateOnly(2035, 5, 1), VehicleId = vehicle, AreaIds = areas.ToList()
        };

        [Fact]
        public async Task Create_GeneratesTheBusinessIdPerSchool_NormalisesTheDl_AndStoresTheAreas()
        {
            var (svc, s, _) = Make();
            var north = s.AddArea(A, "North"); var south = s.AddArea(A, "South");
            var first = (await svc.CreateAsync(A, Req(dl: "dl-04 2011 0149646", areas: new[] { north.AreaId, south.AreaId }), default)).Data!;
            Assert.Equal("DPS001DR001", first.DriverId); Assert.Equal("DL-0420110149646", first.DlNumber); Assert.Equal("Active", first.StatusName);
            Assert.Equal(new[] { "North", "South" }, first.Areas.Select(a => a.AreaName).ToArray()); Assert.False(first.HasLogin);
            Assert.Equal("DPS001DR002", (await svc.CreateAsync(A, Req("Suresh", "DL-0002"), default)).Data!.DriverId);
            Assert.Equal("XYZ002DR001", (await svc.CreateAsync(B, Req("Other School Driver", "DL-0003"), default)).Data!.DriverId);   // its own numbering
        }

        [Fact]
        public async Task Create_RefusesAnotherSchoolsVehicleOrArea_With404_AndInactiveOnesWith400()
        {
            var (svc, s, _) = Make();
            var theirVehicle = s.AddVehicle(B, "Bus 1", "KA01AB0001"); var theirArea = s.AddArea(B, "Far");
            var myInactiveVehicle = s.AddVehicle(A, "Old", "UP80AB0001", TransportStore.Inactive); var myInactiveArea = s.AddArea(A, "Closed", TransportStore.Inactive);
            Assert.Equal(404, (await svc.CreateAsync(A, Req(vehicle: theirVehicle.VehicleId), default)).StatusCode);
            Assert.Equal(404, (await svc.CreateAsync(A, Req(areas: theirArea.AreaId), default)).StatusCode);
            var v = await svc.CreateAsync(A, Req(vehicle: myInactiveVehicle.VehicleId), default);
            Assert.Equal(400, v.StatusCode); Assert.Contains("Old", v.Message);
            var a = await svc.CreateAsync(A, Req(areas: myInactiveArea.AreaId), default);
            Assert.Equal(400, a.StatusCode); Assert.Contains("Closed", a.Message);
            Assert.Empty(s.Drivers);                                                                            // nothing was created by the refused calls
        }

        [Fact]
        public async Task TwoActiveDrivers_MayDriveTheSameVehicle()
        {
            var (svc, s, _) = Make();
            var bus = s.AddVehicle(A, "Bus 3", "UP80AB1234");
            Assert.Equal(201, (await svc.CreateAsync(A, Req("Morning", "DL-0001", vehicle: bus.VehicleId), default)).StatusCode);
            Assert.Equal(201, (await svc.CreateAsync(A, Req("Evening", "DL-0002", vehicle: bus.VehicleId), default)).StatusCode);
            Assert.Equal(2, s.Drivers.Count(d => d.VehicleId == bus.VehicleId));
        }

        [Fact]
        public async Task DlNumber_IsUniqueAmongActiveDrivers_AndEmailIsUniquePerSchool_WhenGiven()
        {
            var (svc, s, _) = Make();
            Assert.Equal(201, (await svc.CreateAsync(A, Req("One", "DL-0001", "a@x.in"), default)).StatusCode);
            Assert.Equal(409, (await svc.CreateAsync(A, Req("Two", "dl-0001"), default)).StatusCode);                    // same licence, any case
            Assert.Equal(409, (await svc.CreateAsync(A, Req("Two", "DL-0002", "A@X.IN"), default)).StatusCode);          // same email, any case
            Assert.Equal(201, (await svc.CreateAsync(A, Req("Three", "DL-0003"), default)).StatusCode);                  // no email
            Assert.Equal(201, (await svc.CreateAsync(A, Req("Four", "DL-0004"), default)).StatusCode);                   // no email again: blanks never clash
            Assert.Equal(201, (await svc.CreateAsync(B, Req("Other", "DL-0001", "a@x.in"), default)).StatusCode);        // another school
            s.Drivers.First(d => d.DlNumber == "DL-0001" && d.SchoolId == A).StatusId = TransportStore.Inactive;
            Assert.Equal(201, (await svc.CreateAsync(A, Req("Five", "DL-0001"), default)).StatusCode);                   // an inactive driver's licence is free
        }

        [Fact]
        public async Task Update_ReplacesThePickupAreaSet_AndDeletesTheUnlistedLinkRows()
        {
            var (svc, s, _) = Make();
            var a1 = s.AddArea(A, "A1"); var a2 = s.AddArea(A, "A2"); var a3 = s.AddArea(A, "A3");
            var id = (await svc.CreateAsync(A, Req(areas: new[] { a1.AreaId, a2.AreaId }), default)).Data!.DriverId;
            var kept = s.Links.First(l => l.AreaId == a2.AreaId).DriverAreaId;
            var r = await svc.UpdateAsync(A, id, Req(areas: new[] { a2.AreaId, a3.AreaId }), default);
            Assert.Equal(200, r.StatusCode);
            Assert.Equal(new[] { "A2", "A3" }, r.Data!.Areas.Select(a => a.AreaName).ToArray());
            Assert.DoesNotContain(s.Links, l => l.AreaId == a1.AreaId);                                         // hard-deleted
            Assert.Equal(kept, s.Links.First(l => l.AreaId == a2.AreaId).DriverAreaId);                          // untouched rows keep their id
            Assert.Empty((await svc.UpdateAsync(A, id, Req(), default)).Data!.Areas);                           // an empty list clears the set
            Assert.Empty(s.Links);
        }

        [Fact]
        public async Task AnotherSchoolsDriver_IsA404_ForGet_Update_Toggle_Photo_AndLogin()
        {
            var (svc, s, _) = Make();
            var theirs = s.AddDriver(B, "XYZ002DR001", "Their Driver");
            Assert.Equal(404, (await svc.GetAsync(A, theirs.DriverId, default)).StatusCode);
            Assert.Equal(404, (await svc.UpdateAsync(A, theirs.DriverId, Req("Hijack", "DL-0009"), default)).StatusCode);
            Assert.Equal(404, (await svc.ToggleStatusAsync(A, theirs.DriverId, default)).StatusCode);
            Assert.Equal(404, (await svc.RemovePhotoAsync(A, theirs.DriverId, default)).StatusCode);
            Assert.Equal(404, (await svc.CreateLoginAsync(A, theirs.DriverId, default)).StatusCode);
            var photos = new DriverPhotoService(new FakeDriverRepo(s), Fake.Of<IFileStorageService>(), NullLogger<DriverPhotoService>.Instance);
            Assert.Equal(404, (await photos.GetAsync(A, theirs.DriverId, default)).StatusCode);
            Assert.Equal("Their Driver", theirs.FullName); Assert.Empty(s.Users);
            Assert.Empty((await svc.ListAsync(A, null, "All", null, 1, 10, default)).Data!.Items);
        }

        [Fact]
        public async Task NoSchoolInTheToken_Is400_OnEveryMethod()
        {
            var (svc, _, _) = Make();
            Assert.Equal(400, (await svc.ListAsync(null, null, null, null, 1, 10, default)).StatusCode);
            Assert.Equal(400, (await svc.GetAsync(" ", "x", default)).StatusCode);
            Assert.Equal(400, (await svc.CreateAsync(null, Req(), default)).StatusCode);
            Assert.Equal(400, (await svc.UpdateAsync("", "x", Req(), default)).StatusCode);
            Assert.Equal(400, (await svc.ToggleStatusAsync(null, "x", default)).StatusCode);
            Assert.Equal(400, (await svc.CreateLoginAsync(null, "x", default)).StatusCode);
            Assert.Equal(400, (await svc.GetLookupsAsync(null, default)).StatusCode);
        }

        // ---------------------------------------------------------------- activation rules
        [Fact]
        public async Task Reactivate_IsBlockedWhileTheVehicleIsInactive_NamingIt()
        {
            var (svc, s, _) = Make();
            var bus = s.AddVehicle(A, "Bus 3", "UP80AB1234", TransportStore.Inactive);
            var d = s.AddDriver(A, "DPS001DR001", "Ramesh", TransportStore.Inactive, bus.VehicleId);
            var r = await svc.ToggleStatusAsync(A, d.DriverId, default);
            Assert.Equal(409, r.StatusCode); Assert.Contains("Bus 3", r.Message); Assert.Contains("inactive", r.Message);
            Assert.Equal(TransportStore.Inactive, d.StatusId);
            bus.StatusId = TransportStore.Active;
            Assert.Equal(200, (await svc.ToggleStatusAsync(A, d.DriverId, default)).StatusCode);
        }

        [Fact]
        public async Task Reactivate_IsBlockedWhileAnAssignedAreaIsInactive_NamingIt()
        {
            var (svc, s, _) = Make();
            var area = s.AddArea(A, "North", TransportStore.Inactive);
            var d = s.AddDriver(A, "DPS001DR001", "Ramesh", TransportStore.Inactive); s.Link(A, d.DriverId, area.AreaId);
            var r = await svc.ToggleStatusAsync(A, d.DriverId, default);
            Assert.Equal(409, r.StatusCode); Assert.Contains("'North'", r.Message);
            Assert.Equal(TransportStore.Inactive, d.StatusId);
        }

        [Fact]
        public async Task Reactivate_IsBlockedWhenAnotherActiveDriverNowHoldsTheLicence()
        {
            var (svc, s, _) = Make();
            var old = s.AddDriver(A, "DPS001DR001", "Old", TransportStore.Inactive, dl: "DL-0007");
            s.AddDriver(A, "DPS001DR002", "New", dl: "DL-0007");
            Assert.Equal(409, (await svc.ToggleStatusAsync(A, old.DriverId, default)).StatusCode);
        }

        [Fact]
        public async Task AnInactiveDriver_MayBeEdited_WithoutHavingToDropItsInactiveVehicleOrAreas()
        {
            var (svc, s, _) = Make();
            var bus = s.AddVehicle(A, "Bus 3", "UP80AB1234", TransportStore.Inactive); var area = s.AddArea(A, "North", TransportStore.Inactive);
            var d = s.AddDriver(A, "DPS001DR001", "Ramesh", TransportStore.Inactive, bus.VehicleId); s.Link(A, d.DriverId, area.AreaId);
            Assert.Equal(200, (await svc.UpdateAsync(A, d.DriverId, Req("Ramesh K", "DL-12345", null, bus.VehicleId, area.AreaId), default)).StatusCode);
            // but an Inactive driver may not be given a NEW inactive one
            var other = s.AddVehicle(A, "Bus 9", "UP80AB9999", TransportStore.Inactive);
            Assert.Equal(400, (await svc.UpdateAsync(A, d.DriverId, Req("Ramesh K", "DL-12345", null, other.VehicleId), default)).StatusCode);
        }

        // ---------------------------------------------------------------- login
        [Fact]
        public async Task CreateLogin_MakesADriverUser_WithATemporaryPasswordShownOnce_AndFirstLoginSet()
        {
            var (svc, s, notify) = Make();
            await svc.CreateAsync(A, Req(email: "ramesh@x.in"), default);
            var r = await svc.CreateLoginAsync(A, "DPS001DR001", default);
            Assert.Equal(201, r.StatusCode);
            Assert.Equal("DPS001DR001", r.Data!.OrgUserId); Assert.Equal("Driver", r.Data.Role); Assert.Equal(12, r.Data.TemporaryPassword.Length);
            var user = Assert.Single(s.Users);
            Assert.Equal("Driver", user.UserType); Assert.Equal(A, user.SchoolId); Assert.True(user.IsFirstLogin); Assert.Equal(TransportStore.Active, user.StatusId);
            Assert.NotEqual(r.Data.TemporaryPassword, user.PasswordHash); Assert.True(PasswordHelper.Verify(r.Data.TemporaryPassword, user.PasswordHash!));   // only the hash is stored
            Assert.Equal(11, Assert.Single(user.UserRoles).RoleId);
            Assert.True(r.Data.EmailSent); Assert.Contains("driver login", notify.Sent[0].Html);
            Assert.True((await svc.GetAsync(A, "DPS001DR001", default)).Data!.HasLogin);
        }

        [Fact]
        public async Task CreateLogin_Rules_OnlyOnce_OnlyForAnActiveDriver_AndNeedsTheRole()
        {
            var (svc, s, _) = Make();
            await svc.CreateAsync(A, Req(), default);
            Assert.Equal(201, (await svc.CreateLoginAsync(A, "DPS001DR001", default)).StatusCode);
            Assert.Equal(409, (await svc.CreateLoginAsync(A, "DPS001DR001", default)).StatusCode);                  // already has one
            var inactive = s.AddDriver(A, "DPS001DR002", "Gone", TransportStore.Inactive, dl: "DL-0002");
            Assert.Equal(409, (await svc.CreateLoginAsync(A, inactive.DriverId, default)).StatusCode);
            s.AddDriver(A, "DPS001DR003", "NoRole", dl: "DL-0003"); s.Roles.Clear();
            Assert.Equal(500, (await svc.CreateLoginAsync(A, "DPS001DR003", default)).StatusCode);                  // role DRIVER missing: a configuration problem, not a user error
            Assert.Single(s.Users);
        }

        [Fact]
        public async Task CreateLogin_DoesNotFailWhenNoEmailCouldBeSent()
        {
            var (svc, _, notify) = Make();
            await svc.CreateAsync(A, Req(email: null), default);
            var r = await svc.CreateLoginAsync(A, "DPS001DR001", default);
            Assert.Equal(201, r.StatusCode); Assert.False(r.Data!.EmailSent); Assert.Empty(notify.Sent);
        }

        [Fact]
        public async Task Toggle_SyncsTheLogin_AndDeactivatingRevokesEveryRefreshToken()
        {
            var (svc, s, _) = Make();
            await svc.CreateAsync(A, Req(), default); await svc.CreateLoginAsync(A, "DPS001DR001", default);
            var user = s.Users.Single();
            s.Tokens.Add(new TbRefreshTokens { RefreshTokenId = 1, UserId = user.UserId, TokenHash = "h1", ExpiresAt = DateTime.UtcNow.AddDays(1) });
            s.Tokens.Add(new TbRefreshTokens { RefreshTokenId = 2, UserId = user.UserId, TokenHash = "h2", ExpiresAt = DateTime.UtcNow.AddDays(1) });
            s.Tokens.Add(new TbRefreshTokens { RefreshTokenId = 3, UserId = 999, TokenHash = "other", ExpiresAt = DateTime.UtcNow.AddDays(1) });

            Assert.Equal("Driver deactivated.", (await svc.ToggleStatusAsync(A, "DPS001DR001", default)).Message);
            Assert.Equal(TransportStore.Inactive, user.StatusId);
            Assert.All(s.Tokens.Where(t => t.UserId == user.UserId), t => Assert.NotNull(t.RevokedAt));
            Assert.Null(s.Tokens.Single(t => t.UserId == 999).RevokedAt);                                         // nobody else's session is touched

            Assert.Equal("Driver activated.", (await svc.ToggleStatusAsync(A, "DPS001DR001", default)).Message);
            Assert.Equal(TransportStore.Active, user.StatusId);
        }

        [Fact]
        public async Task ADeactivatedDriver_CannotSignIn_OrRefresh_AndASignInWorksAgainAfterReactivation()
        {
            var (svc, s, _) = Make();
            await svc.CreateAsync(A, Req(), default);
            var pw = (await svc.CreateLoginAsync(A, "DPS001DR001", default)).Data!.TemporaryPassword;
            var jwt = new JwtTokenGenerator(Options.Create(new JwtSettings { Key = new string('k', 40) }));
            var log = new ListLogger<AuthService>();
            var auth = new AuthService(new FakeAuthRepo(s), jwt, log, new NoThrottle());

            var login = await auth.LoginAsync(new LoginRequest { LoginId = "DPS001DR001", Password = pw }, "1.1.1.1");
            Assert.True(200 == login.StatusCode, login.Message + " :: " + string.Join(" | ", log.Entries));
            var data = Assert.IsType<LoginResponse>(login.Data);
            Assert.Equal(new[] { "Driver" }, data.User.Roles); Assert.Equal("Driver", data.User.UserType); Assert.True(data.User.IsFirstLogin);

            await svc.ToggleStatusAsync(A, "DPS001DR001", default);                                               // deactivate
            Assert.Equal(403, (await auth.LoginAsync(new LoginRequest { LoginId = "DPS001DR001", Password = pw }, "1.1.1.1")).StatusCode);
            Assert.Equal(401, (await auth.RefreshAsync(new RefreshTokenRequest { RefreshToken = data.RefreshToken })).StatusCode);   // the token was revoked with the deactivation

            await svc.ToggleStatusAsync(A, "DPS001DR001", default);                                               // reactivate
            Assert.Equal(200, (await auth.LoginAsync(new LoginRequest { LoginId = "DPS001DR001", Password = pw }, "1.1.1.1")).StatusCode);
        }

        // ---------------------------------------------------------------- list
        [Fact]
        public async Task List_SearchesNameMobileIdAndLicence_FiltersByStatusAndVehicle_AndPages()
        {
            var (svc, s, _) = Make();
            var bus = s.AddVehicle(A, "Bus 3", "UP80AB1234");
            s.AddDriver(A, "DPS001DR001", "Ramesh Kumar", vehicleId: bus.VehicleId, dl: "DL-AAA");
            s.AddDriver(A, "DPS001DR002", "Suresh Rao", dl: "DL-BBB");
            s.AddDriver(A, "DPS001DR003", "Mahesh Nair", TransportStore.Inactive, dl: "DL-CCC");
            Assert.Equal(1, (await svc.ListAsync(A, "ramesh", null, null, 1, 10, default)).Data!.Total);
            Assert.Equal(1, (await svc.ListAsync(A, "DL-BBB", null, null, 1, 10, default)).Data!.Total);
            Assert.Equal(1, (await svc.ListAsync(A, "DR003", null, null, 1, 10, default)).Data!.Total);
            Assert.Equal(3, (await svc.ListAsync(A, "9876", null, null, 1, 10, default)).Data!.Total);
            Assert.Equal(2, (await svc.ListAsync(A, null, "Active", null, 1, 10, default)).Data!.Total);
            Assert.Equal("DPS001DR001", (await svc.ListAsync(A, null, "All", bus.VehicleId, 1, 10, default)).Data!.Items.Single().DriverId);
            Assert.Equal(2, (await svc.ListAsync(A, null, "All", null, 1, 2, default)).Data!.Items.Count);
            Assert.Equal(400, (await svc.ListAsync(A, null, "nope", null, 1, 10, default)).StatusCode);
        }

        [Fact]
        public async Task DlExpiry_IsABadge_NotAStoredValue()
        {
            var (svc, s, _) = Make();
            var d = s.AddDriver(A, "DPS001DR001", "Ramesh"); d.DlValidUpto = SchoolClock.Today().AddDays(-3);
            var detail = (await svc.GetAsync(A, d.DriverId, default)).Data!;
            Assert.Equal("EXPIRED", detail.DlStatus); Assert.Equal(-3, detail.DlDaysLeft);
            d.DlValidUpto = SchoolClock.Today().AddDays(20);
            Assert.Equal("EXPIRING", (await svc.ListAsync(A, null, null, null, 1, 10, default)).Data!.Items.Single().DlStatus);
        }

        [Fact]
        public async Task Lookups_OnlyListTheSchoolsActiveVehiclesAndAreas()
        {
            var (svc, s, _) = Make();
            s.AddVehicle(A, "Bus 3", "UP80AB1234"); s.AddVehicle(A, "Old", "UP80AB0000", TransportStore.Inactive); s.AddVehicle(B, "Theirs", "KA01AB0001");
            s.AddArea(A, "North"); s.AddArea(A, "Closed", TransportStore.Inactive); s.AddArea(B, "Far");
            var l = (await svc.GetLookupsAsync(A, default)).Data!;
            Assert.Equal(new[] { "Bus 3 (UP80AB1234)" }, l.Vehicles.Select(v => v.Name).ToArray());
            Assert.Equal(new[] { "North" }, l.Areas.Select(a => a.Name).ToArray());
        }

        [Fact]
        public async Task Validation_RejectsABadLicenceNumber_AndAnImpossibleValidUptoDate()
        {
            var (svc, _, _) = Make();
            Assert.Equal(400, (await svc.CreateAsync(A, Req(dl: "!!"), default)).StatusCode);
            var r = Req(); r.DlValidUpto = new DateOnly(1990, 1, 1);
            Assert.Equal(400, (await svc.CreateAsync(A, r, default)).StatusCode);
            r.DlValidUpto = SchoolClock.Today().AddYears(60);
            Assert.Equal(400, (await svc.CreateAsync(A, r, default)).StatusCode);
            var ids = Req(); ids.AreaIds = new List<long> { 0 };
            Assert.Equal(400, (await svc.CreateAsync(A, ids, default)).StatusCode);
        }
    }
}

namespace smss_api_tests
{
    /// <summary>Keeps what the code under test logged, so a failing assertion can show the exception behind a 500.</summary>
    public class ListLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<string> Entries = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(formatter(state, exception) + (exception == null ? "" : " -> " + exception.GetType().Name + ": " + exception.Message));
    }
}

namespace smss_api_tests
{
    internal sealed class NoThrottle : smss_api_service_layer.@interface.ILoginThrottle
    {
        public TimeSpan? RetryAfter(string loginId, string clientKey) => null;
        public void RecordFailure(string loginId, string clientKey) { }
        public void RecordSuccess(string loginId) { }
        public void ClearLogin(string loginId) { }
    }
}
