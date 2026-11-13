using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    /// <summary>The driver as the duty endpoints see them: the login (tb_users) joined to the driver row and the vehicle.</summary>
    public record DutyDriver(long UserId, string SchoolId, string DriverId, string FullName, int StatusId, long? VehicleId, string? VehicleLabel, string? VehiclePlate, int? VehicleStatusId);

    /// <summary>A session found by its token hash, with the driver's current status (checked on every ping).</summary>
    public record DutySessionContext(TbDriverDutySession Session, string DriverName, int DriverStatusId);

    /// <summary>One open session for the live board; latitude/longitude/receivedAt are null until the first fix arrives.</summary>
    public record LiveRow(string DriverId, string DriverName, long VehicleId, string VehicleLabel, string VehiclePlate, long SessionId, DateTime SessionStartedAt,
        double? Latitude, double? Longitude, float? AccuracyM, float? SpeedMps, float? HeadingDeg, DateTime? ReceivedAt);

    /// <summary>The values of one accepted fix, already validated by the service.</summary>
    public record LocationFix(double Latitude, double Longitude, float? AccuracyM, float? SpeedMps, float? HeadingDeg, DateTime RecordedAt, DateTime ReceivedAt);

    public interface IDriverDutyRepository
    {
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct);

        /// <summary>The Driver login <paramref name="userId"/> of this school with its driver row; null when the user is not a driver of that school.</summary>
        Task<DutyDriver?> GetDutyDriverAsync(string schoolId, long userId, CancellationToken ct);
        Task<List<AreaRef>> GetAssignedAreasAsync(string schoolId, string driverId, CancellationToken ct);

        Task<TbDriverDutySession?> GetOpenSessionAsync(string schoolId, string driverId, CancellationToken ct);
        Task<TbDriverLiveLocation?> GetLiveAsync(string driverId, CancellationToken ct);
        Task<DutySessionContext?> GetSessionByTokenHashAsync(string tokenHash, CancellationToken ct);

        /// <summary>
        /// One transaction: closes <paramref name="staleSessionId"/> (AUTO_TIMEOUT) if given, deletes the driver's live row, inserts the new open session.
        /// Returns null when the database refuses because the driver already has an open session (two starts at once).
        /// </summary>
        Task<TbDriverDutySession?> StartSessionAsync(string schoolId, string driverId, long vehicleId, string tokenHash, long? staleSessionId, DateTime now, CancellationToken ct);
        /// <summary>One transaction: swaps the token hash of an OPEN session and deletes the live row. False when the session is no longer open.</summary>
        Task<bool> ResumeSessionAsync(long sessionId, string newTokenHash, CancellationToken ct);
        /// <summary>Closes an open session and deletes its live row. True when THIS call ended it (false = it was already closed).</summary>
        Task<bool> EndSessionAsync(long sessionId, string reason, DateTime now, CancellationToken ct);

        /// <summary>
        /// INSERT ... ON CONFLICT (driver_id) DO UPDATE ... WHERE the stored row is of the same session and OLDER than this fix.
        /// Returns the rows written: 0 means the fix was out of order (an equal or newer one is already stored).
        /// </summary>
        Task<int> UpsertLocationAsync(string schoolId, string driverId, long sessionId, long vehicleId, LocationFix fix, CancellationToken ct);

        /// <summary>The open sessions of the school (drivers that are Active), optionally for one area or vehicle, with their latest fix.</summary>
        Task<List<LiveRow>> ListOpenAsync(string schoolId, long? areaId, long? vehicleId, int activeStatusId, CancellationToken ct);
        Task<Dictionary<string, List<string>>> GetAreaNamesAsync(string schoolId, IReadOnlyCollection<string> driverIds, CancellationToken ct);
    }
}
