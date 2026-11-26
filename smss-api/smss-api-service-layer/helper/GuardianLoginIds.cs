using System.Text.RegularExpressions;

namespace smss_api_service_layer.helper
{
    /// <summary>
    /// A guardian's login id = the school's short code + "GD" + a number (GHS001GD001). It is the ONLY way a parent logs in (no login by WhatsApp number or email). Unique across the platform ignoring case, and never equal
    /// to a student id (student ids are short code + "ST" + number; the allocator also checks every existing login id and student id).
    /// </summary>
    public static class GuardianLoginIds
    {
        private static readonly Regex Pattern = new("^[A-Za-z0-9]+GD[0-9]{3,}$", RegexOptions.Compiled);

        public static string Build(string schoolShortCode, int number) => $"{schoolShortCode.Trim()}GD{number:D3}";
        public static bool LooksLikeGuardianId(string? id) => id != null && Pattern.IsMatch(id);
    }
}
