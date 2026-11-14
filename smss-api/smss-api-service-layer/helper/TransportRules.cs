using System.Text.RegularExpressions;

namespace smss_api_service_layer.helper
{
    /// <summary>The pure rules of the Transport module: normalisation and the read-time expiry badges. Nothing here touches the database.</summary>
    public static class TransportRules
    {
        public const int ExpiringWithinDays = 30;

        /// <summary>lut_vehicle_ownership.ownership_code of a rented vehicle (the only one that may carry owner / vendor details).</summary>
        public const string RentedOwnershipCode = "RENTED";

        public const string Expired = "EXPIRED";
        public const string Expiring = "EXPIRING";
        public const string Ok = "OK";
        public const string NotSet = "NOT_SET";

        private static readonly Regex PlatePattern = new(@"^[A-Z0-9-]{4,20}$", RegexOptions.Compiled);
        private static readonly Regex DlPattern = new(@"^[A-Z0-9/-]{5,30}$", RegexOptions.Compiled);
        private static readonly Regex ContactPattern = new(@"^[0-9+()\- ]{6,20}$", RegexOptions.Compiled);

        /// <summary>Trim, remove inner spaces, upper-case: "up80 ab 1234" and "UP80AB1234" are the same plate.</summary>
        public static string NormalizePlate(string? plate) => string.Concat((plate ?? string.Empty).Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        public static bool IsValidPlate(string normalized) => PlatePattern.IsMatch(normalized);

        /// <summary>Same normalisation for a driving licence number.</summary>
        public static string NormalizeDl(string? dl) => string.Concat((dl ?? string.Empty).Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        public static bool IsValidDl(string normalized) => DlPattern.IsMatch(normalized);

        public static bool IsValidContact(string contact) => ContactPattern.IsMatch(contact);

        // ---------------------------------------------------------------- approximate pickup / drop times ("HH:mm", school local time, no zone)
        private static readonly Regex TimePattern = new(@"^([01][0-9]|2[0-3]):[0-5][0-9]$", RegexOptions.Compiled);

        /// <summary>Blank is valid and means "not set" (null). Otherwise exactly two-digit "HH:mm", 00:00 to 23:59. Seconds and other spellings are refused.</summary>
        public static bool TryParseTime(string? text, out TimeOnly? time)
        {
            time = null;
            var t = text?.Trim();
            if (string.IsNullOrEmpty(t)) return true;
            if (!TimePattern.IsMatch(t)) return false;
            time = new TimeOnly(int.Parse(t[..2]), int.Parse(t[3..]));
            return true;
        }

        public static string? FormatTime(TimeOnly? time) => time?.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// The badge for an expiry date, computed from today's date in the school's time zone and never stored: NOT_SET when there is no date,
        /// EXPIRED when it is before today, EXPIRING when it falls within the next 30 days (today included), otherwise OK.
        /// <paramref name="daysLeft"/> is null for NOT_SET and negative for EXPIRED.
        /// </summary>
        public static string Expiry(DateOnly? date, DateOnly today, out int? daysLeft)
        {
            if (date == null) { daysLeft = null; return NotSet; }
            daysLeft = date.Value.DayNumber - today.DayNumber;
            if (daysLeft < 0) return Expired;
            return daysLeft <= ExpiringWithinDays ? Expiring : Ok;
        }
    }
}
