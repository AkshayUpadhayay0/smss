using System.Text.RegularExpressions;

namespace smss_api_db_layer.helper
{
    /// <summary>
    /// The normalised form of a roll number: spaces trimmed, case ignored, and a purely numeric value without leading zeros ("01" = "1" = " 1 ").
    /// It must stay identical to the database function public.fn_roll_key (Docs/smss-yc-1a-RUN-ME.sql), which the unique index uses.
    /// </summary>
    public static class RollKey
    {
        private static readonly Regex Digits = new("^[0-9]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>The key, or null for a blank roll number (blank rolls are never compared).</summary>
        public static string? Of(string? roll)
        {
            var t = roll?.Trim();
            if (string.IsNullOrEmpty(t)) return null;
            if (Digits.IsMatch(t)) { var s = t.TrimStart('0'); return s.Length == 0 ? "0" : s; }
            return t.ToLowerInvariant();
        }

        public static bool Same(string? a, string? b) => Of(a) is { } x && Of(b) is { } y && x == y;
    }
}
