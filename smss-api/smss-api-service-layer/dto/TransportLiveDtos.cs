namespace smss_api_service_layer.dto
{
    // Stage 2: driver duty and live location. Dates are UTC. Coordinates travel only on /location (in) and /live (out to School Admin / Principal).

    public class DutyVehicleDto
    {
        public long VehicleId { get; set; }
        public string VehicleLabel { get; set; } = null!;
        public string VehiclePlate { get; set; } = null!;
        public bool IsActive { get; set; }
    }

    public class DutyAreaDto
    {
        public long AreaId { get; set; }
        public string AreaName { get; set; } = null!;
        /// <summary>The driver's own approximate morning time for this area, "HH:mm", or null.</summary>
        public string? MorningPickupTime { get; set; }
        /// <summary>The driver's own approximate afternoon time for this area, "HH:mm", or null.</summary>
        public string? AfternoonDropTime { get; set; }
    }

    /// <summary>Today's duty state of the signed-in driver.</summary>
    public class DutyStateDto
    {
        public bool OnDuty { get; set; }
        public long? SessionId { get; set; }
        public DateTime? StartedAt { get; set; }
        /// <summary>LIVE | STALE | OFFLINE while a session is open (OFFLINE until the first fix arrives); null when off duty.</summary>
        public string? Status { get; set; }
        public DateTime? LastSentAt { get; set; }
        public int? LastSentAgeSeconds { get; set; }
    }

    /// <summary>GET api/Drivers/me (Driver only).</summary>
    public class DriverMeDto
    {
        public string DriverId { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public DutyVehicleDto? Vehicle { get; set; }
        public List<DutyAreaDto> Areas { get; set; } = new();
        public DutyStateDto Duty { get; set; } = new();
        /// <summary>False when duty cannot be started (no vehicle, inactive vehicle); StartBlockedReason says why.</summary>
        public bool CanStartDuty { get; set; }
        public string? StartBlockedReason { get; set; }
        public int IntervalSeconds { get; set; }
        public int MinIntervalSeconds { get; set; }
        public DateTime ServerTime { get; set; }
    }

    /// <summary>POST api/DriverDuty/start. The tracking token appears here ONCE (only its hash is stored); a resume issues a NEW token and the old one stops working.</summary>
    public class DutyStartedDto
    {
        public long SessionId { get; set; }
        public DateTime StartedAt { get; set; }
        /// <summary>True when an open, still-fresh session was resumed instead of a new one created.</summary>
        public bool Resumed { get; set; }
        public DutyVehicleDto Vehicle { get; set; } = null!;
        public string TrackingToken { get; set; } = null!;
        /// <summary>The session cannot be used for pings after this moment (the hard cap on one session).</summary>
        public DateTime ExpiresAt { get; set; }
        public int IntervalSeconds { get; set; }
        public int MinIntervalSeconds { get; set; }
    }

    /// <summary>POST api/DriverDuty/location body. Validated by the service AFTER the token is checked (so an unknown token is always 401).</summary>
    public class DutyLocationRequest
    {
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public double? AccuracyMeters { get; set; }
        public double? SpeedMps { get; set; }
        public double? HeadingDegrees { get; set; }
        /// <summary>When the device took the fix (ISO 8601 with a Z or an offset). Orders fixes; never decides LIVE / STALE.</summary>
        public DateTimeOffset? RecordedAt { get; set; }
        /// <summary>The device's "mock provider" flag (Android). Refused when TransportLive:RejectMockLocations is on.</summary>
        public bool? Mocked { get; set; }
    }

    /// <summary>
    /// The answer to a ping. Accepted=false with a Reason is a DROP (status 200: the data was unusable, send the next one). On a refusal that makes the app STOP
    /// (401 / 409) or retry (429) the same shape carries Code. See smss-mobile/CLAUDE.md for the table the app follows.
    /// </summary>
    public class DutyLocationResultDto
    {
        public bool Accepted { get; set; }
        /// <summary>LOW_ACCURACY | STALE_FIX | FUTURE_FIX | OUT_OF_ORDER | MOCK_LOCATION (only with status 200 and Accepted=false).</summary>
        public string? Reason { get; set; }
        /// <summary>UNKNOWN_TOKEN (401) | SESSION_ENDED | SESSION_EXPIRED | DRIVER_DEACTIVATED (409) | TOO_SOON (429) | INVALID (400).</summary>
        public string? Code { get; set; }
        public int? RetryAfterSeconds { get; set; }
        public DateTime ServerTime { get; set; }
    }

    public class DutyEndedDto
    {
        /// <summary>True when this call ended the session; false when there was nothing open (ending twice is not an error).</summary>
        public bool Ended { get; set; }
        public DateTime ServerTime { get; set; }
    }

    public class LiveDriverDto
    {
        public string DriverId { get; set; } = null!;
        public string DriverName { get; set; } = null!;
        public long VehicleId { get; set; }
        public string VehicleLabel { get; set; } = null!;
        public string VehiclePlate { get; set; } = null!;
        public List<string> Areas { get; set; } = new();
        public bool HasFix { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public double? AccuracyMeters { get; set; }
        public double? SpeedKmh { get; set; }
        public double? HeadingDegrees { get; set; }
        /// <summary>LIVE | STALE | OFFLINE, computed from the server time the last fix arrived.</summary>
        public string Status { get; set; } = null!;
        public DateTime? UpdatedAt { get; set; }
        public int? AgeSeconds { get; set; }
        public DateTime SessionStartedAt { get; set; }
    }

    public class LiveCountsDto
    {
        public int Live { get; set; }
        public int Stale { get; set; }
        public int Offline { get; set; }
    }

    public class LiveBoardDto
    {
        public DateTime ServerTime { get; set; }
        public LiveCountsDto Counts { get; set; } = new();
        public List<LiveDriverDto> Items { get; set; } = new();
    }
}
