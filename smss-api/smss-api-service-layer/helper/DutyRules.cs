using System.Security.Cryptography;
using System.Text;

namespace smss_api_service_layer.helper
{
    /// <summary>The thresholds of the live location feature (appsettings section "TransportLive"). The code defaults are the production values.</summary>
    public class TransportLiveOptions
    {
        public const string Section = "TransportLive";

        /// <summary>ON by default (production). Development turns it OFF so the emulator's route playback (which sets the mocked flag) works.</summary>
        public bool RejectMockLocations { get; set; } = true;
        /// <summary>The server refuses (429) a second fix sooner than this after the previous one.</summary>
        public int MinIntervalSeconds { get; set; } = 20;
        /// <summary>The interval the app is told to use.</summary>
        public int IntervalSeconds { get; set; } = 60;
        public int LiveSeconds { get; set; } = 180;
        public int StaleSeconds { get; set; } = 600;
        /// <summary>An OFFLINE driver disappears from the live board after this long (a read-time filter; nothing is deleted).</summary>
        public int HideOfflineAfterMinutes { get; set; } = 60;
        public int MaxSessionHours { get; set; } = 16;
        /// <summary>An open session whose last activity is within this many seconds is RESUMED by a new start; older ones are closed (AUTO_TIMEOUT).</summary>
        public int ResumeWithinSeconds { get; set; } = 600;
        public int MaxAccuracyMeters { get; set; } = 200;
        /// <summary>A fix older than this is dropped (STALE_FIX).</summary>
        public int MaxFixAgeSeconds { get; set; } = 600;
        /// <summary>A fix timestamped further ahead than this is dropped (FUTURE_FIX).</summary>
        public int MaxFutureSeconds { get; set; } = 120;
    }

    public static class DutyStatuses
    {
        public const string Live = "LIVE", Stale = "STALE", Offline = "OFFLINE";
    }

    public static class DutyEndReasons
    {
        public const string DriverEnded = "DRIVER_ENDED", AutoTimeout = "AUTO_TIMEOUT", DriverDeactivated = "DRIVER_DEACTIVATED", SessionExpired = "SESSION_EXPIRED";
    }

    public static class DutyCodes
    {
        public const string UnknownToken = "UNKNOWN_TOKEN", SessionEnded = "SESSION_ENDED", SessionExpired = "SESSION_EXPIRED", DriverDeactivated = "DRIVER_DEACTIVATED", TooSoon = "TOO_SOON", Invalid = "INVALID";
    }

    public static class DutyRules
    {
        /// <summary>LIVE up to LiveSeconds, STALE up to StaleSeconds, OFFLINE beyond that or when there has been no fix. Always from the SERVER time of the last fix.</summary>
        public static string Status(DateTime? receivedAtUtc, DateTime nowUtc, TransportLiveOptions o)
        {
            if (receivedAtUtc == null) return DutyStatuses.Offline;
            var age = (nowUtc - receivedAtUtc.Value).TotalSeconds;
            return age <= o.LiveSeconds ? DutyStatuses.Live : age <= o.StaleSeconds ? DutyStatuses.Stale : DutyStatuses.Offline;
        }

        public static int AgeSeconds(DateTime receivedAtUtc, DateTime nowUtc) => (int)Math.Max(0, Math.Round((nowUtc - receivedAtUtc).TotalSeconds));

        /// <summary>A fresh random 256-bit token, URL-safe (43 characters).</summary>
        public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>Lower-case hex SHA-256: what is stored, and what the rate limiter uses as a key. The raw token is never stored or logged.</summary>
        public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

        public const int MaxTokenLength = 200;
    }
}
