using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using smss_api.Controllers;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    // ======================================================================================================================================
    // Stage 3: students and transport areas, and the student's own "Where is my bus" answer. The in-memory repository filters by school exactly like the real one.
    // What it cannot prove is the database (the unique (student, year), composite foreign keys): the scripted live check against smss-db-dev covers those.
    // ======================================================================================================================================
    internal sealed class FakeStudentTransportRepo : IStudentTransportRepository
    {
        public readonly TransportStore Store;
        public readonly List<TbStudentTransport> Rows = new();
        public readonly List<(string School, string StudentId, string Name)> Students = new();
        public readonly List<(string School, long Id, string Name, bool Current)> Years = new();
        private long _id = 1;

        public FakeStudentTransportRepo(TransportStore store) => Store = store;

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) => Task.FromResult<int?>(statusName == "Active" ? TransportStore.Active : TransportStore.Inactive);
        public Task<TransportStudentRef?> GetStudentAsync(string schoolId, string studentId, CancellationToken ct) =>
            Task.FromResult(Students.Where(s => s.School == schoolId && s.StudentId == studentId).Select(s => new TransportStudentRef(s.StudentId, s.Name)).FirstOrDefault());
        public Task<string?> GetStudentIdForLoginAsync(string schoolId, long userId, CancellationToken ct)
        {
            var u = Store.Users.FirstOrDefault(x => x.UserId == userId && x.SchoolId == schoolId && x.UserType == "Student");
            return Task.FromResult(u?.OrgUserId != null && Students.Any(s => s.School == schoolId && s.StudentId == u.OrgUserId) ? u.OrgUserId : null);
        }
        public Task<TransportYearRef?> GetCurrentYearAsync(string schoolId, CancellationToken ct) =>
            Task.FromResult(Years.Where(y => y.School == schoolId && y.Current).Select(y => new TransportYearRef(y.Id, y.Name, y.Current)).FirstOrDefault());
        public Task<TransportYearRef?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            Task.FromResult(Years.Where(y => y.School == schoolId && y.Id == academicYearId).Select(y => new TransportYearRef(y.Id, y.Name, y.Current)).FirstOrDefault());
        public Task<AreaRef?> GetAreaAsync(string schoolId, long areaId, CancellationToken ct) =>
            Task.FromResult(Store.Areas.Where(a => a.SchoolId == schoolId && a.AreaId == areaId).Select(a => new AreaRef(a.AreaId, a.AreaName, a.StatusId)).FirstOrDefault());
        public Task<StudentAssignmentRow?> GetAssignmentAsync(string schoolId, string studentId, long academicYearId, CancellationToken ct)
        {
            var r = Rows.FirstOrDefault(x => x.SchoolId == schoolId && x.StudentId == studentId && x.AcademicYearId == academicYearId);
            var a = r == null ? null : Store.Areas.First(x => x.SchoolId == schoolId && x.AreaId == r.AreaId);
            return Task.FromResult(r == null ? null : new StudentAssignmentRow(r, a!.AreaName, a.StatusId));
        }
        public Task<TbStudentTransport?> GetForUpdateAsync(string schoolId, string studentId, long academicYearId, CancellationToken ct) =>
            Task.FromResult(Rows.FirstOrDefault(x => x.SchoolId == schoolId && x.StudentId == studentId && x.AcademicYearId == academicYearId));
        public Task AddAsync(TbStudentTransport row, CancellationToken ct)
        {
            if (Rows.Any(x => x.StudentId == row.StudentId && x.AcademicYearId == row.AcademicYearId)) throw new InvalidOperationException("the unique (student, year) would refuse this");
            row.StudentTransportId = _id++;
            Rows.Add(row);
            return Task.CompletedTask;
        }
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(1);
    }

    public class StudentTransportServiceTests
    {
        private const string SchoolA = TransportStore.SchoolA, SchoolB = TransportStore.SchoolB;
        private const long StudentUser = 801, OtherSchoolStudentUser = 802, DriverUser = 501;

        private readonly FakeDutyRepo _duty = new();
        private readonly FakeStudentTransportRepo _repo;
        private readonly ManualClock _clock = new();
        private readonly TransportLiveOptions _o = new();
        private readonly StudentTransportService _svc;
        private readonly long _civil, _ring, _old, _bImpostor;
        private readonly long _yearA = 5, _yearB = 6;

        public StudentTransportServiceTests()
        {
            _repo = new FakeStudentTransportRepo(_duty.Store);
            _svc = new StudentTransportService(_repo, _duty, Options.Create(_o), _clock, NullLogger<StudentTransportService>.Instance);
            var s = _duty.Store;
            _civil = s.AddArea(SchoolA, "Civil Lines").AreaId;
            _ring = s.AddArea(SchoolA, "Ring Road").AreaId;
            _old = s.AddArea(SchoolA, "Old City", status: TransportStore.Inactive).AreaId;
            _bImpostor = s.AddArea(SchoolB, "Elsewhere").AreaId;
            _repo.Years.Add((SchoolA, _yearA, "2026-27", true));
            _repo.Years.Add((SchoolA, 4, "2025-26", false));
            _repo.Years.Add((SchoolB, _yearB, "2026-27", true));
            _repo.Students.Add((SchoolA, "DPS001ST001", "Asha Kid"));
            _repo.Students.Add((SchoolB, "XYZ002ST001", "Other School Kid"));
            s.Users.Add(new TbUsers { UserId = StudentUser, SchoolId = SchoolA, UserType = "Student", OrgUserId = "DPS001ST001", DisplayName = "Asha Kid" });
            s.Users.Add(new TbUsers { UserId = OtherSchoolStudentUser, SchoolId = SchoolB, UserType = "Student", OrgUserId = "XYZ002ST001", DisplayName = "Other School Kid" });
        }

        private DateTime Utc => _clock.Now.UtcDateTime;

        // ------------------------------------------------------------------------------------------------ assignment
        [Fact]
        public async Task Get_without_an_assignment_is_null_for_the_current_year()
        {
            var r = await _svc.GetAsync(SchoolA, "DPS001ST001", null, default);
            Assert.Equal(200, r.StatusCode);
            Assert.Null(r.Data!.Assignment);
            Assert.Equal((_yearA, "2026-27", true, "Asha Kid"), (r.Data.AcademicYearId, r.Data.AcademicYearName, r.Data.IsCurrentYear, r.Data.StudentName));
        }

        [Fact]
        public async Task Assign_creates_the_row_for_the_current_year_by_default_then_reads_back()
        {
            var r = await _svc.AssignAsync(SchoolA, "DPS001ST001", new() { AreaId = _civil }, default);
            Assert.Equal(201, r.StatusCode);
            Assert.Equal("Civil Lines", r.Data!.Assignment!.AreaName);
            var row = Assert.Single(_repo.Rows);
            Assert.Equal((SchoolA, "DPS001ST001", _yearA, _civil, TransportStore.Active), (row.SchoolId, row.StudentId, row.AcademicYearId, row.AreaId, row.StatusId));
            Assert.Equal("Civil Lines", (await _svc.GetAsync(SchoolA, "DPS001ST001", null, default)).Data!.Assignment!.AreaName);
        }

        [Fact]
        public async Task Changing_the_area_updates_the_same_row_never_a_second_one()
        {
            await _svc.AssignAsync(SchoolA, "DPS001ST001", new() { AreaId = _civil }, default);
            var id = _repo.Rows[0].StudentTransportId;
            var r = await _svc.AssignAsync(SchoolA, "DPS001ST001", new() { AreaId = _ring }, default);
            Assert.Equal(200, r.StatusCode);
            var row = Assert.Single(_repo.Rows);
            Assert.Equal((id, _ring), (row.StudentTransportId, row.AreaId));
        }

        [Fact]
        public async Task Remove_is_a_soft_delete_and_assigning_again_REACTIVATES_the_same_row()
        {
            await _svc.AssignAsync(SchoolA, "DPS001ST001", new() { AreaId = _civil }, default);
            var id = _repo.Rows[0].StudentTransportId;

            var removed = await _svc.RemoveAsync(SchoolA, "DPS001ST001", null, default);
            Assert.Equal(200, removed.StatusCode);
            Assert.Null(removed.Data!.Assignment);
            var row = Assert.Single(_repo.Rows);                                       // the row is still there ...
            Assert.Equal(TransportStore.Inactive, row.StatusId);                        // ... marked Inactive

            var again = await _svc.AssignAsync(SchoolA, "DPS001ST001", new() { AreaId = _ring }, default);
            Assert.Equal(200, again.StatusCode);                                        // an update of the old row, not a creation
            var after = Assert.Single(_repo.Rows);
            Assert.Equal((id, TransportStore.Active, _ring), (after.StudentTransportId, after.StatusId, after.AreaId));
        }

        [Fact]
        public async Task Removing_what_is_not_there_is_a_200_with_no_assignment()
        {
            var r = await _svc.RemoveAsync(SchoolA, "DPS001ST001", null, default);
            Assert.Equal(200, r.StatusCode);
            Assert.Null(r.Data!.Assignment);
            Assert.Empty(_repo.Rows);
            Assert.Equal(200, (await _svc.RemoveAsync(SchoolA, "DPS001ST001", null, default)).StatusCode);
        }

        [Fact]
        public async Task Assign_refuses_an_inactive_area_with_400_and_writes_nothing()
        {
            var r = await _svc.AssignAsync(SchoolA, "DPS001ST001", new() { AreaId = _old }, default);
            Assert.Equal(400, r.StatusCode);
            Assert.Contains("inactive", r.Message);
            Assert.Empty(_repo.Rows);
        }

        [Fact]
        public async Task Another_schools_student_area_or_year_is_a_404_and_nothing_is_written()
        {
            Assert.Equal(404, (await _svc.GetAsync(SchoolA, "XYZ002ST001", null, default)).StatusCode);                          // another school's student
            Assert.Equal(404, (await _svc.AssignAsync(SchoolA, "XYZ002ST001", new() { AreaId = _civil }, default)).StatusCode);
            Assert.Equal(404, (await _svc.AssignAsync(SchoolA, "DPS001ST001", new() { AreaId = _bImpostor }, default)).StatusCode);   // another school's area
            Assert.Equal(404, (await _svc.AssignAsync(SchoolA, "DPS001ST001", new() { AreaId = _civil, AcademicYearId = _yearB }, default)).StatusCode);   // another school's year
            Assert.Equal(404, (await _svc.RemoveAsync(SchoolA, "XYZ002ST001", null, default)).StatusCode);
            Assert.Equal(404, (await _svc.GetAsync(SchoolA, "DPS001ST001", _yearB, default)).StatusCode);
            Assert.Equal(404, (await _svc.AssignAsync(SchoolA, "DPS001ST001", new() { AreaId = 99999 }, default)).StatusCode);
            Assert.Empty(_repo.Rows);
        }

        [Fact]
        public async Task A_given_academic_year_is_used_and_each_year_has_its_own_row()
        {
            await _svc.AssignAsync(SchoolA, "DPS001ST001", new() { AreaId = _civil }, default);
            var past = await _svc.AssignAsync(SchoolA, "DPS001ST001", new() { AreaId = _ring, AcademicYearId = 4 }, default);
            Assert.Equal(201, past.StatusCode);
            Assert.False(past.Data!.IsCurrentYear);
            Assert.Equal(2, _repo.Rows.Count);
            Assert.Equal("Civil Lines", (await _svc.GetAsync(SchoolA, "DPS001ST001", null, default)).Data!.Assignment!.AreaName);
            Assert.Equal("Ring Road", (await _svc.GetAsync(SchoolA, "DPS001ST001", 4, default)).Data!.Assignment!.AreaName);
        }

        [Fact]
        public async Task Without_a_current_year_the_default_is_a_409_and_a_missing_area_or_school_is_a_400()
        {
            _repo.Years.RemoveAll(y => y.School == SchoolA && y.Current);
            Assert.Equal(409, (await _svc.AssignAsync(SchoolA, "DPS001ST001", new() { AreaId = _civil }, default)).StatusCode);
            Assert.Equal(409, (await _svc.GetAsync(SchoolA, "DPS001ST001", null, default)).StatusCode);
            Assert.Equal(400, (await _svc.AssignAsync(SchoolA, "DPS001ST001", new(), default)).StatusCode);
            Assert.Equal(400, (await _svc.AssignAsync(SchoolA, "DPS001ST001", null, default)).StatusCode);
            Assert.Equal(400, (await _svc.AssignAsync(null, "DPS001ST001", new() { AreaId = _civil }, default)).StatusCode);
            Assert.Equal(400, (await _svc.GetAsync(" ", "DPS001ST001", null, default)).StatusCode);
        }

        [Fact]
        public async Task An_area_deactivated_after_the_assignment_is_shown_as_inactive_so_the_school_can_move_the_student()
        {
            await _svc.AssignAsync(SchoolA, "DPS001ST001", new() { AreaId = _civil }, default);
            _duty.Store.Areas.First(a => a.AreaId == _civil).StatusId = TransportStore.Inactive;
            var a = (await _svc.GetAsync(SchoolA, "DPS001ST001", null, default)).Data!.Assignment!;
            Assert.False(a.AreaIsActive);
        }

        // ------------------------------------------------------------------------------------------------ my-bus
        private (long User, string DriverId) AddDriver(string school, string id, string name, string vehicleLabel, string plate, params long[] areas)
        {
            var s = _duty.Store;
            var v = s.AddVehicle(school, vehicleLabel, plate);
            s.AddDriver(school, id, name, vehicleId: v.VehicleId, dl: "DL" + id);
            foreach (var a in areas) s.Link(school, id, a);
            var user = 900 + s.Users.Count;
            s.Users.Add(new TbUsers { UserId = user, SchoolId = school, UserType = "Driver", OrgUserId = id, DisplayName = name });
            return (user, id);
        }

        /// <summary>Sets the approximate times of the link row (driver, area), "HH:mm" or null.</summary>
        private void SetTimes(string driverId, long areaId, string? pickup, string? drop)
        {
            var link = _duty.Store.Links.First(l => l.DriverId == driverId && l.AreaId == areaId);
            link.MorningPickupTime = pickup == null ? null : TimeOnly.ParseExact(pickup, "HH:mm");
            link.AfternoonDropTime = drop == null ? null : TimeOnly.ParseExact(drop, "HH:mm");
        }

        private async Task<string> OnDuty(string school, long user)
        {
            var dutySvc = new DriverDutyService(_duty, Options.Create(_o), _clock, NullLogger<DriverDutyService>.Instance);
            var r = await dutySvc.StartAsync(school, user, default);
            Assert.True(r.Status, r.Message);
            return r.Data!.TrackingToken;
        }

        private async Task Ping(string token, double lat, double lng = 80.9)
        {
            var dutySvc = new DriverDutyService(_duty, Options.Create(_o), _clock, NullLogger<DriverDutyService>.Instance);
            var r = await dutySvc.RecordLocationAsync(token, new DutyLocationRequest { Latitude = lat, Longitude = lng, RecordedAt = _clock.Now, AccuracyMeters = 8 }, default);
            Assert.True(r.Data!.Accepted, r.Message);
        }

        private async Task AssignStudentTo(long areaId) => Assert.True((await _svc.AssignAsync(SchoolA, "DPS001ST001", new() { AreaId = areaId }, default)).Status);

        [Fact]
        public async Task My_bus_for_a_student_with_no_assignment_is_a_200_not_assigned()
        {
            var r = await _svc.GetMyBusAsync(SchoolA, StudentUser, default);
            Assert.Equal(200, r.StatusCode);
            Assert.False(r.Data!.Assigned);
            Assert.Null(r.Data.Area);
            Assert.Empty(r.Data.Buses);
            Assert.NotNull(r.Data.Message);
        }

        [Fact]
        public async Task My_bus_after_the_assignment_is_removed_is_not_assigned_again()
        {
            await AssignStudentTo(_civil);
            await _svc.RemoveAsync(SchoolA, "DPS001ST001", null, default);
            Assert.False((await _svc.GetMyBusAsync(SchoolA, StudentUser, default)).Data!.Assigned);
        }

        [Fact]
        public async Task My_bus_with_nobody_on_duty_says_so()
        {
            await AssignStudentTo(_civil);
            AddDriver(SchoolA, "DPS001DR001", "Ramesh Kumar", "Bus 1", "UP32AB1234", _civil);              // covers the area but is off duty
            var r = (await _svc.GetMyBusAsync(SchoolA, StudentUser, default)).Data!;
            Assert.True(r.Assigned);
            Assert.Equal("Civil Lines", r.Area!.AreaName);
            Assert.Empty(r.Buses);
            Assert.Contains("No bus is on duty", r.Message);
        }

        [Fact]
        public async Task My_bus_lists_ONLY_the_drivers_on_duty_who_cover_the_students_area()
        {
            await AssignStudentTo(_civil);
            var (u1, _) = AddDriver(SchoolA, "DPS001DR001", "Ramesh Kumar", "Bus 1", "UP32AB1234", _civil);          // covers Civil Lines
            var (u2, _) = AddDriver(SchoolA, "DPS001DR002", "Suresh Yadav", "Van 2", "UP32CD2222", _civil, _ring);    // covers Civil Lines and Ring Road
            var (u3, _) = AddDriver(SchoolA, "DPS001DR003", "Anil Singh", "Bus 3", "UP32EF3333", _ring);             // does NOT cover Civil Lines
            var (u4, _) = AddDriver(SchoolA, "DPS001DR004", "Off Duty", "Bus 4", "UP32GH4444", _civil);               // covers it but never starts duty
            var (u5, _) = AddDriver(SchoolB, "XYZ002DR001", "Other School", "Bus B", "UP32ZZ9999");                    // another school, no area of this school
            foreach (var (u, school, lat) in new[] { (u1, SchoolA, 26.1), (u2, SchoolA, 26.2), (u3, SchoolA, 26.3), (u5, SchoolB, 26.5) })
                await Ping(await OnDuty(school, u), lat);

            var r = (await _svc.GetMyBusAsync(SchoolA, StudentUser, default)).Data!;
            Assert.Equal(new[] { "Ramesh Kumar", "Suresh Yadav" }, r.Buses.Select(b => b.DriverName));
            Assert.DoesNotContain(r.Buses, b => b.DriverName is "Anil Singh" or "Off Duty" or "Other School");
            Assert.All(r.Buses, b => Assert.Equal("LIVE", b.Status));
            Assert.Equal(("Bus 1", "UP32AB1234"), (r.Buses[0].VehicleLabel, r.Buses[0].VehiclePlate));
            Assert.Equal(26.1, r.Buses[0].Latitude);
        }

        [Fact]
        public async Task My_bus_status_thresholds_LIVE_STALE_and_OFFLINE_with_coordinates_only_while_not_offline()
        {
            await AssignStudentTo(_civil);
            var (u, _) = AddDriver(SchoolA, "DPS001DR001", "Ramesh Kumar", "Bus 1", "UP32AB1234", _civil);
            await Ping(await OnDuty(SchoolA, u), 26.85, 80.95);

            async Task<MyBusItemDto> Bus() => Assert.Single((await _svc.GetMyBusAsync(SchoolA, StudentUser, default)).Data!.Buses);

            var live = await Bus();
            Assert.Equal(("LIVE", 26.85, 80.95, 0), (live.Status, live.Latitude, live.Longitude, live.AgeSeconds));

            _clock.Advance(181);
            var stale = await Bus();
            Assert.Equal("STALE", stale.Status);
            Assert.Equal((26.85, 80.95, 181), (stale.Latitude, stale.Longitude, stale.AgeSeconds));         // the LAST position, with its age so the app can warn

            _clock.Advance(600 - 181 + 1);                                                                    // 601 s since the last fix
            var offline = await Bus();
            Assert.Equal("OFFLINE", offline.Status);
            Assert.Null(offline.Latitude);                                                                    // OFFLINE: state only
            Assert.Null(offline.Longitude);
            Assert.Null(offline.AgeSeconds);
        }

        [Fact]
        public async Task My_bus_a_session_with_no_fix_yet_is_OFFLINE_without_coordinates_and_hides_after_the_hide_time()
        {
            await AssignStudentTo(_civil);
            var (u, _) = AddDriver(SchoolA, "DPS001DR001", "Ramesh Kumar", "Bus 1", "UP32AB1234", _civil);
            await OnDuty(SchoolA, u);
            var bus = Assert.Single((await _svc.GetMyBusAsync(SchoolA, StudentUser, default)).Data!.Buses);
            Assert.Equal("OFFLINE", bus.Status);
            Assert.Null(bus.Latitude);
            _clock.Advance(60 * 60 + 2);
            Assert.Empty((await _svc.GetMyBusAsync(SchoolA, StudentUser, default)).Data!.Buses);          // the same hiding rule as the school's board
        }

        [Fact]
        public async Task My_bus_orders_LIVE_before_STALE_before_OFFLINE()
        {
            await AssignStudentTo(_civil);
            var (u1, _) = AddDriver(SchoolA, "DPS001DR001", "Aaa Offline", "Bus 1", "UP32AB1111", _civil);
            var (u2, _) = AddDriver(SchoolA, "DPS001DR002", "Bbb Stale", "Bus 2", "UP32AB2222", _civil);
            var (u3, _) = AddDriver(SchoolA, "DPS001DR003", "Ccc Live", "Bus 3", "UP32AB3333", _civil);
            await Ping(await OnDuty(SchoolA, u1), 26.1);
            _clock.Advance(700);
            await Ping(await OnDuty(SchoolA, u2), 26.2);
            _clock.Advance(200);
            await Ping(await OnDuty(SchoolA, u3), 26.3);
            Assert.Equal(new[] { "Ccc Live", "Bbb Stale", "Aaa Offline" }, (await _svc.GetMyBusAsync(SchoolA, StudentUser, default)).Data!.Buses.Select(b => b.DriverName));
        }

        [Fact]
        public async Task My_bus_an_inactive_area_or_a_deactivated_driver_shows_no_bus()
        {
            await AssignStudentTo(_civil);
            var (u, id) = AddDriver(SchoolA, "DPS001DR001", "Ramesh Kumar", "Bus 1", "UP32AB1234", _civil);
            await Ping(await OnDuty(SchoolA, u), 26.1);
            _duty.Store.Drivers.First(d => d.DriverId == id).StatusId = TransportStore.Inactive;
            Assert.Empty((await _svc.GetMyBusAsync(SchoolA, StudentUser, default)).Data!.Buses);
            _duty.Store.Drivers.First(d => d.DriverId == id).StatusId = TransportStore.Active;
            _duty.Store.Areas.First(a => a.AreaId == _civil).StatusId = TransportStore.Inactive;
            var r = (await _svc.GetMyBusAsync(SchoolA, StudentUser, default)).Data!;
            Assert.True(r.Assigned);
            Assert.Empty(r.Buses);
            Assert.Contains("not running", r.Message);
        }

        [Fact]
        public async Task My_bus_identity_comes_from_the_login_a_driver_or_another_schools_student_cannot_use_it()
        {
            await AssignStudentTo(_civil);
            Assert.Equal(404, (await _svc.GetMyBusAsync(SchoolA, DriverUser, default)).StatusCode);                  // not a Student login
            Assert.Equal(404, (await _svc.GetMyBusAsync(SchoolA, OtherSchoolStudentUser, default)).StatusCode);      // a student of ANOTHER school, with this school's token claim
            Assert.Equal(404, (await _svc.GetMyBusAsync(SchoolB, StudentUser, default)).StatusCode);                 // this student with another school's claim
            Assert.Equal(400, (await _svc.GetMyBusAsync(null, StudentUser, default)).StatusCode);
        }

        // ------------------------------------------------------------------------------------------------ Stage D: approximate times and scheduled[]
        [Fact]
        public async Task My_bus_gives_each_driver_their_OWN_times_for_the_students_area_only()
        {
            await AssignStudentTo(_civil);
            var (u1, _) = AddDriver(SchoolA, "DPS001DR001", "Ramesh Kumar", "Bus 1", "UP32AB1234", _civil, _ring);
            var (u2, _) = AddDriver(SchoolA, "DPS001DR002", "Suresh Yadav", "Van 2", "UP32CD2222", _civil);
            SetTimes("DPS001DR001", _civil, "07:30", "14:15"); SetTimes("DPS001DR001", _ring, "06:00", "13:00");   // Ring Road is NOT the student's area
            SetTimes("DPS001DR002", _civil, null, "15:00");                                                         // one driver may have only a drop time
            await Ping(await OnDuty(SchoolA, u1), 26.1); await Ping(await OnDuty(SchoolA, u2), 26.2);

            var r = (await _svc.GetMyBusAsync(SchoolA, StudentUser, default)).Data!;
            Assert.Equal(new[] { ("Ramesh Kumar", "07:30", "14:15"), ("Suresh Yadav", (string?)null, "15:00") }, r.Buses.Select(b => (b.DriverName, b.MorningPickupTime, b.AfternoonDropTime)));
            Assert.Empty(r.Scheduled);
            var json = JsonSerializer.Serialize(r);
            Assert.DoesNotContain("06:00", json); Assert.DoesNotContain("13:00", json);
        }

        [Fact]
        public async Task A_student_of_another_area_never_sees_these_times()
        {
            await AssignStudentTo(_ring);
            var (u1, _) = AddDriver(SchoolA, "DPS001DR001", "Ramesh Kumar", "Bus 1", "UP32AB1234", _civil);
            SetTimes("DPS001DR001", _civil, "07:30", "14:15");
            await Ping(await OnDuty(SchoolA, u1), 26.1);
            var r = (await _svc.GetMyBusAsync(SchoolA, StudentUser, default)).Data!;
            Assert.Empty(r.Buses); Assert.Empty(r.Scheduled);
            Assert.DoesNotContain("07:30", JsonSerializer.Serialize(r));
        }

        [Fact]
        public async Task Scheduled_lists_active_drivers_with_a_time_who_are_not_on_duty_and_nobody_else()
        {
            await AssignStudentTo(_civil);
            var (onDuty, _) = AddDriver(SchoolA, "DPS001DR001", "Ramesh Kumar", "Bus 1", "UP32AB1234", _civil);
            AddDriver(SchoolA, "DPS001DR002", "Suresh Yadav", "Van 2", "UP32CD2222", _civil);          // off duty, has times
            AddDriver(SchoolA, "DPS001DR003", "No Times", "Bus 3", "UP32EF3333", _civil);              // off duty, no times at all: not listed
            AddDriver(SchoolA, "DPS001DR004", "Wrong Area", "Bus 4", "UP32GH4444", _ring);             // has times, but on another area
            AddDriver(SchoolA, "DPS001DR005", "Hidden Driver", "Bus 5", "UP32IJ5555", _civil);         // a deactivated driver
            AddDriver(SchoolA, "DPS001DR006", "Closed Vehicle", "Bus 6", "UP32KL6666", _civil);        // an inactive vehicle
            AddDriver(SchoolB, "XYZ002DR001", "Other School", "Bus B", "UP32ZZ9999");                   // another school
            SetTimes("DPS001DR001", _civil, "07:30", "14:15");
            SetTimes("DPS001DR002", _civil, "07:45", null);
            SetTimes("DPS001DR004", _ring, "07:00", "14:00");
            SetTimes("DPS001DR005", _civil, "07:10", "14:10");
            SetTimes("DPS001DR006", _civil, "07:20", "14:20");
            _duty.Store.Drivers.First(d => d.DriverId == "DPS001DR005").StatusId = TransportStore.Inactive;
            _duty.Store.Vehicles.First(v => v.VehicleLabel == "Bus 6").StatusId = TransportStore.Inactive;
            await Ping(await OnDuty(SchoolA, onDuty), 26.1);

            var r = (await _svc.GetMyBusAsync(SchoolA, StudentUser, default)).Data!;
            Assert.Equal(new[] { "Ramesh Kumar" }, r.Buses.Select(b => b.DriverName));
            var only = Assert.Single(r.Scheduled);
            Assert.Equal(("Suresh Yadav", "Van 2", "UP32CD2222", "07:45", (string?)null), (only.DriverName, only.VehicleLabel, only.VehiclePlate, only.MorningPickupTime, only.AfternoonDropTime));
        }

        [Fact]
        public async Task A_driver_is_in_exactly_one_list_buses_while_on_duty_scheduled_after()
        {
            await AssignStudentTo(_civil);
            var (u, _) = AddDriver(SchoolA, "DPS001DR001", "Ramesh Kumar", "Bus 1", "UP32AB1234", _civil);
            SetTimes("DPS001DR001", _civil, "07:30", "14:15");

            var before = (await _svc.GetMyBusAsync(SchoolA, StudentUser, default)).Data!;
            Assert.Empty(before.Buses); Assert.Single(before.Scheduled);
            Assert.Contains("No bus is on duty", before.Message);                                  // the schedule is shown WITH the "nobody on duty" line

            var token = await OnDuty(SchoolA, u); await Ping(token, 26.1);
            var during = (await _svc.GetMyBusAsync(SchoolA, StudentUser, default)).Data!;
            Assert.Single(during.Buses); Assert.Empty(during.Scheduled);

            var dutySvc = new DriverDutyService(_duty, Options.Create(_o), _clock, NullLogger<DriverDutyService>.Instance);
            await dutySvc.EndByTokenAsync(token, default);
            var after = (await _svc.GetMyBusAsync(SchoolA, StudentUser, default)).Data!;
            Assert.Empty(after.Buses); Assert.Single(after.Scheduled);
        }

        [Fact]
        public async Task An_old_offline_session_that_is_hidden_from_buses_falls_back_to_scheduled()
        {
            await AssignStudentTo(_civil);
            var (u, _) = AddDriver(SchoolA, "DPS001DR001", "Ramesh Kumar", "Bus 1", "UP32AB1234", _civil);
            SetTimes("DPS001DR001", _civil, "07:30", "14:15");
            await OnDuty(SchoolA, u);                                                               // started, never sent a fix
            _clock.Advance(_o.HideOfflineAfterMinutes * 60 + 5);
            var r = (await _svc.GetMyBusAsync(SchoolA, StudentUser, default)).Data!;
            Assert.Empty(r.Buses);
            Assert.Equal("Ramesh Kumar", Assert.Single(r.Scheduled).DriverName);                    // still exactly one entry for the driver
        }

        [Fact]
        public async Task Scheduled_entries_carry_no_status_position_age_or_ids_and_not_assigned_has_none()
        {
            Assert.Empty((await _svc.GetMyBusAsync(SchoolA, StudentUser, default)).Data!.Scheduled);   // not assigned yet
            await AssignStudentTo(_civil);
            AddDriver(SchoolA, "DPS001DR001", "Ramesh Kumar", "Bus 1", "UP32AB1234", _civil);
            SetTimes("DPS001DR001", _civil, "07:30", null);
            var json = JsonSerializer.Serialize((await _svc.GetMyBusAsync(SchoolA, StudentUser, default)).Data!, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            using var doc = JsonDocument.Parse(json);
            var entry = doc.RootElement.GetProperty("scheduled")[0];
            Assert.Equal(new[] { "afternoonDropTime", "driverName", "morningPickupTime", "vehicleLabel", "vehiclePlate" }, entry.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
            foreach (var bad in new[] { "DPS001DR001", "status", "latitude", "ageSeconds", "driverId" }) Assert.DoesNotContain(bad, json, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task An_inactive_area_shows_no_schedule()
        {
            await AssignStudentTo(_civil);
            AddDriver(SchoolA, "DPS001DR001", "Ramesh Kumar", "Bus 1", "UP32AB1234", _civil);
            SetTimes("DPS001DR001", _civil, "07:30", "14:15");
            _duty.Store.Areas.First(a => a.AreaId == _civil).StatusId = TransportStore.Inactive;
            var r = (await _svc.GetMyBusAsync(SchoolA, StudentUser, default)).Data!;
            Assert.Empty(r.Buses); Assert.Empty(r.Scheduled);
        }

        // ------------------------------------------------------------------------------------------------ what a student may see
        [Fact]
        public async Task The_student_response_exposes_EXACTLY_these_fields_and_nothing_else()
        {
            // If you add a property to a student-facing DTO this test fails on purpose: a child's view must be extended by a reviewed decision, not by accident.
            Assert.Equal(new[] { "Area", "Assigned", "Buses", "Message", "Scheduled", "ServerTime" }, PropertyNames(typeof(MyBusDto)));
            Assert.Equal(new[] { "AreaName" }, PropertyNames(typeof(MyBusAreaDto)));
            Assert.Equal(new[] { "AfternoonDropTime", "AgeSeconds", "DriverName", "Latitude", "Longitude", "MorningPickupTime", "Status", "VehicleLabel", "VehiclePlate" }, PropertyNames(typeof(MyBusItemDto)));
            Assert.Equal(new[] { "AfternoonDropTime", "DriverName", "MorningPickupTime", "VehicleLabel", "VehiclePlate" }, PropertyNames(typeof(MyBusScheduledDto)));   // no status, no position, no ids

            // and the real serialised answer carries no extra key, however the data looks
            await AssignStudentTo(_civil);
            var (u, _) = AddDriver(SchoolA, "DPS001DR001", "Ramesh Kumar", "Bus 1", "UP32AB1234", _civil, _ring);
            SetTimes("DPS001DR001", _civil, "07:30", "14:15"); SetTimes("DPS001DR001", _ring, "06:05", "13:10");                 // the OTHER area's times must never appear
            AddDriver(SchoolA, "DPS001DR002", "Suresh Yadav", "Van 2", "UP32CD2222", _civil);                                    // off duty, with times: goes to scheduled[]
            SetTimes("DPS001DR002", _civil, "08:00", null);
            await Ping(await OnDuty(SchoolA, u), 26.85, 80.95);
            var json = JsonSerializer.Serialize((await _svc.GetMyBusAsync(SchoolA, StudentUser, default)).Data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(new[] { "area", "assigned", "buses", "message", "scheduled", "serverTime" }, doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
            var bus = doc.RootElement.GetProperty("buses")[0];
            Assert.Equal(new[] { "afternoonDropTime", "ageSeconds", "driverName", "latitude", "longitude", "morningPickupTime", "status", "vehicleLabel", "vehiclePlate" }, bus.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
            var sched = doc.RootElement.GetProperty("scheduled")[0];
            Assert.Equal(new[] { "afternoonDropTime", "driverName", "morningPickupTime", "vehicleLabel", "vehiclePlate" }, sched.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
            Assert.Equal("07:30", bus.GetProperty("morningPickupTime").GetString());
            foreach (var otherArea in new[] { "06:05", "13:10" }) Assert.DoesNotContain(otherArea, json);                     // another area's times of the same driver
            foreach (var secret in new[] { "9876543210", "DL12345", "addr", "photo", "DPS001DR001", "driverId", "mobile", "phone", "email" })
                Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase);
        }

        private static string[] PropertyNames(Type t) => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
    }

    // ======================================================================================================================================
    // Controllers: roles first.
    // ======================================================================================================================================
    public class StudentTransportAuthorizationTests
    {
        private const string School = "SCH1";
        private static readonly string[] Others = { "Teacher", "Accountant", "Admission Staff", "Student", "Guardian", "Super Admin", "Driver" };

        private static (T Controller, IStudentTransportService Service) Build<T>(Func<IStudentTransportService, T> make, params string[] roles) where T : ControllerBase
        {
            var svc = Fake.Of<IStudentTransportService>();
            return (Fake.WithUser(make(svc), Fake.User(School, 7, roles)), svc);
        }

        [Fact]
        public async Task Reading_an_assignment_is_School_Admin_and_Principal_only()
        {
            foreach (var role in Others)
            {
                var (c, svc) = Build(s => new StudentTransportController(s), role);
                Assert.Equal(403, Fake.Code(await c.Get("DPS001ST001", null, default)));
                Assert.Empty(Fake.ProxyOf(svc).Calls);
            }
            Assert.Equal(403, Fake.Code(await Build(s => new StudentTransportController(s)).Controller.Get("DPS001ST001", null, default)));      // no role at all
            foreach (var role in new[] { "School Admin", "Principal" })
                Assert.NotEqual(403, Fake.Code(await Build(s => new StudentTransportController(s), role).Controller.Get("DPS001ST001", null, default)));
        }

        [Fact]
        public async Task Assigning_and_removing_are_School_Admin_only_the_Principal_is_refused()
        {
            foreach (var role in new[] { "Principal" }.Concat(Others))
            {
                var (c, svc) = Build(s => new StudentTransportController(s), role);
                Assert.Equal(403, Fake.Code(await c.Assign("DPS001ST001", new StudentTransportRequest { AreaId = 1 }, default)));
                Assert.Equal(403, Fake.Code(await c.Remove("DPS001ST001", null, default)));
                Assert.Empty(Fake.ProxyOf(svc).Calls);
            }
            var admin = Build(s => new StudentTransportController(s), "School Admin").Controller;
            Assert.NotEqual(403, Fake.Code(await admin.Assign("DPS001ST001", new StudentTransportRequest { AreaId = 1 }, default)));
            Assert.NotEqual(403, Fake.Code(await admin.Remove("DPS001ST001", null, default)));
        }

        [Fact]
        public async Task My_bus_is_for_a_Student_only()
        {
            foreach (var role in new[] { "School Admin", "Principal", "Teacher", "Accountant", "Admission Staff", "Guardian", "Super Admin", "Driver" })
            {
                var (c, svc) = Build(s => new StudentBusController(s), role);
                Assert.Equal(403, Fake.Code(await c.MyBus(default)));
                Assert.Empty(Fake.ProxyOf(svc).Calls);
            }
            Assert.Equal(403, Fake.Code(await Build(s => new StudentBusController(s)).Controller.MyBus(default)));
            Assert.NotEqual(403, Fake.Code(await Build(s => new StudentBusController(s), "Student").Controller.MyBus(default)));
        }

        [Fact]
        public async Task My_bus_is_never_cached()
        {
            var svc = Fake.Of<IStudentTransportService>(new() { ["GetMyBusAsync"] = Fake.Ok(new MyBusDto { Assigned = false }) });
            var c = Fake.WithUser(new StudentBusController(svc), Fake.User(School, 7, "Student"));
            await c.MyBus(default);
            Assert.Equal("no-store", c.Response.Headers.CacheControl.ToString());
        }

        [Fact]
        public void Every_endpoint_needs_a_login_and_has_exactly_the_documented_route()
        {
            foreach (var t in new[] { typeof(StudentTransportController), typeof(StudentBusController) })
            {
                Assert.NotNull(t.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.GetCustomAttributes().Any(a => a is HttpMethodAttribute)))
                    Assert.Null(m.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>());
            }
        }
    }
}
