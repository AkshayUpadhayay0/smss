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
