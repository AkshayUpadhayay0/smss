namespace smss_api_service_layer.helper
{
    /// <summary>
    /// school_id format: {PREFIX}{yyMMdd}{seq:D3}, e.g. FIRS261013001.
    /// PREFIX is up to 4 letters/digits from the school's short name (or its name), SCHL when there are none.
    /// </summary>
    public static class SchoolIdHelper
    {
        public const string FallbackPrefix = "SCHL";
        private const int PrefixLength = 4;

        /// <summary>
        /// Uses the short name when it is given, else the school name: upper-cased, everything except A-Z / 0-9 removed,
        /// then the first 4 characters. If the short name leaves nothing (e.g. it is all punctuation or non-Latin script)
        /// the school name is tried next, and only when that also leaves nothing is "SCHL" used.
        /// </summary>
        public static string GenerateSchoolIdPrefix(string? shortName, string? schoolName)
        {
            var fromShort = Clean(shortName);
            if (fromShort.Length > 0) return Take(fromShort);

            var fromName = Clean(schoolName);
            if (fromName.Length > 0) return Take(fromName);

            return FallbackPrefix;
        }

        /// <summary>{prefix}{yyMMdd}{seq:D3}. The date is the UTC date, the same one the day's school count is taken for.</summary>
        public static string Build(string prefix, DateTime utcNow, long sequence) =>
            $"{prefix}{utcNow:yyMMdd}{sequence:D3}";

        /// <summary>{prefix}{seq:D3}, e.g. DPS001. varchar(10) in the DB: 4 prefix characters + the sequence.</summary>
        public static string BuildShortCode(string prefix, long sequence) => $"{prefix}{sequence:D3}";

        private static string Take(string cleaned) => cleaned.Length <= PrefixLength ? cleaned : cleaned[..PrefixLength];

        // ASCII letters and digits only, upper-cased (accented / non-Latin characters are dropped, not transliterated)
        private static string Clean(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var chars = new List<char>(value.Length);
            foreach (var c in value)
            {
                if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') chars.Add(c);
                else if (c is >= 'a' and <= 'z') chars.Add((char)(c - 32));
            }
            return new string(chars.ToArray());
        }
    }
}
