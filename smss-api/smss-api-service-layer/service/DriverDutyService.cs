using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>
    /// Duty sessions and the latest location of drivers. Identity: the Driver login's id from the JWT (start / me / end) or the tracking token (location / end).
    /// The tracking token is only ever hashed here: it is never logged, stored or included in a message or an exception.
    /// </summary>
    public class DriverDutyService : TransportServiceBase, IDriverDutyService
    {
        private readonly IDriverDutyRepository _repo;
        private readonly TransportLiveOptions _o;
        private readonly TimeProvider _clock;
        private readonly ILogger _log;

        public DriverDutyService(IDriverDutyRepository repo, IOptions<TransportLiveOptions> options, TimeProvider clock, ILogger<DriverDutyService> logger) : base(logger)
        {
            _repo = repo;
            _o = options.Value;
            _clock = clock;
            _log = logger;
        }

        private DateTime Now => _clock.GetUtcNow().UtcDateTime;
        private Task<int?> ActiveIdAsync(CancellationToken ct) => _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);

        private static DutyLocationResultDto Refusal(string code, DateTime now, int? retryAfter = null) => new() { Accepted = false, Code = code, RetryAfterSeconds = retryAfter, ServerTime = now };

        private static DutyVehicleDto? VehicleOf(DutyDriver d, int activeId) =>
            d.VehicleId == null || d.VehicleLabel == null ? null : new DutyVehicleDto { VehicleId = d.VehicleId.Value, VehicleLabel = d.VehicleLabel, VehiclePlate = d.VehiclePlate ?? "", IsActive = d.VehicleStatusId == activeId };

        /// <summary>null when duty can start, otherwise why not.</summary>
        private static string? StartBlock(DutyDriver d, int activeId) =>
            d.StatusId != activeId ? "Your driver account is inactive. Please contact your school."
            : d.VehicleId == null ? "No vehicle is assigned to you. Ask your school to assign one."
            : d.VehicleStatusId != activeId ? $"Your vehicle {d.VehicleLabel} is inactive. Ask your school to assign an active vehicle." : null;

        // ---------------------------------------------------------------- GET api/Drivers/me
        public async Task<ApiResponse<DriverMeDto>> GetMeAsync(string? schoolId, long userId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<DriverMeDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await ActiveIdAsync(ct);
                if (activeId == null) return ConfigError<DriverMeDto>($"Status '{StatusNames.Active}' ({StatusNames.GeneralType}) is missing in lut_status");
                var d = await _repo.GetDutyDriverAsync(schoolId, userId, ct);
                if (d == null) return Result<DriverMeDto>(403, "This login is not a driver of your school.", null);

                var now = Now;
                var areas = await _repo.GetAssignedAreasAsync(schoolId, d.DriverId, ct);
                var open = await _repo.GetOpenSessionAsync(schoolId, d.DriverId, ct);
                var duty = new DutyStateDto();
                if (open != null)
                {
                    var live = await _repo.GetLiveAsync(d.DriverId, ct);
                    var last = live != null && live.SessionId == open.SessionId ? live.ReceivedAt : (DateTime?)null;
                    duty = new DutyStateDto
                    {
                        OnDuty = true, SessionId = open.SessionId, StartedAt = open.StartedAt, Status = DutyRules.Status(last, now, _o),
                        LastSentAt = last, LastSentAgeSeconds = last == null ? null : DutyRules.AgeSeconds(last.Value, now)
                    };
                }
                var block = StartBlock(d, activeId.Value);
                return Result(200, "Successfully fetched", new DriverMeDto
                {
                    DriverId = d.DriverId, FullName = d.FullName, Vehicle = VehicleOf(d, activeId.Value),
                    Areas = areas.Select(a => new DutyAreaDto { AreaId = a.AreaId, AreaName = a.Name, MorningPickupTime = TransportRules.FormatTime(a.MorningPickupTime), AfternoonDropTime = TransportRules.FormatTime(a.AfternoonDropTime) }).ToList(),
                    Duty = duty, CanStartDuty = block == null, StartBlockedReason = block,
                    IntervalSeconds = _o.IntervalSeconds, MinIntervalSeconds = _o.MinIntervalSeconds, ServerTime = now
                });
            }
            catch (Exception ex) { return Error<DriverMeDto>(ex, "fetching the driver's own profile"); }
        }

        // ---------------------------------------------------------------- POST api/DriverDuty/start
        public async Task<ApiResponse<DutyStartedDto>> StartAsync(string? schoolId, long userId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<DutyStartedDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await ActiveIdAsync(ct);
                if (activeId == null) return ConfigError<DutyStartedDto>($"Status '{StatusNames.Active}' ({StatusNames.GeneralType}) is missing in lut_status");
                var d = await _repo.GetDutyDriverAsync(schoolId, userId, ct);
                if (d == null) return Result<DutyStartedDto>(403, "This login is not a driver of your school.", null);
                if (d.StatusId != activeId) return Result<DutyStartedDto>(403, StartBlock(d, activeId.Value)!, null);
                if (StartBlock(d, activeId.Value) is { } blocked) return Result<DutyStartedDto>(409, blocked, null);

                var now = Now;
                var token = DutyRules.NewToken();
                var hash = DutyRules.Hash(token);
                var open = await _repo.GetOpenSessionAsync(schoolId, d.DriverId, ct);
                TbDriverDutySession? session;
                var resumed = false;

                if (open != null && await IsFreshAsync(open, d, now, ct))
                {
                    // the same duty carries on (the app restarted, the phone rebooted): a NEW token, the old one stops working
                    if (!await _repo.ResumeSessionAsync(open.SessionId, hash, ct)) return Result<DutyStartedDto>(409, "Duty changed while starting. Please try again.", null);
                    session = open;
                    resumed = true;
                }
                else
                {
                    if (open != null) _log.LogInformation("Closing stale duty session {SessionId} of driver {DriverId} (AUTO_TIMEOUT)", open.SessionId, d.DriverId);
                    session = await _repo.StartSessionAsync(schoolId, d.DriverId, d.VehicleId!.Value, hash, open?.SessionId, now, ct);
                    if (session == null) return Result<DutyStartedDto>(409, "Duty is already being started. Please try again.", null);
                }

                return Result(resumed ? 200 : 201, resumed ? "Duty resumed" : "Duty started", new DutyStartedDto
                {
                    SessionId = session.SessionId, StartedAt = session.StartedAt, Resumed = resumed, Vehicle = VehicleOf(d, activeId.Value)!, TrackingToken = token,
                    ExpiresAt = session.StartedAt.AddHours(_o.MaxSessionHours), IntervalSeconds = _o.IntervalSeconds, MinIntervalSeconds = _o.MinIntervalSeconds
                });
            }
            catch (Exception ex) { return Error<DutyStartedDto>(ex, "starting duty"); }
        }

        /// <summary>An open session is worth resuming when it is the same vehicle, inside the hard cap and had activity (a fix, else its start) recently.</summary>
        private async Task<bool> IsFreshAsync(TbDriverDutySession open, DutyDriver d, DateTime now, CancellationToken ct)
        {
            if (open.VehicleId != d.VehicleId) return false;
            if ((now - open.StartedAt).TotalHours >= _o.MaxSessionHours) return false;
            var live = await _repo.GetLiveAsync(d.DriverId, ct);
            var last = live != null && live.SessionId == open.SessionId ? live.ReceivedAt : open.StartedAt;
            return (now - last).TotalSeconds <= _o.ResumeWithinSeconds;
        }

        // ---------------------------------------------------------------- POST api/DriverDuty/location
        public async Task<ApiResponse<DutyLocationResultDto>> RecordLocationAsync(string? trackingToken, DutyLocationRequest? req, CancellationToken ct)
        {
            var now = Now;
            try
            {
                // 1. who is this: the token, and nothing else
                var ctx = await FindSessionAsync(trackingToken, ct);
                if (ctx == null) return Result(401, "The tracking token is not valid. Sign in again and start duty.", Refusal(DutyCodes.UnknownToken, now));
                var s = ctx.Session;

                // 2. may this session still send fixes
                if (s.EndedAt != null) return Result(409, "This duty session has ended.", Refusal(DutyCodes.SessionEnded, now));
                var activeId = await ActiveIdAsync(ct);
                if (activeId == null) return ConfigError<DutyLocationResultDto>($"Status '{StatusNames.Active}' ({StatusNames.GeneralType}) is missing in lut_status");
                if (ctx.DriverStatusId != activeId)
                {
                    await _repo.EndSessionAsync(s.SessionId, DutyEndReasons.DriverDeactivated, now, ct);
                    return Result(409, "Your driver account is inactive, so duty has ended.", Refusal(DutyCodes.DriverDeactivated, now));
                }
                if ((now - s.StartedAt).TotalHours >= _o.MaxSessionHours)
                {
                    await _repo.EndSessionAsync(s.SessionId, DutyEndReasons.SessionExpired, now, ct);
                    return Result(409, "This duty session has expired. Start duty again.", Refusal(DutyCodes.SessionExpired, now));
                }

                // 3. minimum interval (the server decides, not the app)
                var previous = await _repo.GetLiveAsync(s.DriverId, ct);
                if (previous != null && previous.SessionId == s.SessionId)
                {
                    var wait = _o.MinIntervalSeconds - (now - previous.ReceivedAt).TotalSeconds;
                    if (wait > 0) return Result(429, "Locations are being sent too often.", Refusal(DutyCodes.TooSoon, now, (int)Math.Ceiling(wait)));
                }

                // 4. malformed or impossible values: 400
                if (BadValue(req) is { } bad) return Result(400, bad, Refusal(DutyCodes.Invalid, now));
                var r = req!;

                // 5. valid but unusable data: 200 with Accepted=false (the app drops it and sends the next one)
                var recorded = r.RecordedAt!.Value.UtcDateTime;
                if (r.AccuracyMeters is { } acc && acc > _o.MaxAccuracyMeters) return Dropped("LOW_ACCURACY", now);
                if ((now - recorded).TotalSeconds > _o.MaxFixAgeSeconds) return Dropped("STALE_FIX", now);
                if ((recorded - now).TotalSeconds > _o.MaxFutureSeconds) return Dropped("FUTURE_FIX", now);
                if (r.Mocked == true && _o.RejectMockLocations) return Dropped("MOCK_LOCATION", now);

                // 6. the latest-only write; 0 rows = an equal or newer fix is already stored
                var heading = r.HeadingDegrees is { } h ? (float)(h >= 360 ? 0 : h) : (float?)null;
                var written = await _repo.UpsertLocationAsync(s.SchoolId, s.DriverId, s.SessionId, s.VehicleId,
                    new LocationFix(r.Latitude!.Value, r.Longitude!.Value, (float?)r.AccuracyMeters, (float?)r.SpeedMps, heading, recorded, now), ct);
                return written == 0 ? Dropped("OUT_OF_ORDER", now) : Result(200, "Location received", new DutyLocationResultDto { Accepted = true, ServerTime = now });
            }
            catch (Exception ex) { return Error<DutyLocationResultDto>(ex, "recording a driver location"); }
        }

        private static ApiResponse<DutyLocationResultDto> Dropped(string reason, DateTime now) =>
            Result(200, "Location not used", new DutyLocationResultDto { Accepted = false, Reason = reason, ServerTime = now });

        /// <summary>null when the values are well-formed; otherwise the 400 message.</summary>
        private static string? BadValue(DutyLocationRequest? r)
        {
            if (r == null) return "A body is required.";
            if (r.Latitude is not { } lat || double.IsNaN(lat) || lat < -90 || lat > 90) return "latitude must be a number between -90 and 90.";
            if (r.Longitude is not { } lng || double.IsNaN(lng) || lng < -180 || lng > 180) return "longitude must be a number between -180 and 180.";
            if (r.RecordedAt == null) return "recordedAt is required (ISO 8601 with a Z or an offset).";
            if (r.AccuracyMeters is { } a && (double.IsNaN(a) || a < 0)) return "accuracyMeters cannot be negative.";
            if (r.SpeedMps is { } sp && (double.IsNaN(sp) || sp < 0)) return "speedMps cannot be negative.";
            if (r.HeadingDegrees is { } h && (double.IsNaN(h) || h < 0 || h > 360)) return "headingDegrees must be between 0 and 360.";
            return null;
        }

        private async Task<DutySessionContext?> FindSessionAsync(string? token, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length > DutyRules.MaxTokenLength) return null;
            return await _repo.GetSessionByTokenHashAsync(DutyRules.Hash(token.Trim()), ct);
        }

        // ---------------------------------------------------------------- POST api/DriverDuty/end
        public async Task<ApiResponse<DutyEndedDto>> EndByTokenAsync(string? trackingToken, CancellationToken ct)
        {
            var now = Now;
            try
            {
                var ctx = await FindSessionAsync(trackingToken, ct);
                if (ctx == null) return Result(401, "The tracking token is not valid.", new DutyEndedDto { Ended = false, ServerTime = now });
                var ended = ctx.Session.EndedAt == null && await _repo.EndSessionAsync(ctx.Session.SessionId, DutyEndReasons.DriverEnded, now, ct);
                return Result(200, ended ? "Duty ended" : "No duty was open", new DutyEndedDto { Ended = ended, ServerTime = now });
            }
            catch (Exception ex) { return Error<DutyEndedDto>(ex, "ending duty"); }
        }

        public async Task<ApiResponse<DutyEndedDto>> EndForDriverAsync(string? schoolId, long userId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<DutyEndedDto>(400, TenantMessages.NoSchool, null);
            var now = Now;
            try
            {
                var d = await _repo.GetDutyDriverAsync(schoolId, userId, ct);
                if (d == null) return Result<DutyEndedDto>(403, "This login is not a driver of your school.", null);
                var open = await _repo.GetOpenSessionAsync(schoolId, d.DriverId, ct);
                var ended = open != null && await _repo.EndSessionAsync(open.SessionId, DutyEndReasons.DriverEnded, now, ct);
                return Result(200, ended ? "Duty ended" : "No duty was open", new DutyEndedDto { Ended = ended, ServerTime = now });
            }
            catch (Exception ex) { return Error<DutyEndedDto>(ex, "ending duty"); }
        }

        // ---------------------------------------------------------------- GET api/Transport/live
        public async Task<ApiResponse<LiveBoardDto>> GetLiveAsync(string? schoolId, long? areaId, long? vehicleId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<LiveBoardDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await ActiveIdAsync(ct);
                if (activeId == null) return ConfigError<LiveBoardDto>($"Status '{StatusNames.Active}' ({StatusNames.GeneralType}) is missing in lut_status");
                var now = Now;
                var rows = await _repo.ListOpenAsync(schoolId, areaId, vehicleId, activeId.Value, ct);
                var areas = await _repo.GetAreaNamesAsync(schoolId, rows.Select(r => r.DriverId).ToList(), ct);

                var board = new LiveBoardDto { ServerTime = now };
                foreach (var r in rows)
                {
                    var status = DutyRules.Status(r.ReceivedAt, now, _o);
                    // an OFFLINE driver is hidden after a while (read-time only: the session stays open until the driver starts again)
                    if (status == DutyStatuses.Offline && (now - (r.ReceivedAt ?? r.SessionStartedAt)).TotalMinutes > _o.HideOfflineAfterMinutes) continue;
                    board.Items.Add(new LiveDriverDto
                    {
                        DriverId = r.DriverId, DriverName = r.DriverName, VehicleId = r.VehicleId, VehicleLabel = r.VehicleLabel, VehiclePlate = r.VehiclePlate,
                        Areas = areas.GetValueOrDefault(r.DriverId) ?? new(), HasFix = r.ReceivedAt != null, Latitude = r.Latitude, Longitude = r.Longitude,
                        AccuracyMeters = r.AccuracyM, SpeedKmh = r.SpeedMps == null ? null : Math.Round(r.SpeedMps.Value * 3.6, 1), HeadingDegrees = r.HeadingDeg,
                        Status = status, UpdatedAt = r.ReceivedAt, AgeSeconds = r.ReceivedAt == null ? null : DutyRules.AgeSeconds(r.ReceivedAt.Value, now), SessionStartedAt = r.SessionStartedAt
                    });
                }
                board.Counts = new LiveCountsDto
                {
                    Live = board.Items.Count(i => i.Status == DutyStatuses.Live), Stale = board.Items.Count(i => i.Status == DutyStatuses.Stale), Offline = board.Items.Count(i => i.Status == DutyStatuses.Offline)
                };
                return Result(200, "Successfully fetched", board);
            }
            catch (Exception ex) { return Error<LiveBoardDto>(ex, "fetching the live board"); }
        }
    }
}
