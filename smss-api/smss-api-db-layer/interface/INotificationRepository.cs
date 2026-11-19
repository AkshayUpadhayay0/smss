using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record EligibleStudent(string StudentId, long UserId);
    public record ActiveDevice(long UserId, long DeviceId, string Token);

    /// <summary>
    /// Push notifications: devices, per-school settings and the send log. Every school-owned read takes the school id, so one school's job or admin
    /// can never see another school's students, tokens or log.
    /// </summary>
    public interface INotificationRepository
    {
        // devices
        /// <summary>Inserts the token, or re-assigns an existing one to this user / school (one token = one user at a time) and re-activates it.</summary>
        Task UpsertDeviceAsync(string schoolId, long userId, string token, string platform, string? label, string? appVersion, CancellationToken ct);
        /// <summary>Deactivates the token ONLY when it is owned by this user. Returns the rows changed (0 for a stranger's or unknown token).</summary>
        Task<int> DeactivateOwnDeviceAsync(long userId, string token, CancellationToken ct);
        /// <summary>The job's own clean-up of tokens FCM reported dead.</summary>
        Task<int> DeactivateTokensAsync(IReadOnlyCollection<string> tokens, CancellationToken ct);
        Task<List<ActiveDevice>> GetActiveDevicesAsync(string schoolId, IReadOnlyCollection<long> userIds, CancellationToken ct);

        // settings
        Task<TbSchoolNotificationSettings?> GetSettingsAsync(string schoolId, CancellationToken ct);
        Task<TbSchoolNotificationSettings?> GetSettingsForUpdateAsync(string schoolId, CancellationToken ct);
        void AddSettings(TbSchoolNotificationSettings row);
        /// <summary>Settings of every school that switched fee reminders on and whose school is Active.</summary>
        Task<List<TbSchoolNotificationSettings>> GetEnabledSettingsAsync(CancellationToken ct);

        // the daily job
        /// <summary>Session-level advisory lock for this school's reminder run. False when another run (another API instance) holds it.</summary>
        Task<bool> TryLockSchoolRunAsync(string schoolId, CancellationToken ct);
        /// <summary>Releases the lock on the SAME connection that took it, then gives the connection back. True when this session really held the lock. Never throws: if the lock cannot be confirmed released, the connection is discarded from the pool instead of being returned still locked.</summary>
        Task<bool> UnlockSchoolRunAsync(string schoolId);
        Task<long?> GetCurrentAcademicYearIdAsync(string schoolId, CancellationToken ct);
        /// <summary>Active students enrolled (Active) in the year, each with an Active Student login. School id on every table.</summary>
        Task<List<EligibleStudent>> GetEligibleStudentsAsync(string schoolId, long academicYearId, string studentUserType, CancellationToken ct);
        /// <summary>One PENDING row per student for the day; a row that already exists (unique student + type + day) is left alone. Returns the rows really inserted.</summary>
        Task<int> CreateLogRowsAsync(string schoolId, DateOnly date, string type, string title, string body, IReadOnlyCollection<EligibleStudent> students, CancellationToken ct);
        /// <summary>The day's rows still to send: PENDING, plus FAILED ones under the attempt cap, plus (only when <paramref name="includeDryRun"/>, i.e. a REAL run) the day's DRY_RUN rows, which were validated but never delivered. Tracked, for updating.</summary>
        Task<List<TbPushNotificationLog>> GetOpenLogRowsAsync(string schoolId, DateOnly date, string type, short maxAttempts, bool includeDryRun, CancellationToken ct);

        // admin reads
        Task<bool> StudentBelongsToSchoolAsync(string schoolId, string studentId, CancellationToken ct);
        Task<List<TbPushNotificationLog>> GetLogAsync(string schoolId, string? studentId, DateOnly from, DateOnly to, int take, CancellationToken ct);

        Task<int> SaveChangesAsync(CancellationToken ct);
        void ClearTracking();
    }
}
