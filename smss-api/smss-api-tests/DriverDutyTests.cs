using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging;
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
    // Stage 2: driver duty and live location. The in-memory repository below behaves like the real one (school-scoped, one open session per driver,
    // the upsert guard). What it cannot prove is the database itself (the partial unique index, CHECK constraints, the real ON CONFLICT): the scripted
    // live check against smss-db-dev covers those.
    // ======================================================================================================================================
    internal sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(double seconds) => Now = Now.AddSeconds(seconds);
    }

    internal sealed class FakeDutyRepo : IDriverDutyRepository
    {
        public const int Active = 1, Inactive = 2;
        public readonly TransportStore Store = new();
        public readonly List<TbDriverDutySession> Sessions = new();
        public readonly List<TbDriverLiveLocation> Live = new();
        public bool ThrowOnUpsert, RefuseStart;
        private long _session = 1;

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) => Task.FromResult<int?>(statusName == "Active" ? Active : Inactive);

        public Task<DutyDriver?> GetDutyDriverAsync(string schoolId, long userId, CancellationToken ct)
        {
            var u = Store.Users.FirstOrDefault(x => x.UserId == userId && x.UserType == "Driver" && x.SchoolId == schoolId);
            var d = u == null ? null : Store.Drivers.FirstOrDefault(x => x.SchoolId == schoolId && x.DriverId == u.OrgUserId);
            if (d == null) return Task.FromResult<DutyDriver?>(null);
            var v = d.VehicleId == null ? null : Store.Vehicles.FirstOrDefault(x => x.SchoolId == schoolId && x.VehicleId == d.VehicleId);
            return Task.FromResult<DutyDriver?>(new DutyDriver(u!.UserId, d.SchoolId, d.DriverId, d.FullName, d.StatusId, d.VehicleId, v?.VehicleLabel, v?.RegistrationNumber, v?.StatusId));
        }

        public Task<List<AreaRef>> GetAssignedAreasAsync(string schoolId, string driverId, CancellationToken ct) =>
            Task.FromResult(Store.Links.Where(l => l.SchoolId == schoolId && l.DriverId == driverId)
                .Select(l => Store.Areas.First(a => a.SchoolId == schoolId && a.AreaId == l.AreaId)).OrderBy(a => a.AreaName).Select(a => new AreaRef(a.AreaId, a.AreaName, a.StatusId)).ToList());

        public Task<TbDriverDutySession?> GetOpenSessionAsync(string schoolId, string driverId, CancellationToken ct) =>
            Task.FromResult(Sessions.FirstOrDefault(s => s.SchoolId == schoolId && s.DriverId == driverId && s.EndedAt == null));

        public Task<TbDriverLiveLocation?> GetLiveAsync(string driverId, CancellationToken ct) => Task.FromResult(Live.FirstOrDefault(l => l.DriverId == driverId));

        public Task<DutySessionContext?> GetSessionByTokenHashAsync(string tokenHash, CancellationToken ct)
        {
            var s = Sessions.FirstOrDefault(x => x.TrackingTokenHash == tokenHash);
            var d = s == null ? null : Store.Drivers.First(x => x.SchoolId == s.SchoolId && x.DriverId == s.DriverId);
            return Task.FromResult(s == null ? null : new DutySessionContext(s, d!.FullName, d.StatusId));
        }

        public Task<TbDriverDutySession?> StartSessionAsync(string schoolId, string driverId, long vehicleId, string tokenHash, long? staleSessionId, DateTime now, CancellationToken ct)
        {
            if (staleSessionId != null) CloseSession(staleSessionId.Value, "AUTO_TIMEOUT", now, deleteLive: false);
            Live.RemoveAll(l => l.DriverId == driverId);
            if (RefuseStart || Sessions.Any(s => s.DriverId == driverId && s.EndedAt == null)) return Task.FromResult<TbDriverDutySession?>(null);   // the partial unique index
            var session = new TbDriverDutySession { SessionId = _session++, SchoolId = schoolId, DriverId = driverId, VehicleId = vehicleId, StartedAt = now, TrackingTokenHash = tokenHash, CreatedAt = now };
            Sessions.Add(session);
            return Task.FromResult<TbDriverDutySession?>(session);
        }

        public Task<bool> ResumeSessionAsync(long sessionId, string newTokenHash, CancellationToken ct)
        {
            var s = Sessions.FirstOrDefault(x => x.SessionId == sessionId && x.EndedAt == null);
            if (s == null) return Task.FromResult(false);
            s.TrackingTokenHash = newTokenHash;
            Live.RemoveAll(l => l.SessionId == sessionId);
            return Task.FromResult(true);
        }

        public Task<bool> EndSessionAsync(long sessionId, string reason, DateTime now, CancellationToken ct) => Task.FromResult(CloseSession(sessionId, reason, now, deleteLive: true));

        private bool CloseSession(long sessionId, string reason, DateTime now, bool deleteLive)
        {
            var s = Sessions.FirstOrDefault(x => x.SessionId == sessionId && x.EndedAt == null);
            if (deleteLive) Live.RemoveAll(l => l.SessionId == sessionId);
            if (s == null) return false;
            s.EndedAt = now; s.EndReason = reason;
            return true;
        }

        public Task<int> UpsertLocationAsync(string schoolId, string driverId, long sessionId, long vehicleId, LocationFix f, CancellationToken ct)
        {
            if (ThrowOnUpsert) throw new InvalidOperationException("simulated database failure");
            var row = Live.FirstOrDefault(l => l.DriverId == driverId);
            if (row == null)
            {
                Live.Add(new TbDriverLiveLocation { DriverId = driverId, SchoolId = schoolId, SessionId = sessionId, VehicleId = vehicleId, Latitude = f.Latitude, Longitude = f.Longitude, AccuracyM = f.AccuracyM, SpeedMps = f.SpeedMps, HeadingDeg = f.HeadingDeg, RecordedAt = f.RecordedAt, ReceivedAt = f.ReceivedAt });
                return Task.FromResult(1);
            }
            if (row.SessionId != sessionId || row.RecordedAt >= f.RecordedAt) return Task.FromResult(0);   // WHERE live.session_id = EXCLUDED.session_id AND live.recorded_at < EXCLUDED.recorded_at
            row.VehicleId = vehicleId; row.Latitude = f.Latitude; row.Longitude = f.Longitude; row.AccuracyM = f.AccuracyM; row.SpeedMps = f.SpeedMps; row.HeadingDeg = f.HeadingDeg;
            row.RecordedAt = f.RecordedAt; row.ReceivedAt = f.ReceivedAt;
            return Task.FromResult(1);
        }

        public Task<List<LiveRow>> ListOpenAsync(string schoolId, long? areaId, long? vehicleId, int activeStatusId, CancellationToken ct)
        {
            var rows = new List<LiveRow>();
            foreach (var s in Sessions.Where(x => x.SchoolId == schoolId && x.EndedAt == null))
            {
                var d = Store.Drivers.First(x => x.SchoolId == schoolId && x.DriverId == s.DriverId);
                if (d.StatusId != activeStatusId) continue;
                if (vehicleId != null && s.VehicleId != vehicleId) continue;
                if (areaId != null && !Store.Links.Any(l => l.SchoolId == schoolId && l.DriverId == d.DriverId && l.AreaId == areaId)) continue;
                var v = Store.Vehicles.First(x => x.SchoolId == schoolId && x.VehicleId == s.VehicleId);
                var l2 = Live.FirstOrDefault(x => x.SessionId == s.SessionId);
                rows.Add(new LiveRow(d.DriverId, d.FullName, v.VehicleId, v.VehicleLabel, v.RegistrationNumber, s.SessionId, s.StartedAt, l2?.Latitude, l2?.Longitude, l2?.AccuracyM, l2?.SpeedMps, l2?.HeadingDeg, l2?.ReceivedAt));
            }
            return Task.FromResult(rows.OrderBy(r => r.DriverName).ThenBy(r => r.DriverId).ToList());
        }

        public Task<Dictionary<string, List<string>>> GetAreaNamesAsync(string schoolId, IReadOnlyCollection<string> driverIds, CancellationToken ct) =>
            Task.FromResult(Store.Links.Where(l => l.SchoolId == schoolId && driverIds.Contains(l.DriverId)).GroupBy(l => l.DriverId)
                .ToDictionary(g => g.Key, g => g.Select(l => Store.Areas.First(a => a.AreaId == l.AreaId).AreaName).OrderBy(n => n).ToList()));
    }

    /// <summary>Collects every log line (message and exception text) so a test can prove a secret never reaches the log.</summary>
    internal sealed class RecordingLogger<T> : ILogger<T>
    {
        public readonly List<string> Lines = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception) + " | " + exception?.ToString() + " | " + string.Join(";", (state as IEnumerable<KeyValuePair<string, object?>>)?.Select(kv => $"{kv.Key}={kv.Value}") ?? Array.Empty<string>()));
    }

    public class DriverDutyServiceTests
    {
        private const string SchoolA = TransportStore.SchoolA, SchoolB = TransportStore.SchoolB;
        private const long DriverUser = 501, OtherSchoolDriverUser = 601;

        private readonly FakeDutyRepo _repo = new();
        private readonly ManualClock _clock = new();
        private readonly RecordingLogger<DriverDutyService> _log = new();
        private readonly TransportLiveOptions _o = new();
        private readonly DriverDutyService _svc;
        private readonly long _vehicle;

        public DriverDutyServiceTests()
        {
            _svc = new DriverDutyService(_repo, Options.Create(_o), _clock, _log);
            var s = _repo.Store;
            var area1 = s.AddArea(SchoolA, "Civil Lines"); var area2 = s.AddArea(SchoolA, "Ring Road");
            var bus = s.AddVehicle(SchoolA, "Bus 1", "UP32AB1234"); _vehicle = bus.VehicleId;
            s.AddDriver(SchoolA, "DPS001DR001", "Ramesh Kumar", vehicleId: bus.VehicleId, dl: "DL00001");
            s.Link(SchoolA, "DPS001DR001", area1.AreaId); s.Link(SchoolA, "DPS001DR001", area2.AreaId);
            s.Users.Add(new TbUsers { UserId = DriverUser, SchoolId = SchoolA, UserType = "Driver", OrgUserId = "DPS001DR001", DisplayName = "Ramesh Kumar" });
            // a driver of ANOTHER school with a vehicle and an open duty
            var busB = s.AddVehicle(SchoolB, "Bus B", "UP32ZZ9999");
            s.AddDriver(SchoolB, "XYZ002DR001", "Other School Driver", vehicleId: busB.VehicleId, dl: "DL00002");
            s.Users.Add(new TbUsers { UserId = OtherSchoolDriverUser, SchoolId = SchoolB, UserType = "Driver", OrgUserId = "XYZ002DR001", DisplayName = "Other School Driver" });
        }

        private DateTime Utc => _clock.Now.UtcDateTime;
        private async Task<string> StartAsync(long user = DriverUser, string school = SchoolA)
        {
            var r = await _svc.StartAsync(school, user, default);
            Assert.True(r.Status, r.Message);
            return r.Data!.TrackingToken;
        }
        private static DutyLocationRequest Fix(DateTimeOffset at, double lat = 26.85, double lng = 80.95, Action<DutyLocationRequest>? tweak = null)
        {
            var f = new DutyLocationRequest { Latitude = lat, Longitude = lng, RecordedAt = at, AccuracyMeters = 12, SpeedMps = 5, HeadingDegrees = 90 };
            tweak?.Invoke(f);
            return f;
        }
        private Task<ApiResponse<DutyLocationResultDto>> Ping(string? token, DutyLocationRequest? f = null) => _svc.RecordLocationAsync(token, f ?? Fix(_clock.Now), default);

        // ------------------------------------------------------------------------------------------------ GET me
        [Fact]
        public async Task Me_shows_the_vehicle_the_areas_and_off_duty()
        {
            var r = await _svc.GetMeAsync(SchoolA, DriverUser, default);
            Assert.Equal(200, r.StatusCode);
            Assert.Equal("DPS001DR001", r.Data!.DriverId);
            Assert.Equal("Bus 1", r.Data.Vehicle!.VehicleLabel);
            Assert.Equal(new[] { "Civil Lines", "Ring Road" }, r.Data.Areas.Select(a => a.AreaName));
            Assert.False(r.Data.Duty.OnDuty);
            Assert.True(r.Data.CanStartDuty);
            Assert.Equal(60, r.Data.IntervalSeconds);
        }

        [Fact]
        public async Task Me_reports_an_open_duty_and_when_the_last_fix_was_sent()
        {
            var token = await StartAsync();
            Assert.Equal(DutyStatuses.Offline, (await _svc.GetMeAsync(SchoolA, DriverUser, default)).Data!.Duty.Status);   // started, nothing sent yet
            await Ping(token);
            _clock.Advance(30);
            var duty = (await _svc.GetMeAsync(SchoolA, DriverUser, default)).Data!.Duty;
            Assert.True(duty.OnDuty);
            Assert.Equal(DutyStatuses.Live, duty.Status);
            Assert.Equal(30, duty.LastSentAgeSeconds);
        }

        [Fact]
        public async Task Me_refuses_a_login_that_is_not_a_driver_of_the_callers_school_and_a_missing_school()
        {
            Assert.Equal(403, (await _svc.GetMeAsync(SchoolA, 999, default)).StatusCode);                          // no such driver login
            Assert.Equal(403, (await _svc.GetMeAsync(SchoolA, OtherSchoolDriverUser, default)).StatusCode);        // a driver of another school
            Assert.Equal(400, (await _svc.GetMeAsync(null, DriverUser, default)).StatusCode);
        }

        [Fact]
        public async Task Me_says_why_duty_cannot_start_when_there_is_no_vehicle_or_it_is_inactive()
        {
            _repo.Store.Drivers.First(d => d.DriverId == "DPS001DR001").VehicleId = null;
            var r = (await _svc.GetMeAsync(SchoolA, DriverUser, default)).Data!;
            Assert.False(r.CanStartDuty);
            Assert.Contains("No vehicle is assigned", r.StartBlockedReason);

            _repo.Store.Drivers.First(d => d.DriverId == "DPS001DR001").VehicleId = _vehicle;
            _repo.Store.Vehicles.First(v => v.VehicleId == _vehicle).StatusId = TransportStore.Inactive;
            r = (await _svc.GetMeAsync(SchoolA, DriverUser, default)).Data!;
            Assert.False(r.CanStartDuty);
            Assert.Contains("inactive", r.StartBlockedReason);
        }

        // ------------------------------------------------------------------------------------------------ POST start
        [Fact]
        public async Task Start_creates_a_session_with_a_token_that_is_stored_only_as_a_hash()
        {
            var r = await _svc.StartAsync(SchoolA, DriverUser, default);
            Assert.Equal(201, r.StatusCode);
            Assert.False(r.Data!.Resumed);
            Assert.Equal(43, r.Data.TrackingToken.Length);
            var s = Assert.Single(_repo.Sessions);
            Assert.Equal(DutyRules.Hash(r.Data.TrackingToken), s.TrackingTokenHash);
            Assert.DoesNotContain(r.Data.TrackingToken, s.TrackingTokenHash);
            Assert.Equal(_vehicle, s.VehicleId);                                           // the vehicle snapshot
            Assert.Equal(SchoolA, s.SchoolId);
            Assert.Equal(Utc.AddHours(16), r.Data.ExpiresAt);
        }

        [Fact]
        public async Task Start_is_idempotent_a_fresh_open_session_is_resumed_with_a_NEW_token_and_the_old_one_stops_working()
        {
            var first = await StartAsync();
            await Ping(first);
            _clock.Advance(60);
            var r = await _svc.StartAsync(SchoolA, DriverUser, default);
            Assert.Equal(200, r.StatusCode);
            Assert.True(r.Data!.Resumed);
            Assert.Single(_repo.Sessions);
            Assert.NotEqual(first, r.Data.TrackingToken);
            Assert.Equal(401, (await Ping(first)).StatusCode);                              // the old token is dead
            Assert.Equal(200, (await Ping(r.Data.TrackingToken)).StatusCode);              // the new one works
        }

        [Fact]
        public async Task Start_deletes_the_live_row_so_yesterdays_position_never_shows()
        {
            var token = await StartAsync();
            await Ping(token);
            Assert.Single(_repo.Live);
            _clock.Advance(60);
            await _svc.StartAsync(SchoolA, DriverUser, default);                            // resume
            Assert.Empty(_repo.Live);
        }

        [Fact]
        public async Task Start_closes_a_stale_open_session_as_AUTO_TIMEOUT_and_opens_a_new_one()
        {
            var first = await StartAsync();
            await Ping(first);
            _clock.Advance(_o.ResumeWithinSeconds + 1);                                      // nothing heard for more than 10 minutes
            var r = await _svc.StartAsync(SchoolA, DriverUser, default);
            Assert.Equal(201, r.StatusCode);
            Assert.Equal(2, _repo.Sessions.Count);
            Assert.Equal("AUTO_TIMEOUT", _repo.Sessions[0].EndReason);
            Assert.NotNull(_repo.Sessions[0].EndedAt);
            Assert.Null(_repo.Sessions[1].EndedAt);
            Assert.Equal(409, (await Ping(first)).StatusCode);                              // the old token now says SESSION_ENDED
        }

        [Fact]
        public async Task Start_after_a_vehicle_change_opens_a_new_session()
        {
            await StartAsync();
            var van = _repo.Store.AddVehicle(SchoolA, "Van 2", "UP32CD2222");
            _repo.Store.Drivers.First(d => d.DriverId == "DPS001DR001").VehicleId = van.VehicleId;
            _clock.Advance(30);
            var r = await _svc.StartAsync(SchoolA, DriverUser, default);
            Assert.Equal(201, r.StatusCode);
            Assert.Equal(van.VehicleId, _repo.Sessions[1].VehicleId);
        }

        [Fact]
        public async Task Start_after_the_session_cap_opens_a_new_one()
        {
            var t = await StartAsync();
            await Ping(t);
            _clock.Advance(16 * 3600 - 10);
            await Ping(t);                                                                  // still inside the cap, so the session is fresh enough to resume ...
            _clock.Advance(20);                                                             // ... until the cap passes
            Assert.Equal(201, (await _svc.StartAsync(SchoolA, DriverUser, default)).StatusCode);
            Assert.Equal(2, _repo.Sessions.Count);
        }

        [Fact]
        public async Task Start_is_refused_without_a_vehicle_with_an_inactive_vehicle_and_for_an_inactive_driver()
        {
            _repo.Store.Vehicles.First(v => v.VehicleId == _vehicle).StatusId = TransportStore.Inactive;
            Assert.Equal(409, (await _svc.StartAsync(SchoolA, DriverUser, default)).StatusCode);
            _repo.Store.Vehicles.First(v => v.VehicleId == _vehicle).StatusId = TransportStore.Active;
            _repo.Store.Drivers.First(d => d.DriverId == "DPS001DR001").VehicleId = null;
            Assert.Equal(409, (await _svc.StartAsync(SchoolA, DriverUser, default)).StatusCode);
            _repo.Store.Drivers.First(d => d.DriverId == "DPS001DR001").VehicleId = _vehicle;
            _repo.Store.Drivers.First(d => d.DriverId == "DPS001DR001").StatusId = TransportStore.Inactive;
            Assert.Equal(403, (await _svc.StartAsync(SchoolA, DriverUser, default)).StatusCode);
            Assert.Empty(_repo.Sessions);
        }

        [Fact]
        public async Task Start_cannot_be_used_across_schools_or_by_a_non_driver()
        {
            Assert.Equal(403, (await _svc.StartAsync(SchoolA, OtherSchoolDriverUser, default)).StatusCode);   // the token says school A, the login belongs to B
            Assert.Equal(403, (await _svc.StartAsync(SchoolA, 12345, default)).StatusCode);
            Assert.Equal(400, (await _svc.StartAsync(null, DriverUser, default)).StatusCode);
            Assert.Empty(_repo.Sessions);
        }

        [Fact]
        public async Task Start_when_the_database_refuses_a_second_open_session_is_409_not_500()
        {
            _repo.RefuseStart = true;                                                       // what the partial unique index does to the loser of two starts at once
            var r = await _svc.StartAsync(SchoolA, DriverUser, default);
            Assert.Equal(409, r.StatusCode);
            Assert.Contains("try again", r.Message);
            Assert.Empty(_repo.Sessions);
        }

        // ------------------------------------------------------------------------------------------------ POST location: who and may it continue
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-a-real-token")]
        public async Task Location_with_a_missing_or_unknown_token_is_401_UNKNOWN_TOKEN(string? token)
        {
            await StartAsync();
            var r = await Ping(token);
            Assert.Equal(401, r.StatusCode);
            Assert.Equal(DutyCodes.UnknownToken, r.Data!.Code);
            Assert.Empty(_repo.Live);
        }

        [Fact]
        public async Task Location_authorises_before_it_validates_a_bad_body_with_an_unknown_token_is_401_not_400()
        {
            var r = await Ping("unknown", Fix(_clock.Now, lat: 999));
            Assert.Equal(401, r.StatusCode);
            Assert.Equal(401, (await Ping("unknown", null)).StatusCode);
        }

        [Fact]
        public async Task Location_after_the_session_ended_is_409_SESSION_ENDED()
        {
            var token = await StartAsync();
            await _svc.EndByTokenAsync(token, default);
            var r = await Ping(token);
            Assert.Equal(409, r.StatusCode);
            Assert.Equal(DutyCodes.SessionEnded, r.Data!.Code);
        }

        [Fact]
        public async Task Location_from_a_deactivated_driver_is_409_DRIVER_DEACTIVATED_and_closes_the_session()
        {
            var token = await StartAsync();
            await Ping(token);
            _repo.Store.Drivers.First(d => d.DriverId == "DPS001DR001").StatusId = TransportStore.Inactive;
            _clock.Advance(60);
            var r = await Ping(token);
            Assert.Equal(409, r.StatusCode);
            Assert.Equal(DutyCodes.DriverDeactivated, r.Data!.Code);
            Assert.Equal("DRIVER_DEACTIVATED", _repo.Sessions[0].EndReason);
            Assert.Empty(_repo.Live);
        }

        [Fact]
        public async Task Location_after_the_session_cap_is_409_SESSION_EXPIRED_and_closes_the_session()
        {
            var token = await StartAsync();
            _clock.Advance(16 * 3600);
            var r = await Ping(token);
            Assert.Equal(409, r.StatusCode);
            Assert.Equal(DutyCodes.SessionExpired, r.Data!.Code);
            Assert.Equal("SESSION_EXPIRED", _repo.Sessions[0].EndReason);
        }

        [Fact]
        public async Task Location_sooner_than_the_minimum_interval_is_429_with_a_retry_time_and_nothing_is_overwritten()
        {
            var token = await StartAsync();
            Assert.Equal(200, (await Ping(token, Fix(_clock.Now, lat: 26.0))).StatusCode);
            _clock.Advance(5);
            var r = await Ping(token, Fix(_clock.Now, lat: 27.0));
            Assert.Equal(429, r.StatusCode);
            Assert.Equal(DutyCodes.TooSoon, r.Data!.Code);
            Assert.Equal(15, r.Data.RetryAfterSeconds);
            Assert.Equal(26.0, _repo.Live[0].Latitude);
            _clock.Advance(15);
            Assert.Equal(200, (await Ping(token, Fix(_clock.Now, lat: 27.0))).StatusCode);
            Assert.Equal(27.0, _repo.Live[0].Latitude);
        }

        // ------------------------------------------------------------------------------------------------ POST location: values
        [Theory]
        [InlineData(91, 80)]
        [InlineData(-90.5, 80)]
        [InlineData(26, 180.5)]
        [InlineData(26, -181)]
        public async Task Location_with_impossible_coordinates_is_400(double lat, double lng)
        {
            var token = await StartAsync();
            var r = await Ping(token, Fix(_clock.Now, lat, lng));
            Assert.Equal(400, r.StatusCode);
            Assert.Equal(DutyCodes.Invalid, r.Data!.Code);
            Assert.Empty(_repo.Live);
        }

        [Fact]
        public async Task Location_with_missing_or_malformed_fields_is_400()
        {
            var token = await StartAsync();
            foreach (var tweak in new Action<DutyLocationRequest>[]
            {
                f => f.Latitude = null, f => f.Longitude = null, f => f.RecordedAt = null, f => f.AccuracyMeters = -1, f => f.SpeedMps = -0.1, f => f.HeadingDegrees = 361, f => f.HeadingDegrees = -1,
                f => f.Latitude = double.NaN
            })
                Assert.Equal(400, (await Ping(token, Fix(_clock.Now, tweak: tweak))).StatusCode);
            Assert.Equal(400, (await _svc.RecordLocationAsync(token, null, default)).StatusCode);                  // no body at all
            Assert.Empty(_repo.Live);
        }

        [Theory]
        [InlineData(200, true)]
        [InlineData(200.5, false)]
        [InlineData(5000, false)]
        public async Task Location_accuracy_over_200_m_is_dropped_with_status_200(double accuracy, bool accepted)
        {
            var token = await StartAsync();
            var r = await Ping(token, Fix(_clock.Now, tweak: f => f.AccuracyMeters = accuracy));
            Assert.Equal(200, r.StatusCode);
            Assert.Equal(accepted, r.Data!.Accepted);
            if (!accepted) Assert.Equal("LOW_ACCURACY", r.Data.Reason);
            Assert.Equal(accepted ? 1 : 0, _repo.Live.Count);
        }

        [Fact]
        public async Task Location_with_a_too_old_or_too_far_future_timestamp_is_dropped_with_status_200()
        {
            var token = await StartAsync();
            var old = await Ping(token, Fix(_clock.Now.AddSeconds(-601)));
            Assert.Equal(200, old.StatusCode); Assert.False(old.Data!.Accepted); Assert.Equal("STALE_FIX", old.Data.Reason);
            var future = await Ping(token, Fix(_clock.Now.AddSeconds(121)));
            Assert.Equal(200, future.StatusCode); Assert.False(future.Data!.Accepted); Assert.Equal("FUTURE_FIX", future.Data.Reason);
            Assert.Empty(_repo.Live);
            Assert.True((await Ping(token, Fix(_clock.Now.AddSeconds(-600)))).Data!.Accepted);          // the edge is inclusive
        }

        [Fact]
        public async Task Location_out_of_order_fixes_are_dropped_the_newest_one_stays()
        {
            var token = await StartAsync();
            Assert.True((await Ping(token, Fix(_clock.Now, lat: 26.5))).Data!.Accepted);
            _clock.Advance(30);
            var older = await Ping(token, Fix(_clock.Now.AddSeconds(-60), lat: 20.0));                   // recorded BEFORE the stored one
            Assert.Equal(200, older.StatusCode); Assert.False(older.Data!.Accepted); Assert.Equal("OUT_OF_ORDER", older.Data.Reason);
            Assert.Equal(26.5, _repo.Live[0].Latitude);
            var same = await Ping(token, Fix(_clock.Now.AddSeconds(-30), lat: 21.0));                    // the same recordedAt as the stored one
            Assert.False(same.Data!.Accepted); Assert.Equal("OUT_OF_ORDER", same.Data.Reason);
            Assert.True((await Ping(token, Fix(_clock.Now, lat: 28.0))).Data!.Accepted);
            Assert.Equal(28.0, _repo.Live[0].Latitude);
            Assert.Single(_repo.Live);                                                                   // latest only: one row, never a history
        }

        [Fact]
        public async Task Location_mock_flag_is_refused_only_when_RejectMockLocations_is_on()
        {
            var token = await StartAsync();
            _o.RejectMockLocations = true;
            var refused = await Ping(token, Fix(_clock.Now, tweak: f => f.Mocked = true));
            Assert.Equal(200, refused.StatusCode); Assert.False(refused.Data!.Accepted); Assert.Equal("MOCK_LOCATION", refused.Data.Reason);
            Assert.Empty(_repo.Live);
            _o.RejectMockLocations = false;
            Assert.True((await Ping(token, Fix(_clock.Now, tweak: f => f.Mocked = true))).Data!.Accepted);
            Assert.True(new TransportLiveOptions().RejectMockLocations);                                // ON unless configured off (production default)
        }

        [Fact]
        public async Task Location_stores_the_session_driver_school_and_vehicle_from_the_token_and_takes_the_server_time_as_received_at()
        {
            var token = await StartAsync();
            _clock.Advance(7);
            await Ping(token, Fix(_clock.Now.AddSeconds(-20), tweak: f => { f.HeadingDegrees = 360; f.SpeedMps = null; f.AccuracyMeters = null; }));
            var row = Assert.Single(_repo.Live);
            Assert.Equal(("DPS001DR001", SchoolA, _vehicle), (row.DriverId, row.SchoolId, row.VehicleId));
            Assert.Equal(_repo.Sessions[0].SessionId, row.SessionId);
            Assert.Equal(Utc, row.ReceivedAt);                                                           // server time, not the device's
            Assert.Equal(Utc.AddSeconds(-20), row.RecordedAt);
            Assert.Equal(0f, row.HeadingDeg);                                                            // 360 is north
            Assert.Null(row.SpeedMps); Assert.Null(row.AccuracyM);                                       // optional fields may be missing
        }

        // ------------------------------------------------------------------------------------------------ POST end
        [Fact]
        public async Task End_by_token_ends_the_session_deletes_the_live_row_and_is_idempotent()
        {
            var token = await StartAsync();
            await Ping(token);
            var r = await _svc.EndByTokenAsync(token, default);
            Assert.Equal(200, r.StatusCode); Assert.True(r.Data!.Ended);
            Assert.Equal("DRIVER_ENDED", _repo.Sessions[0].EndReason);
            Assert.Empty(_repo.Live);
            var again = await _svc.EndByTokenAsync(token, default);
            Assert.Equal(200, again.StatusCode); Assert.False(again.Data!.Ended);
            Assert.Equal(401, (await _svc.EndByTokenAsync("nope", default)).StatusCode);
            Assert.Equal(401, (await _svc.EndByTokenAsync(null, default)).StatusCode);
        }

        [Fact]
        public async Task End_with_the_drivers_login_needs_no_token()
        {
            await StartAsync();
            Assert.True((await _svc.EndForDriverAsync(SchoolA, DriverUser, default)).Data!.Ended);
            Assert.False((await _svc.EndForDriverAsync(SchoolA, DriverUser, default)).Data!.Ended);       // nothing open: still a 200
            Assert.Equal(403, (await _svc.EndForDriverAsync(SchoolA, OtherSchoolDriverUser, default)).StatusCode);
            Assert.Equal(400, (await _svc.EndForDriverAsync(null, DriverUser, default)).StatusCode);
        }

        [Fact]
        public async Task A_driver_can_go_on_duty_again_the_same_day_several_sessions()
        {
            var t1 = await StartAsync();
            await _svc.EndByTokenAsync(t1, default);
            var t2 = await StartAsync();
            await _svc.EndByTokenAsync(t2, default);
            await StartAsync();
            Assert.Equal(3, _repo.Sessions.Count);
            Assert.Single(_repo.Sessions.Where(s => s.EndedAt == null));
        }

        // ------------------------------------------------------------------------------------------------ GET live
        private async Task<string> OnDuty(string driverId, long user, string school, string name, double lat = 26.85)
        {
            var d = _repo.Store.Drivers.FirstOrDefault(x => x.DriverId == driverId);
            if (d == null)
            {
                var v = _repo.Store.AddVehicle(school, "V-" + driverId, "UP" + driverId.GetHashCode().ToString("X")[..6]);
                _repo.Store.AddDriver(school, driverId, name, vehicleId: v.VehicleId, dl: "DL" + driverId);
                _repo.Store.Users.Add(new TbUsers { UserId = user, SchoolId = school, UserType = "Driver", OrgUserId = driverId, DisplayName = name });
            }
            var token = await StartAsync(user, school);
            await _svc.RecordLocationAsync(token, Fix(_clock.Now, lat), default);
            return token;
        }

        [Theory]
        [InlineData(0, "LIVE")]
        [InlineData(180, "LIVE")]
        [InlineData(181, "STALE")]
        [InlineData(600, "STALE")]
        [InlineData(601, "OFFLINE")]
        public async Task Live_status_is_computed_from_the_server_time_of_the_last_fix(int ageSeconds, string expected)
        {
            await OnDuty("DPS001DR001", DriverUser, SchoolA, "Ramesh Kumar");
            _clock.Advance(ageSeconds);
            var item = Assert.Single((await _svc.GetLiveAsync(SchoolA, null, null, default)).Data!.Items);
            Assert.Equal(expected, item.Status);
            Assert.Equal(ageSeconds, item.AgeSeconds);
            Assert.True(item.HasFix);
        }

        [Fact]
        public async Task Live_a_session_with_no_fix_yet_is_listed_OFFLINE_without_a_position()
        {
            await StartAsync();
            var item = Assert.Single((await _svc.GetLiveAsync(SchoolA, null, null, default)).Data!.Items);
            Assert.Equal("OFFLINE", item.Status);
            Assert.False(item.HasFix);
            Assert.Null(item.Latitude); Assert.Null(item.Longitude); Assert.Null(item.AgeSeconds);
        }

        [Fact]
        public async Task Live_hides_an_OFFLINE_driver_after_the_hide_time_without_closing_the_session()
        {
            await OnDuty("DPS001DR001", DriverUser, SchoolA, "Ramesh Kumar");
            _clock.Advance(60 * 60);
            Assert.Single((await _svc.GetLiveAsync(SchoolA, null, null, default)).Data!.Items);          // 60 minutes: still shown, greyed
            _clock.Advance(2);
            Assert.Empty((await _svc.GetLiveAsync(SchoolA, null, null, default)).Data!.Items);
            Assert.Null(_repo.Sessions[0].EndedAt);                                                       // read-time filter only
        }

        [Fact]
        public async Task Live_never_shows_another_schools_drivers_a_deactivated_driver_or_an_ended_session()
        {
            await OnDuty("DPS001DR001", DriverUser, SchoolA, "Ramesh Kumar");
            await OnDuty("XYZ002DR001", OtherSchoolDriverUser, SchoolB, "Other School Driver");
            Assert.Equal("DPS001DR001", Assert.Single((await _svc.GetLiveAsync(SchoolA, null, null, default)).Data!.Items).DriverId);
            Assert.Equal("XYZ002DR001", Assert.Single((await _svc.GetLiveAsync(SchoolB, null, null, default)).Data!.Items).DriverId);
            _repo.Store.Drivers.First(d => d.DriverId == "DPS001DR001").StatusId = TransportStore.Inactive;
            Assert.Empty((await _svc.GetLiveAsync(SchoolA, null, null, default)).Data!.Items);
            Assert.Equal(400, (await _svc.GetLiveAsync(null, null, null, default)).StatusCode);
        }

        [Fact]
        public async Task Live_filters_by_area_and_vehicle_and_counts_each_status()
        {
            await OnDuty("DPS001DR001", DriverUser, SchoolA, "Ramesh Kumar");
            _clock.Advance(200);                                                                          // Ramesh is now STALE
            await OnDuty("DPS001DR002", 502, SchoolA, "Suresh Yadav");                                    // LIVE
            var board = (await _svc.GetLiveAsync(SchoolA, null, null, default)).Data!;
            Assert.Equal((1, 1, 0), (board.Counts.Live, board.Counts.Stale, board.Counts.Offline));
            Assert.Equal(new[] { "Ramesh Kumar", "Suresh Yadav" }, board.Items.Select(i => i.DriverName));
            Assert.Equal(new[] { "Civil Lines", "Ring Road" }, board.Items[0].Areas);
            var civil = _repo.Store.Areas.First(a => a.AreaName == "Civil Lines").AreaId;
            Assert.Equal("Ramesh Kumar", Assert.Single((await _svc.GetLiveAsync(SchoolA, civil, null, default)).Data!.Items).DriverName);
            Assert.Equal("Ramesh Kumar", Assert.Single((await _svc.GetLiveAsync(SchoolA, null, _vehicle, default)).Data!.Items).DriverName);
        }

        [Fact]
        public async Task Live_reports_speed_in_km_per_h()
        {
            var token = await StartAsync();
            await Ping(token, Fix(_clock.Now, tweak: f => f.SpeedMps = 10));
            Assert.Equal(36, Assert.Single((await _svc.GetLiveAsync(SchoolA, null, null, default)).Data!.Items).SpeedKmh);
        }

        // ------------------------------------------------------------------------------------------------ the tracking token is a secret
        [Fact]
        public async Task The_tracking_token_never_reaches_a_log_a_response_or_an_exception()
        {
            var token = await StartAsync();
            var bodies = new List<string>();
            string Json(object o) => JsonSerializer.Serialize(o);

            bodies.Add(Json(await Ping(token)));
            _clock.Advance(30);
            bodies.Add(Json(await Ping("wrong-" + token)));                                            // an unknown token must not be echoed either
            bodies.Add(Json(await Ping(token, Fix(_clock.Now, lat: 999))));
            _repo.ThrowOnUpsert = true;                                                                  // a database failure: Error() logs the exception
            _clock.Advance(30);
            var failed = await Ping(token);
            Assert.Equal(500, failed.StatusCode);
            bodies.Add(Json(failed));
            _repo.ThrowOnUpsert = false;
            bodies.Add(Json(await _svc.EndByTokenAsync(token, default)));
            bodies.Add(Json(await Ping(token)));                                                         // after the end

            Assert.NotEmpty(_log.Lines);                                                                 // the failure was logged ...
            foreach (var line in _log.Lines) Assert.DoesNotContain(token, line);                         // ... without the token
            foreach (var body in bodies) Assert.DoesNotContain(token, body);
            foreach (var line in _log.Lines) Assert.DoesNotContain("wrong-", line);
        }

        [Fact]
        public void Tokens_are_random_url_safe_and_hash_to_64_hex_characters()
        {
            var tokens = Enumerable.Range(0, 1000).Select(_ => DutyRules.NewToken()).ToList();
            Assert.Equal(1000, tokens.Distinct().Count());
            Assert.All(tokens, t => { Assert.Equal(43, t.Length); Assert.Matches("^[A-Za-z0-9_-]+$", t); });
            var h = DutyRules.Hash(tokens[0]);
            Assert.Matches("^[0-9a-f]{64}$", h);
            Assert.NotEqual(h, DutyRules.Hash(tokens[1]));
        }
    }

    // ======================================================================================================================================
    // Controllers: roles first, and the token header is not part of anything that could log it.
    // ======================================================================================================================================
    public class DriverDutyAuthorizationTests
    {
        private const string School = "SCH1";
        private static readonly string[] NotDriver = { "School Admin", "Principal", "Teacher", "Accountant", "Admission Staff", "Student", "Guardian", "Super Admin" };

        private static (DriverDutyController Controller, IDriverDutyService Service) BuildDuty(params string[] roles)
        {
            var svc = Fake.Of<IDriverDutyService>();
            return (Fake.WithUser(new DriverDutyController(svc), Fake.User(School, 7, roles)), svc);
        }

        private static IEnumerable<MethodInfo> Endpoints(Type c) =>
            c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.GetCustomAttributes().Any(a => a is HttpMethodAttribute));

        [Fact]
        public async Task Me_and_start_refuse_every_role_except_Driver_before_calling_the_service()
        {
            foreach (var role in NotDriver)
            {
                var (c, svc) = BuildDuty(role);
                Assert.Equal(403, Fake.Code(await c.GetMe(default)));
                Assert.Equal(403, Fake.Code(await c.Start(default)));
                Assert.Empty(Fake.ProxyOf(svc).Calls);
            }
            Assert.Equal(403, Fake.Code(await BuildDuty().Controller.GetMe(default)));                    // a login with no role at all
            Assert.NotEqual(403, Fake.Code(await BuildDuty("Driver").Controller.GetMe(default)));
            Assert.NotEqual(403, Fake.Code(await BuildDuty("Driver").Controller.Start(default)));
        }

        [Fact]
        public async Task End_with_a_login_refuses_every_role_except_Driver_and_without_either_credential_is_401()
        {
            foreach (var role in NotDriver)
            {
                var (c, svc) = BuildDuty(role);
                Assert.Equal(403, Fake.Code(await c.End(default)));
                Assert.Empty(Fake.ProxyOf(svc).Calls);
            }
            var anonymous = new DriverDutyController(Fake.Of<IDriverDutyService>());
            anonymous.ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };
            Assert.Equal(401, Fake.Code(await anonymous.End(default)));
        }

        [Fact]
        public async Task Location_and_end_read_the_token_only_from_the_X_Duty_Token_header()
        {
            var seen = new List<string?>();
            var svc = Fake.Of<IDriverDutyService>(new()
            {
                ["RecordLocationAsync"] = (Func<object?[], object?>)(a => { seen.Add((string?)a[0]); return Fake.Ok(new DutyLocationResultDto { Accepted = true }); }),
                ["EndByTokenAsync"] = (Func<object?[], object?>)(a => { seen.Add((string?)a[0]); return Fake.Ok(new DutyEndedDto { Ended = true }); }),
            });
            var c = new DriverDutyController(svc) { ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() } };
            c.Request.Headers[DriverDutyController.TokenHeader] = "dummy-header-value";
            await c.Location(new DutyLocationRequest(), default);
            await c.End(default);
            Assert.Equal(new string?[] { "dummy-header-value", "dummy-header-value" }, seen);
            Assert.Equal("X-Duty-Token", DriverDutyController.TokenHeader);

            seen.Clear();
            var noHeader = new DriverDutyController(svc) { ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() } };
            await noHeader.Location(new DutyLocationRequest(), default);
            Assert.Equal(new string?[] { null }, seen);                                    // no header: the service answers 401, nothing else identifies the caller
        }

        [Fact]
        public async Task A_429_answer_carries_the_Retry_After_header_and_the_responses_are_not_cached()
        {
            var svc = Fake.Of<IDriverDutyService>(new() { ["RecordLocationAsync"] = new ApiResponse<DutyLocationResultDto> { Status = false, StatusCode = 429, Message = "x", Data = new() { Code = "TOO_SOON", RetryAfterSeconds = 15 } } });
            var c = new DriverDutyController(svc) { ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() } };
            var r = await c.Location(new DutyLocationRequest(), default);
            Assert.Equal(429, Fake.Code(r));
            Assert.Equal("15", c.Response.Headers.RetryAfter.ToString());
            Assert.Equal("no-store", c.Response.Headers.CacheControl.ToString());
        }

        [Fact]
        public async Task Live_is_for_School_Admin_and_Principal_only()
        {
            foreach (var role in new[] { "Driver", "Student", "Teacher", "Accountant", "Admission Staff", "Guardian", "Super Admin" })
            {
                var svc = Fake.Of<IDriverDutyService>();
                var c = Fake.WithUser(new TransportLiveController(svc), Fake.User(School, 7, role));
                Assert.Equal(403, Fake.Code(await c.Live(null, null, default)));
                Assert.Empty(Fake.ProxyOf(svc).Calls);
            }
            foreach (var role in new[] { "School Admin", "Principal" })
                Assert.NotEqual(403, Fake.Code(await Fake.WithUser(new TransportLiveController(Fake.Of<IDriverDutyService>()), Fake.User(School, 7, role)).Live(null, null, default)));
        }

        [Fact]
        public void Only_location_and_end_are_anonymous_and_both_are_rate_limited_everything_else_needs_a_login()
        {
            foreach (var m in Endpoints(typeof(DriverDutyController)))
            {
                var anonymous = m.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>() != null;
                var expected = m.Name is "Location" or "End";
                Assert.True(anonymous == expected, $"{m.Name}: anonymous={anonymous}");
                if (expected) Assert.NotNull(m.GetCustomAttribute<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>());
            }
            Assert.NotNull(typeof(DriverDutyController).GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
            Assert.NotNull(typeof(TransportLiveController).GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
        }
    }
}
