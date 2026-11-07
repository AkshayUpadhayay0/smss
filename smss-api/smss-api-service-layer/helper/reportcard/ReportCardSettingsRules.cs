using smss_api_service_layer.dto;
using smss_api_service_layer.helper.idcard;

namespace smss_api_service_layer.helper.reportcard
{
    public sealed record ReportCardSettingsClean(string PrimaryColor, string SecondaryColor, string? HeaderLine, string? RecognitionText, string? UdiseCode, string? FooterText, string? RulesText,
        bool ShowCover, bool ShowRules, bool ShowAttendance, bool ShowGradeLegend, bool ShowPhoto = false);

    /// <summary>The one place that decides whether report card settings are acceptable (used by Save and by Preview).</summary>
    public static class ReportCardSettingsRules
    {
        public const string DefaultPrimary = "#1F3A5F", DefaultSecondary = "#C8963E";
        public const int MaxHeaderLine = 150, MaxRecognition = 150, MaxUdise = 20, MaxFooter = 200, MaxRules = 1500, MaxRulesLines = 30;

        public static (ReportCardSettingsClean? Clean, string? Error) Validate(ReportCardSettingsRequest r)
        {
            var primary = Blank(r.PrimaryColor) ?? DefaultPrimary; var secondary = Blank(r.SecondaryColor) ?? DefaultSecondary;
            if (!IdCardColors.IsHex(primary)) return (null, "Primary colour must be a hex colour like #1F3A5F.");
            if (!IdCardColors.IsHex(secondary)) return (null, "Secondary colour must be a hex colour like #C8963E.");
            var header = Line(r.HeaderLine); if (header != null && header.Length > MaxHeaderLine) return (null, $"The header line can be at most {MaxHeaderLine} characters.");
            var rec = Line(r.RecognitionText); if (rec != null && rec.Length > MaxRecognition) return (null, $"The recognition text can be at most {MaxRecognition} characters.");
            var udise = Line(r.UdiseCode); if (udise != null && (udise.Length > MaxUdise || udise.Any(char.IsWhiteSpace) && udise.Length > MaxUdise)) return (null, $"The UDISE code can be at most {MaxUdise} characters.");
            var footer = Line(r.FooterText); if (footer != null && footer.Length > MaxFooter) return (null, $"The footer text can be at most {MaxFooter} characters.");
            string? rules = null;
            if (!string.IsNullOrWhiteSpace(r.RulesText))
            {
                var lines = r.RulesText.Replace("\r", "").Split('\n').Select(l => StripControl(l).TrimEnd()).ToList();
                while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
                while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
                rules = string.Join('\n', lines);
                if (rules.Length > MaxRules) return (null, $"The rules text can be at most {MaxRules} characters (it is {rules.Length}).");
                if (lines.Count > MaxRulesLines) return (null, $"The rules text can have at most {MaxRulesLines} lines.");
                if (rules.Length == 0) rules = null;
            }
            return (new ReportCardSettingsClean(primary.ToUpperInvariant(), secondary.ToUpperInvariant(), header, rec, udise, footer, rules, r.ShowCover, r.ShowRules, r.ShowAttendance, r.ShowGradeLegend, r.ShowPhoto), null);
        }

        private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        private static string? Line(string? s) => Blank(s) is { } v ? StripControl(v) : null;
        private static string StripControl(string s) => new(s.Where(c => !char.IsControl(c) || c == '\t').ToArray());
    }
}
