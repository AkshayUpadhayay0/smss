namespace smss_api_service_layer.helper
{
    /// <summary>
    /// Report-card presets: CODE CONSTANTS (no seed rows) that are COPIED into a school's own rows on request, so a school edits its own copy and a preset can improve
    /// without touching anyone's data. Three scheme presets cover the three real card shapes; four grade-scale presets and a starter set of activity areas support them.
    /// </summary>
    public static class ReportPresets
    {
        // ------------------------------------------------------------------ grade scales
        public sealed record BandSeed(string Code, string Label, decimal? Min, bool IsFail = false, string? Description = null);
        public sealed record ScalePreset(string Code, string Name, string Kind, string Note, BandSeed[] Bands);

        public static readonly IReadOnlyList<ScalePreset> Scales = new ScalePreset[]
        {
            new("cbse-ix-absolute", "CBSE Class IX-style (absolute) A1-E", ReportSetup.PercentBands,
                "Source: CBSE Secondary Curriculum 2025-26 (absolute grading for Class IX): A1 91-100, A2 81-90, B1 71-80, B2 61-70, C1 51-60, C2 41-50, D 33-40, E 32 and below (fail). " +
                "An EDITABLE DEFAULT, not an official certificate scale: Class X board grades are awarded by the board, not computed by the school. Check the current CBSE circular for your session.",
                new BandSeed[]
                {
                    new("A1", "A1", 91), new("A2", "A2", 81), new("B1", "B1", 71), new("B2", "B2", 61), new("C1", "C1", 51), new("C2", "C2", 41), new("D", "D", 33), new("E", "E (fail)", 0, true),
                }),
            new("abcde-standard", "Standard A-E (percentage)", ReportSetup.PercentBands,
                "A simple five-grade scale for school-set marks: A 80 and above, B 60-79, C 45-59, D 33-44, E below 33 (fail). An editable default.",
                new BandSeed[] { new("A", "Excellent", 80), new("B", "Very Good", 60), new("C", "Good", 45), new("D", "Satisfactory", 33), new("E", "Needs improvement", 0, true) }),
            new("abc-skills", "Skill rating A-B-C", ReportSetup.Rating,
                "For skills and co-scholastic areas: A Outstanding, B Very Good, C Fair. An editable default.",
                new BandSeed[] { new("A", "Outstanding", null), new("B", "Very Good", null), new("C", "Fair", null) }),
            new("rating-a-to-e", "Descriptive rating A-E", ReportSetup.Rating,
                "For descriptive report cards: A Excellent, B Very Good, C Good, D Satisfactory, E Needs improvement. An editable default.",
                new BandSeed[] { new("A", "Excellent", null), new("B", "Very Good", null), new("C", "Good", null), new("D", "Satisfactory", null), new("E", "Needs improvement", null) }),
        };

        public static ScalePreset? FindScale(string? code) => Scales.FirstOrDefault(s => string.Equals(s.Code, code, StringComparison.Ordinal));

        // ------------------------------------------------------------------ schemes
        public sealed record ExamSeed(string Name, decimal Max, decimal Weight);
        public sealed record TermSeed(string Name, ExamSeed[] Exams);
        public sealed record SchemePreset(string Code, string Name, string Description, string Mode, string TemplateKey, string ScalePresetCode, bool ShowRank, string EntryMode,
            TermSeed[] Terms, string[] Skills);

        public static readonly IReadOnlyList<SchemePreset> Schemes = new SchemePreset[]
        {
            new("term-wise-100", "Term-wise: PT, Half-Yearly and Annual (out of 100)",
                "Two terms. Each term: two periodic tests of 10 and a 80-mark main exam, adding up to 100. The year is the sum of both terms. The usual primary-school marks card.",
                ReportSetup.Marks, "term-spread", "abcde-standard", true, "MARKS",
                new TermSeed[]
                {
                    new("Term I", new ExamSeed[] { new("PT 1", 10, 10), new("PT 2", 10, 10), new("Half Yearly", 80, 80) }),
                    new("Term II", new ExamSeed[] { new("PT 3", 10, 10), new("PT 4", 10, 10), new("Annual", 80, 80) }),
                }, Array.Empty<string>()),
            new("two-paper-simple", "Two papers, one result (out of 100)",
                "One term with a 1st Paper and a 2nd Paper of 50 each, giving a total, a result and a position. The simple card.",
                ReportSetup.Marks, "classic-card", "abcde-standard", true, "MARKS",
                new TermSeed[] { new("Annual", new ExamSeed[] { new("1st Paper", 50, 50), new("2nd Paper", 50, 50) }) }, Array.Empty<string>()),
            new("pre-primary-skill-grid", "Pre-primary skill grid (grades only)",
                "No marks. Each subject is graded on skills (Reading, Writing, Oral) in PT 1, PT 2, Half Yearly, PT 3, PT 4 and Final, in two terms. Rank is written by hand.",
                ReportSetup.Grades, "skill-grid", "abc-skills", false, "GRADE",
                new TermSeed[]
                {
                    new("Term I", new ExamSeed[] { new("PT 1", 10, 10), new("PT 2", 10, 10), new("Half Yearly", 10, 10) }),
                    new("Term II", new ExamSeed[] { new("PT 3", 10, 10), new("PT 4", 10, 10), new("Final", 10, 10) }),
                }, new[] { "Reading", "Writing", "Oral" }),
        };

        public static SchemePreset? FindScheme(string? code) => Schemes.FirstOrDefault(s => string.Equals(s.Code, code, StringComparison.Ordinal));

        // ------------------------------------------------------------------ activity areas
        public sealed record AreaSeed(string Category, string Name);

        /// <summary>Starter co-scholastic and discipline areas, all rated on the "abc-skills" scale.</summary>
        public static readonly IReadOnlyList<AreaSeed> StarterAreas = new AreaSeed[]
        {
            new("DISCIPLINE", "Discipline"), new("DISCIPLINE", "Cleanliness"), new("DISCIPLINE", "Punctuality"),
            new("CO_SCHOLASTIC", "Art & Craft"), new("CO_SCHOLASTIC", "Music"), new("CO_SCHOLASTIC", "Physical Education"), new("CO_SCHOLASTIC", "Computer Skills"),
        };
        public const string StarterScalePresetCode = "abc-skills";

        // ------------------------------------------------------------------ term dates
        /// <summary>Splits an academic year into <paramref name="count"/> consecutive terms of (nearly) equal length.</summary>
        public static List<(DateOnly Start, DateOnly End)> SplitYear(DateOnly start, DateOnly end, int count)
        {
            var days = end.DayNumber - start.DayNumber + 1;
            var list = new List<(DateOnly, DateOnly)>();
            for (var i = 0; i < count; i++)
            {
                var s = start.AddDays(days * i / count);
                var e = i == count - 1 ? end : start.AddDays(days * (i + 1) / count - 1);
                list.Add((s, e));
            }
            return list;
        }
    }
}
