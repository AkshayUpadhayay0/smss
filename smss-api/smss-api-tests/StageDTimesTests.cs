using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
    // Stage D: approximate pickup / drop times on a driver-and-area link.
    // The fakes cannot prove the database (the time columns and the CHECK): the scripted live check against smss-db-dev covers those.
    // ======================================================================================================================================
    public class TransportTimeRulesTests
    {
        [Theory]
        [InlineData("07:30", 7, 30)]
        [InlineData("00:00", 0, 0)]
        [InlineData("23:59", 23, 59)]
        [InlineData(" 14:05 ", 14, 5)]
        public void Valid_times_parse(string text, int h, int m)
        {
            Assert.True(TransportRules.TryParseTime(text, out var t));
            Assert.Equal(new TimeOnly(h, m), t);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Blank_means_not_set(string? text)
        {
            Assert.True(TransportRules.TryParseTime(text, out var t));
            Assert.Null(t);
        }

        [Theory]
        [InlineData("24:00")] [InlineData("25:00")] [InlineData("7:30")] [InlineData("07:3")] [InlineData("07:60")] [InlineData("07:30:00")]
        [InlineData("0730")] [InlineData("07.30")] [InlineData("7am")] [InlineData("abc")] [InlineData("-1:00")] [InlineData("07:30 PM")]
        public void Anything_but_two_digit_HHmm_is_refused(string text) => Assert.False(TransportRules.TryParseTime(text, out _));

        [Fact]
        public void Format_is_HHmm_or_null()
        {
            Assert.Equal("07:05", TransportRules.FormatTime(new TimeOnly(7, 5)));
            Assert.Equal("14:30", TransportRules.FormatTime(new TimeOnly(14, 30, 59)));   // seconds are never shown
            Assert.Null(TransportRules.FormatTime(null));
        }
    }

    public class DriverAreaTimesServiceTests
    {
        private const string A = TransportStore.SchoolA, B = TransportStore.SchoolB;

        private static (DriverService Svc, TransportStore Store) Make()
        {
            var s = new TransportStore();
            return (new DriverService(new FakeDriverRepo(s), Fake.Of<IFileStorageService>(), new FakeNotifier(), NullLogger<DriverService>.Instance), s);
        }

        private static DriverRequest Req(string name = "Ramesh Kumar", string dl = "DL-0420110149646", params DriverAreaRequest[] areas) => new()
        {
            FullName = name, MobileNumber = "9876543210", Address = "12 Main Road", DlNumber = dl, DlValidUpto = new DateOnly(2035, 5, 1), Areas = areas.ToList()
        };
        private static DriverRequest ReqAreas(params DriverAreaRequest[] areas) => Req(areas: areas);
        private static DriverAreaRequest Area(long id, string? pickup = null, string? drop = null) => new() { AreaId = id, MorningPickupTime = pickup, AfternoonDropTime = drop };

        [Fact]
        public async Task Create_stores_the_times_and_the_driver_answers_with_HHmm()
        {
            var (svc, s) = Make();
            var north = s.AddArea(A, "North"); var south = s.AddArea(A, "South");
            var r = await svc.CreateAsync(A, ReqAreas(Area(north.AreaId, "07:30", "14:15"), Area(south.AreaId, "08:00", null)), default);
            Assert.Equal(201, r.StatusCode);
            Assert.Equal(new[] { ("North", "07:30", "14:15"), ("South", "08:00", (string?)null) }, r.Data!.Areas.Select(a => (a.AreaName, a.MorningPickupTime, a.AfternoonDropTime)));
        }

        [Fact]
        public async Task Both_times_are_optional_and_an_area_without_times_keeps_NULLs()
        {
            var (svc, s) = Make();
            var north = s.AddArea(A, "North");
            var r = await svc.CreateAsync(A, ReqAreas(Area(north.AreaId)), default);
            Assert.Equal(201, r.StatusCode);
            Assert.Null(r.Data!.Areas[0].MorningPickupTime); Assert.Null(r.Data.Areas[0].AfternoonDropTime);
            Assert.Equal(200, (await svc.UpdateAsync(A, r.Data.DriverId, ReqAreas(Area(north.AreaId, null, "15:00")), default)).StatusCode);   // only a drop time
            Assert.Equal(200, (await svc.UpdateAsync(A, r.Data.DriverId, ReqAreas(Area(north.AreaId, "07:00", null)), default)).StatusCode);   // only a pickup time
        }

        [Fact]
        public async Task Update_changes_the_times_of_a_kept_row_and_clears_one_when_null_is_sent()
        {
            var (svc, s) = Make();
            var north = s.AddArea(A, "North"); var south = s.AddArea(A, "South");
            var d = (await svc.CreateAsync(A, ReqAreas(Area(north.AreaId, "07:30", "14:15"), Area(south.AreaId, "08:00", "14:45")), default)).Data!;
            var rowIds = s.Links.Select(l => l.DriverAreaId).OrderBy(x => x).ToList();

            var u = await svc.UpdateAsync(A, d.DriverId, ReqAreas(Area(north.AreaId, "07:45", null), Area(south.AreaId, "08:00", "14:45")), default);
            Assert.Equal(200, u.StatusCode);
            Assert.Equal(new[] { ("North", "07:45", (string?)null), ("South", "08:00", "14:45") }, u.Data!.Areas.Select(a => (a.AreaName, a.MorningPickupTime, a.AfternoonDropTime)));
            Assert.Equal(rowIds, s.Links.Select(l => l.DriverAreaId).OrderBy(x => x).ToList());   // the same link rows: nothing was deleted and re-created

            var cleared = await svc.UpdateAsync(A, d.DriverId, ReqAreas(Area(north.AreaId), Area(south.AreaId)), default);
            Assert.All(cleared.Data!.Areas, a => { Assert.Null(a.MorningPickupTime); Assert.Null(a.AfternoonDropTime); });
        }

        [Fact]
        public async Task An_area_not_listed_is_removed_with_its_times_and_a_new_one_starts_with_what_was_sent()
        {
            var (svc, s) = Make();
            var north = s.AddArea(A, "North"); var south = s.AddArea(A, "South");
            var d = (await svc.CreateAsync(A, ReqAreas(Area(north.AreaId, "07:30", "14:15")), default)).Data!;
            var u = await svc.UpdateAsync(A, d.DriverId, ReqAreas(Area(south.AreaId, "09:00", "16:00")), default);
            Assert.Equal(new[] { ("South", "09:00", "16:00") }, u.Data!.Areas.Select(a => (a.AreaName, a.MorningPickupTime!, a.AfternoonDropTime!)));
            Assert.Single(s.Links);
        }

        [Theory]
        [InlineData("25:00", null, "pickup")]
        [InlineData("7:30", null, "pickup")]
        [InlineData("abc", null, "pickup")]
        [InlineData(null, "24:00", "drop")]
        [InlineData(null, "14:3", "drop")]
        [InlineData("07:30:00", null, "pickup")]
        public async Task A_malformed_time_is_400_naming_which_one_and_writes_nothing(string? pickup, string? drop, string which)
        {
            var (svc, s) = Make();
            var north = s.AddArea(A, "North");
            var r = await svc.CreateAsync(A, ReqAreas(Area(north.AreaId, pickup, drop)), default);
            Assert.Equal(400, r.StatusCode);
            Assert.Contains($"approximate {which} time as HH:mm", r.Message);
            Assert.Empty(s.Drivers); Assert.Empty(s.Links);
        }

        [Theory]
        [InlineData("14:15", "07:30")]
        [InlineData("07:30", "07:30")]
        public async Task The_pickup_must_be_earlier_than_the_drop(string pickup, string drop)
        {
            var (svc, s) = Make();
            var north = s.AddArea(A, "North");
            var r = await svc.CreateAsync(A, ReqAreas(Area(north.AreaId, pickup, drop)), default);
            Assert.Equal(400, r.StatusCode);
            Assert.Equal("The approximate pickup time must be earlier than the approximate drop time.", r.Message);
            Assert.Empty(s.Drivers);
        }

        [Fact]
        public async Task A_refused_update_changes_nothing()
        {
            var (svc, s) = Make();
            var north = s.AddArea(A, "North");
            var d = (await svc.CreateAsync(A, ReqAreas(Area(north.AreaId, "07:30", "14:15")), default)).Data!;
            Assert.Equal(400, (await svc.UpdateAsync(A, d.DriverId, ReqAreas(Area(north.AreaId, "16:00", "09:00")), default)).StatusCode);
            var link = Assert.Single(s.Links);
            Assert.Equal((new TimeOnly(7, 30), new TimeOnly(14, 15)), (link.MorningPickupTime!.Value, link.AfternoonDropTime!.Value));
        }

        [Fact]
        public async Task The_old_areaIds_shape_is_refused_alone_or_together_with_areas_and_never_clears_the_areas()
        {
            var (svc, s) = Make();
            var north = s.AddArea(A, "North");
            var d = (await svc.CreateAsync(A, ReqAreas(Area(north.AreaId, "07:30", "14:15")), default)).Data!;

            var onlyOld = Req(); onlyOld.Areas = null; onlyOld.AreaIds = new List<long> { north.AreaId };
            var a = await svc.UpdateAsync(A, d.DriverId, onlyOld, default);
            Assert.Equal(400, a.StatusCode); Assert.Contains("areaIds", a.Message);

            var both = ReqAreas(Area(north.AreaId, "08:00", "15:00")); both.AreaIds = new List<long> { north.AreaId };
            Assert.Equal(400, (await svc.UpdateAsync(A, d.DriverId, both, default)).StatusCode);
            Assert.Equal(400, (await svc.CreateAsync(A, both, default)).StatusCode);

            var link = Assert.Single(s.Links);                                  // untouched by the refused calls
            Assert.Equal(new TimeOnly(7, 30), link.MorningPickupTime);
            Assert.Single(s.Drivers);
        }

        [Fact]
        public async Task A_duplicate_missing_or_non_positive_area_id_is_400()
        {
            var (svc, s) = Make();
            var north = s.AddArea(A, "North");
            var dup = await svc.CreateAsync(A, ReqAreas(Area(north.AreaId), Area(north.AreaId, "07:00", null)), default);
            Assert.Equal(400, dup.StatusCode); Assert.Equal("An area is listed more than once.", dup.Message);
            Assert.Equal(400, (await svc.CreateAsync(A, ReqAreas(new DriverAreaRequest { AreaId = null }), default)).StatusCode);
            Assert.Equal(400, (await svc.CreateAsync(A, ReqAreas(Area(0)), default)).StatusCode);
            Assert.Equal(400, (await svc.CreateAsync(A, ReqAreas(Area(-3)), default)).StatusCode);
            Assert.Empty(s.Drivers);
        }

        [Fact]
        public async Task Another_schools_area_is_404_and_an_inactive_one_is_400_even_with_times_present()
        {
            var (svc, s) = Make();
            var theirs = s.AddArea(B, "Far"); var closed = s.AddArea(A, "Closed", TransportStore.Inactive);
            Assert.Equal(404, (await svc.CreateAsync(A, ReqAreas(Area(theirs.AreaId, "07:00", "14:00")), default)).StatusCode);
            Assert.Equal(400, (await svc.CreateAsync(A, ReqAreas(Area(closed.AreaId, "07:00", "14:00")), default)).StatusCode);
            Assert.Empty(s.Links);
        }

        [Fact]
        public async Task No_areas_at_all_clears_the_set_and_a_null_list_is_the_same()
        {
            var (svc, s) = Make();
            var north = s.AddArea(A, "North");
            var d = (await svc.CreateAsync(A, ReqAreas(Area(north.AreaId, "07:30", null)), default)).Data!;
            var none = Req(); none.Areas = null;
            Assert.Equal(200, (await svc.UpdateAsync(A, d.DriverId, none, default)).StatusCode);
            Assert.Empty(s.Links);
        }

        [Fact]
        public async Task Another_schools_driver_cannot_be_updated_so_its_times_cannot_be_changed()
        {
            var (svc, s) = Make();
            var theirArea = s.AddArea(B, "Far");
            var theirs = (await svc.CreateAsync(B, Req("Other School Driver", "DL-0003", Area(theirArea.AreaId, "07:00", "14:00")), default)).Data!;
            Assert.Equal(404, (await svc.UpdateAsync(A, theirs.DriverId, ReqAreas(Area(theirArea.AreaId, "09:00", "10:00")), default)).StatusCode);
            Assert.Equal(new TimeOnly(7, 0), Assert.Single(s.Links).MorningPickupTime);
        }
    }

    public class DriverMeTimesTests
    {
        [Fact]
        public async Task The_driver_sees_only_their_own_times_under_their_own_areas()
        {
            var repo = new FakeDutyRepo();
            var s = repo.Store;
            var a1 = s.AddArea(TransportStore.SchoolA, "Civil Lines"); var a2 = s.AddArea(TransportStore.SchoolA, "Ring Road");
            var bus = s.AddVehicle(TransportStore.SchoolA, "Bus 1", "UP32AB1234");
            s.AddDriver(TransportStore.SchoolA, "DPS001DR001", "Ramesh Kumar", vehicleId: bus.VehicleId, dl: "DL00001");
            s.AddDriver(TransportStore.SchoolA, "DPS001DR002", "Suresh Yadav", vehicleId: bus.VehicleId, dl: "DL00002");
            s.Link(TransportStore.SchoolA, "DPS001DR001", a1.AreaId, "07:30", "14:15"); s.Link(TransportStore.SchoolA, "DPS001DR001", a2.AreaId);
            s.Link(TransportStore.SchoolA, "DPS001DR002", a1.AreaId, "06:00", "13:00");                                   // another driver's times on the same area
            s.Users.Add(new TbUsers { UserId = 501, SchoolId = TransportStore.SchoolA, UserType = "Driver", OrgUserId = "DPS001DR001", DisplayName = "Ramesh Kumar" });

            var svc = new DriverDutyService(repo, Options.Create(new TransportLiveOptions()), new ManualClock(), NullLogger<DriverDutyService>.Instance);
            var me = (await svc.GetMeAsync(TransportStore.SchoolA, 501, default)).Data!;
            Assert.Equal(new[] { ("Civil Lines", "07:30", "14:15"), ("Ring Road", (string?)null, (string?)null) }, me.Areas.Select(a => (a.AreaName, a.MorningPickupTime, a.AfternoonDropTime)));
            var json = JsonSerializer.Serialize(me);
            Assert.DoesNotContain("06:00", json); Assert.DoesNotContain("13:00", json);
        }
    }
}
