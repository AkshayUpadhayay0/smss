using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using smss_api.Controllers;
using smss_api.Helpers;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>In-memory stand-in for the tables the setup checklist reads. The fake repository filters by school exactly like the real one, so these tests prove tenant isolation too.</summary>
    public class OnbStore
    {
        public const string A = "SCHA", B = "SCHB";
        public const int Active = 1, Inactive = 2;
        public record U(string School, long Id, bool FirstLogin);
        public record Year(string School, long Id, bool Current, int Status);
        public record Cls(string School, long Id, string Name, int Status);
        public record Sec(string School, long ClassId, int Status);
        public record FeeType(string School, bool Active);
        public record Structure(string School, long Year, long ClassId, bool Active, int Lines);
        public record Stu(string School, int Status);

        public List<U> Users = new(); public List<Year> Years = new(); public List<Cls> Classes = new(); public List<Sec> Sections = new();
        public List<FeeType> FeeTypes = new(); public List<Structure> Structures = new(); public List<Stu> Students = new();
        public bool Throw;
        private long _id = 1;
        public long Next() => _id++;

        public OnbStore AddUser(string school, long id, bool firstLogin) { Users.Add(new(school, id, firstLogin)); return this; }
        public long AddYear(string school, bool current = true, int status = Active) { var id = Next(); Years.Add(new(school, id, current, status)); return id; }
        public long AddClass(string school, string name, int status = Active) { var id = Next(); Classes.Add(new(school, id, name, status)); return id; }
        public void AddSection(string school, long classId, int status = Active) => Sections.Add(new(school, classId, status));
        public void AddFeeType(string school, bool active = true) => FeeTypes.Add(new(school, active));
        public void AddStructure(string school, long year, long classId, int lines = 1, bool active = true) => Structures.Add(new(school, year, classId, active, lines));

        /// <summary>Everything a school needs, in one call. Returns (year, class ids).</summary>
        public (long Year, long[] Classes) Complete(string school, int classCount = 2)
        {
            var year = AddYear(school);
            AddFeeType(school);
            var ids = Enumerable.Range(1, classCount).Select(i => AddClass(school, $"Class {i}")).ToArray();
            foreach (var c in ids) { AddSection(school, c); AddStructure(school, year, c); }
            return (year, ids);
        }
    }

    public class FakeOnbRepo : IOnboardingRepository
    {
        private readonly OnbStore _s;
        public FakeOnbRepo(OnbStore s) => _s = s;
        private void Maybe() { if (_s.Throw) throw new InvalidOperationException("db down"); }
        public Task<int?> ActiveStatusIdAsync(CancellationToken ct) { Maybe(); return Task.FromResult<int?>(OnbStore.Active); }
        public Task<bool?> IsFirstLoginAsync(string schoolId, long userId, CancellationToken ct) => Task.FromResult(_s.Users.Where(u => u.School == schoolId && u.Id == userId).Select(u => (bool?)u.FirstLogin).FirstOrDefault());
        public Task<bool> HasActiveStudentAsync(string schoolId, int activeStatusId, CancellationToken ct) => Task.FromResult(_s.Students.Any(s => s.School == schoolId && s.Status == activeStatusId));
        public Task<long?> CurrentYearIdAsync(string schoolId, int activeStatusId, CancellationToken ct) => Task.FromResult(_s.Years.Where(y => y.School == schoolId && y.Current && y.Status == activeStatusId).Select(y => (long?)y.Id).FirstOrDefault());
        // the fake offers every class and section in the current year (offerings themselves are covered by the database tests); no current year = nothing offered
        private bool HasYear(string schoolId) => _s.Years.Any(y => y.School == schoolId && y.Current && y.Status == OnbStore.Active);
        public Task<List<OnboardingClassRow>> ActiveClassesAsync(string schoolId, long? academicYearId, int activeStatusId, CancellationToken ct) => Task.FromResult(academicYearId == null ? new List<OnboardingClassRow>() : _s.Classes.Where(c => c.School == schoolId && c.Status == activeStatusId).Select(c => new OnboardingClassRow(c.Id, c.Name)).ToList());
        public Task<HashSet<long>> ClassIdsWithActiveSectionAsync(string schoolId, long? academicYearId, int activeStatusId, CancellationToken ct) => Task.FromResult(academicYearId == null ? new HashSet<long>() : _s.Sections.Where(c => c.School == schoolId && c.Status == activeStatusId).Select(c => c.ClassId).ToHashSet());
        public Task<bool> HasActiveFeeTypeAsync(string schoolId, CancellationToken ct) => Task.FromResult(_s.FeeTypes.Any(t => t.School == schoolId && t.Active));
        public Task<HashSet<long>> ClassIdsWithFeeStructureAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            Task.FromResult(_s.Structures.Where(f => f.School == schoolId && f.Year == academicYearId && f.Active && f.Lines > 0).Select(f => f.ClassId).ToHashSet());
    }

    public class OnboardingStatusTests
    {
        private static OnboardingService Svc(OnbStore s) => new(new FakeOnbRepo(s), NullLogger<OnboardingService>.Instance);
        private static async Task<OnboardingStatusDto> Status(OnbStore s, string school = OnbStore.A, long user = 1)
        {
            var r = await Svc(s).GetStatusAsync(school, user, default);
            Assert.True(r.Status, r.Message);
            return r.Data!;
        }
        private static OnboardingStepDto Step(OnboardingStatusDto d, string code) => d.Steps.Single(x => x.Code == code);

        [Fact]
        public async Task Steps_come_in_the_agreed_order()
        {
            var d = await Status(new OnbStore().AddUser(OnbStore.A, 1, true));
            Assert.Equal(new[] { "PASSWORD_CHANGED", "ACADEMIC_YEAR", "IMPORT_MASTERS_OR_CLASSES", "SCHOOL_SETUP", "FEE_TYPES", "FEE_STRUCTURE" }, d.Steps.Select(x => x.Code));
        }

        [Fact]
        public async Task Fresh_school_starts_at_the_password()
        {
            var d = await Status(new OnbStore().AddUser(OnbStore.A, 1, firstLogin: true));
            Assert.False(d.IsComplete);
            Assert.Equal("PASSWORD_CHANGED", d.NextStep!.Code);
            Assert.Equal("/change-password", d.NextStep.Route);
            Assert.All(d.Steps.Skip(1), x => Assert.False(x.Done));
        }

        [Fact]
        public async Task Password_step_follows_the_users_first_login_flag_and_only_for_that_school()
        {
            var s = new OnbStore().AddUser(OnbStore.A, 1, false).AddUser(OnbStore.B, 2, true);
            Assert.True(Step(await Status(s, OnbStore.A, 1), "PASSWORD_CHANGED").Done);
            Assert.False(Step(await Status(s, OnbStore.B, 2), "PASSWORD_CHANGED").Done);
            // a user id of another school is "not changed", never read across schools
            Assert.False(Step(await Status(s, OnbStore.A, 2), "PASSWORD_CHANGED").Done);
        }

        [Fact]
        public async Task After_the_password_the_next_step_is_the_academic_year()
        {
            var d = await Status(new OnbStore().AddUser(OnbStore.A, 1, false));
            Assert.Equal("ACADEMIC_YEAR", d.NextStep!.Code);
            Assert.Equal("/school-setup/academic-year", d.NextStep.Route);
        }

        [Theory]
        [InlineData(false, OnbStore.Active)]   // exists but is not the current year
        [InlineData(true, OnbStore.Inactive)]  // current but deactivated
        public async Task Academic_year_step_needs_a_current_active_year(bool current, int status)
        {
            var s = new OnbStore().AddUser(OnbStore.A, 1, false);
            s.AddYear(OnbStore.A, current, status);
            Assert.False(Step(await Status(s), "ACADEMIC_YEAR").Done);
        }

        [Fact]
        public async Task Academic_year_done_then_next_is_import_masters()
        {
            var s = new OnbStore().AddUser(OnbStore.A, 1, false);
            s.AddYear(OnbStore.A);
            var d = await Status(s);
            Assert.True(Step(d, "ACADEMIC_YEAR").Done);
            Assert.Equal("IMPORT_MASTERS_OR_CLASSES", d.NextStep!.Code);
            Assert.Equal("/school-setup/import-masters", d.NextStep.Route);
        }

        [Fact]
        public async Task Import_step_is_satisfied_by_classes_however_they_were_added_but_not_by_inactive_ones()
        {
            var s = new OnbStore().AddUser(OnbStore.A, 1, false);
            s.AddYear(OnbStore.A);
            s.AddClass(OnbStore.A, "Old", OnbStore.Inactive);
            Assert.False(Step(await Status(s), "IMPORT_MASTERS_OR_CLASSES").Done);
            s.AddClass(OnbStore.A, "Class 1");
            Assert.True(Step(await Status(s), "IMPORT_MASTERS_OR_CLASSES").Done);
        }

        [Fact]
        public async Task School_setup_needs_every_active_class_to_have_an_active_section_and_names_the_ones_missing()
        {
            var s = new OnbStore().AddUser(OnbStore.A, 1, false);
            s.AddYear(OnbStore.A);
            var one = s.AddClass(OnbStore.A, "Class 1");
            s.AddClass(OnbStore.A, "Class 2");
            s.AddSection(OnbStore.A, one);
            var d = await Status(s);
            Assert.False(Step(d, "SCHOOL_SETUP").Done);
            Assert.Contains("Class 2", Step(d, "SCHOOL_SETUP").Detail);
            Assert.DoesNotContain("Class 1", Step(d, "SCHOOL_SETUP").Detail);
            Assert.Equal("SCHOOL_SETUP", d.NextStep!.Code);
        }

        [Fact]
        public async Task An_inactive_section_does_not_count()
        {
            var s = new OnbStore().AddUser(OnbStore.A, 1, false);
            var c = s.AddClass(OnbStore.A, "Class 1");
            s.AddSection(OnbStore.A, c, OnbStore.Inactive);
            Assert.False(Step(await Status(s), "SCHOOL_SETUP").Done);
        }

        [Fact]
        public async Task School_setup_is_not_done_with_no_classes()
        {
            Assert.False(Step(await Status(new OnbStore().AddUser(OnbStore.A, 1, false)), "SCHOOL_SETUP").Done);
        }

        [Fact]
        public async Task Fee_type_step_needs_an_active_fee_type()
        {
            var s = new OnbStore().AddUser(OnbStore.A, 1, false);
            s.AddFeeType(OnbStore.A, active: false);
            Assert.False(Step(await Status(s), "FEE_TYPES").Done);
            s.AddFeeType(OnbStore.A);
            Assert.True(Step(await Status(s), "FEE_TYPES").Done);
        }

        [Fact]
        public async Task Fee_structure_step_lists_the_classes_still_missing_and_ignores_empty_inactive_and_other_year_structures()
        {
            var s = new OnbStore().AddUser(OnbStore.A, 1, false);
            var year = s.AddYear(OnbStore.A);
            var otherYear = s.AddYear(OnbStore.A, current: false);
            var c1 = s.AddClass(OnbStore.A, "Class 1");
            var c2 = s.AddClass(OnbStore.A, "Class 2");
            var c3 = s.AddClass(OnbStore.A, "Class 3");
            var c4 = s.AddClass(OnbStore.A, "Class 4");
            s.AddStructure(OnbStore.A, year, c1);                          // counts
            s.AddStructure(OnbStore.A, year, c2, lines: 0);                // no line
            s.AddStructure(OnbStore.A, year, c3, active: false);           // switched off
            s.AddStructure(OnbStore.A, otherYear, c4);                     // not the current year

            var step = Step(await Status(s), "FEE_STRUCTURE");
            Assert.False(step.Done);
            Assert.Contains("Class 2", step.Detail);
            Assert.Contains("Class 3", step.Detail);
            Assert.Contains("Class 4", step.Detail);
            Assert.DoesNotContain("Class 1", step.Detail);
            Assert.Equal("/fee-setup/fee-structures", step.Route);
        }

        [Fact]
        public async Task Fee_structure_detail_is_capped()
        {
            var s = new OnbStore().AddUser(OnbStore.A, 1, false);
            s.AddYear(OnbStore.A);
            for (var i = 1; i <= 12; i++) s.AddClass(OnbStore.A, $"Class {i}");
            var detail = Step(await Status(s), "FEE_STRUCTURE").Detail;
            Assert.Contains("and 4 more", detail);
        }

        [Fact]
        public async Task A_fee_structure_in_the_current_year_is_not_enough_without_a_current_year()
        {
            var s = new OnbStore().AddUser(OnbStore.A, 1, false);
            var c = s.AddClass(OnbStore.A, "Class 1");
            Assert.False(Step(await Status(s), "FEE_STRUCTURE").Done);
            s.AddStructure(OnbStore.A, 999, c);
            Assert.False(Step(await Status(s), "FEE_STRUCTURE").Done);
        }

        [Fact]
        public async Task Everything_done_is_complete_with_no_next_step()
        {
            var s = new OnbStore().AddUser(OnbStore.A, 1, false);
            s.Complete(OnbStore.A);
            var d = await Status(s);
            Assert.True(d.IsComplete);
            Assert.Null(d.NextStep);
            Assert.All(d.Steps, x => Assert.True(x.Done));
        }

        [Fact]
        public async Task Next_step_walks_forward_as_each_step_is_finished()
        {
            var s = new OnbStore().AddUser(OnbStore.A, 1, true);
            Assert.Equal("PASSWORD_CHANGED", (await Status(s)).NextStep!.Code);
            s.Users.Clear(); s.AddUser(OnbStore.A, 1, false);
            Assert.Equal("ACADEMIC_YEAR", (await Status(s)).NextStep!.Code);
            var y = s.AddYear(OnbStore.A);
            Assert.Equal("IMPORT_MASTERS_OR_CLASSES", (await Status(s)).NextStep!.Code);
            var c = s.AddClass(OnbStore.A, "Class 1");
            Assert.Equal("SCHOOL_SETUP", (await Status(s)).NextStep!.Code);
            s.AddSection(OnbStore.A, c);
            Assert.Equal("FEE_TYPES", (await Status(s)).NextStep!.Code);
            s.AddFeeType(OnbStore.A);
            Assert.Equal("FEE_STRUCTURE", (await Status(s)).NextStep!.Code);
            s.AddStructure(OnbStore.A, y, c);
            Assert.True((await Status(s)).IsComplete);
        }

        [Fact]
        public async Task A_school_with_an_active_student_is_complete_whatever_the_steps_say_but_steps_stay_honest()
        {
            var s = new OnbStore().AddUser(OnbStore.A, 1, false);
            s.Students.Add(new(OnbStore.A, OnbStore.Active));
            var d = await Status(s);
            Assert.True(d.IsComplete);
            Assert.Null(d.NextStep);
            Assert.False(Step(d, "ACADEMIC_YEAR").Done);
            Assert.False(Step(d, "FEE_TYPES").Done);
        }

        [Fact]
        public async Task An_inactive_student_does_not_grandfather_the_school()
        {
            var s = new OnbStore().AddUser(OnbStore.A, 1, false);
            s.Students.Add(new(OnbStore.A, OnbStore.Inactive));
            Assert.False((await Status(s)).IsComplete);
        }

        [Fact]
        public async Task Grandfathering_and_progress_never_leak_between_schools()
        {
            var s = new OnbStore().AddUser(OnbStore.A, 1, false).AddUser(OnbStore.B, 2, false);
            s.Complete(OnbStore.A);                       // A is fully set up
            s.Students.Add(new(OnbStore.A, OnbStore.Active));
            Assert.True((await Status(s, OnbStore.A, 1)).IsComplete);

            var b = await Status(s, OnbStore.B, 2);       // B has nothing of its own
            Assert.False(b.IsComplete);
            Assert.Equal("ACADEMIC_YEAR", b.NextStep!.Code);
            Assert.All(b.Steps.Skip(1), x => Assert.False(x.Done));

            // and B's progress does not change A's missing list
            var c = s.AddClass(OnbStore.B, "Only in B");
            var a = await Status(s, OnbStore.A, 1);
            Assert.DoesNotContain("Only in B", string.Join(" ", a.Steps.Select(x => x.Detail)));
            Assert.True(c > 0);
        }

        [Fact]
        public async Task Another_schools_classes_do_not_appear_in_the_missing_list()
        {
            var s = new OnbStore().AddUser(OnbStore.A, 1, false);
            s.AddYear(OnbStore.A);
            s.AddClass(OnbStore.A, "A Class");
            s.AddClass(OnbStore.B, "B Class");
            var detail = Step(await Status(s), "FEE_STRUCTURE").Detail;
            Assert.Contains("A Class", detail);
            Assert.DoesNotContain("B Class", detail);
        }

        [Fact]
        public async Task No_school_on_the_token_is_a_400()
        {
            var r = await Svc(new OnbStore()).GetStatusAsync(null, 1, default);
            Assert.Equal(400, r.StatusCode);
            Assert.False(r.Status);
        }

        [Fact]
        public async Task Status_reports_a_safe_500_when_the_database_fails()
        {
            var r = await Svc(new OnbStore { Throw = true }).GetStatusAsync(OnbStore.A, 1, default);
            Assert.Equal(500, r.StatusCode);
            Assert.DoesNotContain("db down", r.Message);
        }

        [Fact]
        public async Task Gate_is_open_for_a_complete_school_and_closed_with_the_next_step_otherwise()
        {
            var s = new OnbStore();
            s.Complete(OnbStore.A);
            Assert.Null(await Svc(s).CheckSetupAsync(OnbStore.A, default));

            var blocked = await Svc(s).CheckSetupAsync(OnbStore.B, default);
            Assert.NotNull(blocked);
            Assert.Equal(409, blocked!.StatusCode);
            Assert.False(blocked.Status);
            Assert.Equal("SETUP_INCOMPLETE", blocked.Data!.Code);
            Assert.Equal("ACADEMIC_YEAR", blocked.Data.NextStep!.Code);
        }

        [Fact]
        public async Task Gate_does_not_depend_on_the_password_step()
        {
            // the gate runs after the first-login middleware, so it never asks about the password
            var s = new OnbStore().AddUser(OnbStore.A, 1, true);
            s.Complete(OnbStore.A);
            Assert.Null(await Svc(s).CheckSetupAsync(OnbStore.A, default));
        }

        [Fact]
        public async Task Gate_fails_closed_when_the_status_cannot_be_worked_out()
        {
            var blocked = await Svc(new OnbStore { Throw = true }).CheckSetupAsync(OnbStore.A, default);
            Assert.NotNull(blocked);
            Assert.Equal(409, blocked!.StatusCode);
            Assert.Equal("SETUP_INCOMPLETE", blocked.Data!.Code);
            Assert.DoesNotContain("db down", blocked.Message);
        }
    }

    public class OnboardingEnforcementTests
    {
        private const string Admin = "School Admin";

        /// <summary>One guarded endpoint: how to call it and which service method must (not) be reached.</summary>
        public sealed record Case(string Name, string ServiceMethod, Func<IOnboardingService, ClaimsPrincipal, (Func<Task<IActionResult>> Run, object Service)> Make);

        private static IFormFile Xlsx() => new FormFile(new MemoryStream(new byte[] { 1, 2, 3 }), 0, 3, "file", "students.xlsx");
        private static readonly CancellationToken Ct = default;

        public static readonly Case[] Cases =
        {
            new("new student (existing student entry)", "CreateDirectEntryAsync", (onb, user) =>
            {
                var svc = Fake.Of<IStudentOnboardingService>();
                var c = Fake.WithUser(new StudentOnboardingController(svc, onb), user);
                return (() => c.CreateExistingStudent(OnbStore.A, new DirectStudentEntryRequestDto(), Ct), svc);
            }),
            new("student import commit", "CommitAsync", (onb, user) =>
            {
                var svc = Fake.Of<IStudentImportService>();
                var c = Fake.WithUser(new StudentImportController(svc, onb), user);
                return (() => c.Commit(Xlsx(), Ct), svc);
            }),
            new("admission submit", "SubmitAsync", (onb, user) =>
            {
                var svc = Fake.Of<IAdmissionService>();
                var c = Fake.WithUser(new AdmissionController(svc, Fake.Of<IAdmissionDocumentService>(), onb), user);
                return (() => c.Submit(OnbStore.A, new SubmitAdmissionRequestDto(), Ct), svc);
            }),
            new("admission confirm (creates the student)", "ConfirmAdmissionAsync", (onb, user) =>
            {
                var svc = Fake.Of<IAdmissionService>();
                var c = Fake.WithUser(new AdmissionController(svc, Fake.Of<IAdmissionDocumentService>(), onb), user);
                return (() => c.Confirm(OnbStore.A, 1, new ConfirmAdmissionRequestDto(), Ct), svc);
            }),
            new("employee create", "CreateAsync", (onb, user) =>
            {
                var svc = Fake.Of<IEmployeeService>();
                var c = Fake.WithUser(new EmployeeController(svc, Fake.Of<IEmployeePhotoService>(), onb), user);
                return (() => c.Create(new EmployeeRequest(), Ct), svc);
            }),
            new("transport area create", "CreateAsync", (onb, user) =>
            {
                var svc = Fake.Of<ITransportAreaService>();
                var c = Fake.WithUser(new TransportAreasController(svc, onb), user);
                return (() => c.Create(new TransportAreaRequest(), Ct), svc);
            }),
            new("vehicle create", "CreateAsync", (onb, user) =>
            {
                var svc = Fake.Of<IVehicleService>();
                var c = Fake.WithUser(new VehiclesController(svc, onb), user);
                return (() => c.Create(new VehicleRequest(), Ct), svc);
            }),
            new("driver create", "CreateAsync", (onb, user) =>
            {
                var svc = Fake.Of<IDriverService>();
                var c = Fake.WithUser(new DriversController(svc, Fake.Of<IDriverPhotoService>(), onb), user);
                return (() => c.Create(new DriverRequest(), Ct), svc);
            }),
        };

        public static IEnumerable<object[]> AllCases() => Cases.Select(c => new object[] { c.Name });
        private static Case Find(string name) => Cases.Single(c => c.Name == name);
        private static IOnboardingService Onb(OnbStore s) => new OnboardingService(new FakeOnbRepo(s), NullLogger<OnboardingService>.Instance);
        private static ClaimsPrincipal AdminOf(string school) => Fake.User(school, 1, Admin);
        private static bool Reached(object svc, string method) => Fake.ProxyOf(svc).Calls.Contains(method);

        [Theory, MemberData(nameof(AllCases))]
        public async Task Incomplete_school_gets_409_SETUP_INCOMPLETE_with_the_next_step_and_the_service_is_never_reached(string name)
        {
            var c = Find(name);
            var (run, svc) = c.Make(Onb(new OnbStore()), AdminOf(OnbStore.A));
            var result = await run();

            Assert.Equal(409, Fake.Code(result));
            var body = Assert.IsType<ApiResponse<SetupIncompleteDto>>(((ObjectResult)result).Value);
            Assert.False(body.Status);
            Assert.Equal(409, body.StatusCode);
            Assert.Equal("SETUP_INCOMPLETE", body.Data!.Code);
            Assert.Equal("ACADEMIC_YEAR", body.Data.NextStep!.Code);
            Assert.Equal("/school-setup/academic-year", body.Data.NextStep.Route);
            Assert.False(Reached(svc, c.ServiceMethod), "the write must not run");
        }

        [Theory, MemberData(nameof(AllCases))]
        public async Task Complete_school_goes_through_to_the_service(string name)
        {
            var c = Find(name);
            var store = new OnbStore();
            store.Complete(OnbStore.A);
            var (run, svc) = c.Make(Onb(store), AdminOf(OnbStore.A));
            var result = await run();

            Assert.NotEqual(409, Fake.Code(result));
            Assert.True(Reached(svc, c.ServiceMethod));
        }

        [Theory, MemberData(nameof(AllCases))]
        public async Task A_school_with_students_goes_through_without_any_setup(string name)
        {
            var c = Find(name);
            var store = new OnbStore();
            store.Students.Add(new(OnbStore.A, OnbStore.Active));
            var (run, svc) = c.Make(Onb(store), AdminOf(OnbStore.A));
            var result = await run();

            Assert.NotEqual(409, Fake.Code(result));
            Assert.True(Reached(svc, c.ServiceMethod));
        }

        [Theory, MemberData(nameof(AllCases))]
        public async Task Another_schools_setup_does_not_open_the_gate(string name)
        {
            var c = Find(name);
            var store = new OnbStore();
            store.Complete(OnbStore.B);                     // B is complete, the caller is A
            var (run, svc) = c.Make(Onb(store), AdminOf(OnbStore.A));
            Assert.Equal(409, Fake.Code(await run()));
            Assert.False(Reached(svc, c.ServiceMethod));
        }

        [Theory, MemberData(nameof(AllCases))]
        public async Task The_gate_fails_closed_on_a_database_error(string name)
        {
            var c = Find(name);
            var (run, svc) = c.Make(Onb(new OnbStore { Throw = true }), AdminOf(OnbStore.A));
            Assert.Equal(409, Fake.Code(await run()));
            Assert.False(Reached(svc, c.ServiceMethod));
        }

        [Fact]
        public async Task A_caller_without_the_role_still_gets_403_not_409()
        {
            // the role check runs before the gate, so an unauthorised caller learns nothing about the school's setup
            var onb = Onb(new OnbStore());
            var teacher = Fake.User(OnbStore.A, 5, "Teacher");
            foreach (var name in new[] { "employee create", "transport area create", "vehicle create", "driver create", "student import commit", "new student (existing student entry)" })
            {
                var (run, _) = Find(name).Make(onb, teacher);
                Assert.Equal(403, Fake.Code(await run()));
            }
        }

        [Fact]
        public async Task Super_Admin_is_exempt_from_the_gate_even_for_an_incomplete_school()
        {
            var onb = Onb(new OnbStore());
            var host = Fake.WithUser(new OnboardingController(onb), Fake.User(null, 1, "Super Admin"));
            Assert.Null(await host.RequireSetupAsync(onb, Ct));

            // the same caller as a School Admin is blocked
            var admin = Fake.WithUser(new OnboardingController(onb), AdminOf(OnbStore.A));
            var blocked = await admin.RequireSetupAsync(onb, Ct);
            Assert.NotNull(blocked);
            Assert.Equal(409, Fake.Code(blocked!));
        }

        [Fact]
        public async Task A_token_without_a_school_is_left_to_the_endpoint_to_refuse()
        {
            var onb = Onb(new OnbStore());
            var host = Fake.WithUser(new OnboardingController(onb), Fake.User(null, 1, Admin));
            Assert.Null(await host.RequireSetupAsync(onb, Ct));
        }

    }

    public class OnboardingControllerTests
    {
        private static OnboardingController Make(string? school, long user, params string[] roles)
        {
            var s = new OnbStore().AddUser(OnbStore.A, user, false);
            return Fake.WithUser(new OnboardingController(new OnboardingService(new FakeOnbRepo(s), NullLogger<OnboardingService>.Instance)), Fake.User(school, user, roles));
        }

        [Theory]
        [InlineData("School Admin")]
        [InlineData("Principal")]
        [InlineData("Accountant")]
        public async Task Admin_Principal_and_Accountant_can_read_the_status(string role)
        {
            var r = await Make(OnbStore.A, 7, role).Status(default);
            Assert.Equal(200, Fake.Code(r));
            var body = Assert.IsType<ApiResponse<OnboardingStatusDto>>(((ObjectResult)r).Value);
            Assert.NotNull(body.Data);
            Assert.Equal(6, body.Data!.Steps.Count);
        }

        [Theory]
        [InlineData("Teacher")]
        [InlineData("Student")]
        [InlineData("Guardian")]
        [InlineData("Admission Staff")]
        [InlineData("Driver")]
        [InlineData("Super Admin")]
        public async Task Other_roles_are_refused(string role)
        {
            Assert.Equal(403, Fake.Code(await Make(OnbStore.A, 7, role).Status(default)));
        }

        [Fact]
        public async Task The_school_comes_from_the_token_only()
        {
            // the endpoint has no school parameter at all; a token for school B reads school B's (empty) state, never A's
            var s = new OnbStore().AddUser(OnbStore.A, 1, false).AddUser(OnbStore.B, 2, false);
            s.Complete(OnbStore.A);
            var c = Fake.WithUser(new OnboardingController(new OnboardingService(new FakeOnbRepo(s), NullLogger<OnboardingService>.Instance)), Fake.User(OnbStore.B, 2, "School Admin"));
            var body = Assert.IsType<ApiResponse<OnboardingStatusDto>>(((ObjectResult)await c.Status(default)).Value);
            Assert.False(body.Data!.IsComplete);
            Assert.Equal("ACADEMIC_YEAR", body.Data.NextStep!.Code);
            Assert.Empty(typeof(OnboardingController).GetMethod(nameof(OnboardingController.Status))!.GetParameters().Where(p => p.ParameterType == typeof(string)));
        }

        [Fact]
        public async Task A_token_with_no_school_is_a_400()
        {
            Assert.Equal(400, Fake.Code(await Make(null, 7, "School Admin").Status(default)));
        }
    }
}
