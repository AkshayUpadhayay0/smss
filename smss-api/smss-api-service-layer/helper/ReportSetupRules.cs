using smss_api_service_layer.dto;

namespace smss_api_service_layer.helper
{
    /// <summary>The fixed vocabularies of the report-card setup (the same strings as the CHECK constraints in Docs/smss-report-card-stage1.sql).</summary>
    public static class ReportSetup
    {
        public const string Marks = "MARKS", Grades = "GRADES", Grade = "GRADE";
        public const string PercentBands = "PERCENT_BANDS", Rating = "RATING";
        public static readonly string[] YearCombines = { "SUM_OF_TERMS", "WEIGHTED" };
        public static readonly string[] RankScopes = { "SECTION", "CLASS" };
        public static readonly string[] AttendanceSources = { "AUTO", "MANUAL" };
        public static readonly string[] Roundings = { "ROUND_HALF_UP", "TRUNCATE" };
        public static readonly string[] Categories = { "CO_SCHOLASTIC", "DISCIPLINE", "OTHER" };
        public const int MaxTerms = 6, MaxExamsPerTerm = 10, MaxSkills = 10, MaxBands = 15;
    }

    /// <summary>The report-card templates: code constants (no table), each with the evaluation modes it can draw. Stage 3 builds term-spread's renderer, Stage 4 the rest.</summary>
    public static class ReportCardTemplates
    {
        public sealed record Info(string Key, string Name, string Description, string[] Modes, string Orientation, bool Implemented);

        public static readonly IReadOnlyList<Info> All = new Info[]
        {
            new("classic-card", "Classic Card", "Compact portrait card: papers as columns, result block and conduct lines.", new[] { "MARKS" }, "Portrait", true),
            new("term-spread", "Term Spread", "Landscape two-panel Term I / Term II marks table with a grand total, co-scholastic and discipline grades.", new[] { "MARKS" }, "Landscape", true),
            new("skill-grid", "Skill Grid", "Landscape exam-by-skill grade matrix for the early years, with remarks, rank and promotion.", new[] { "GRADES" }, "Landscape", false),
            new("board-style", "Board Style", "Portrait scholastic plus co-scholastic layout for the upper classes.", new[] { "MARKS", "GRADES" }, "Portrait", false),
            new("playful-report", "Playful Report", "Descriptive learning areas with a grade, things to improve and comments.", new[] { "GRADES" }, "Portrait", false),
            new("minimal-bw", "Minimal (ink-saving)", "Single colour, photocopy-friendly.", new[] { "MARKS" }, "Portrait", true),
        };

        public static Info? Find(string? key) => All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));
        public static ReportTemplateDto ToDto(Info t) => new() { Key = t.Key, Name = t.Name, Description = t.Description, Modes = t.Modes.ToList(), Orientation = t.Orientation, Implemented = t.Implemented };
    }

    /// <summary>Validation of a grade scale's bands (rules across rows that a CHECK cannot express).</summary>
    public static class GradeScaleRules
    {
        public sealed record Clean(string Code, string Label, decimal? Min, decimal? Point, bool IsFail, string? Description);

        /// <summary>
        /// PERCENT_BANDS: every band has a lower edge, the lowest is 0, edges are all different. Because only the LOWER edge is stored (inclusive; the upper edge is the next
        /// band's lower edge), no percentage, however many decimals, can fall in a gap. RATING: no percentages; the order given is the order of the scale.
        /// Returns the bands in storage order (percent scales: lowest first) or an error message.
        /// </summary>
        public static string? Validate(string? kind, IReadOnlyList<BandRequest>? bands, out List<Clean> clean)
        {
            clean = new();
            if (kind != ReportSetup.PercentBands && kind != ReportSetup.Rating) return "kind must be PERCENT_BANDS or RATING.";
            if (bands == null || bands.Count == 0) return "A grade scale needs at least one grade.";
            if (bands.Count > ReportSetup.MaxBands) return $"A grade scale can have at most {ReportSetup.MaxBands} grades.";
            foreach (var b in bands)
            {
                var code = (b.GradeCode ?? "").Trim(); var label = (b.Label ?? "").Trim();
                if (code.Length == 0 || code.Length > 10) return "Every grade needs a code of 1 to 10 characters.";
                if (label.Length == 0 || label.Length > 60) return $"Grade {code} needs a label of up to 60 characters.";
                if (kind == ReportSetup.PercentBands && b.MinPercent == null) return $"Grade {code} needs its lower percentage.";
                if (kind == ReportSetup.Rating && b.MinPercent != null) return $"Grade {code}: a rating scale has no percentages.";
                if (b.MinPercent is { } m && (m < 0 || m > 100 || decimal.Round(m, 2) != m)) return $"Grade {code}: the lower percentage must be between 0 and 100 with at most two decimals.";
                clean.Add(new Clean(code, label, b.MinPercent, b.GradePoint, b.IsFail, string.IsNullOrWhiteSpace(b.Description) ? null : b.Description.Trim()));
            }
            if (clean.Select(c => c.Code.ToUpperInvariant()).Distinct().Count() != clean.Count) return "Grade codes must be different from each other.";
            if (kind == ReportSetup.PercentBands)
            {
                clean = clean.OrderBy(c => c.Min).ToList();
                if (clean[0].Min != 0) return "The lowest grade must start at 0%, so every percentage has a grade.";
                for (var i = 1; i < clean.Count; i++)
                    if (clean[i].Min == clean[i - 1].Min) return $"Grades {clean[i - 1].Code} and {clean[i].Code} start at the same percentage.";
            }
            return null;
        }

        /// <summary>The grade of a percentage: the band with the greatest lower edge that is not above it.</summary>
        public static T? Lookup<T>(IEnumerable<T> bandsLowestFirst, Func<T, decimal?> min, decimal percent) where T : class =>
            bandsLowestFirst.Where(b => min(b) != null && min(b)! <= percent).OrderByDescending(b => min(b)).FirstOrDefault();
    }
}
