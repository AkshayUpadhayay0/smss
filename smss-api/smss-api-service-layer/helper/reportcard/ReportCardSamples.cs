using System.Globalization;

namespace smss_api_service_layer.helper.reportcard
{
    /// <summary>What a sample card should look like: used by the settings preview (built-in sample data) and by the template tests (long names, many subjects, one to three terms).</summary>
    public sealed class RcSampleOptions
    {
        public string TemplateKey { get; set; } = "term-spread";
        public int Terms { get; set; } = 2;
        public int Subjects { get; set; } = 8;
        public bool LongNames { get; set; }
        public bool Hindi { get; set; }
        public bool Fail { get; set; }
        public string Primary { get; set; } = "#1F3A5F";
        public string Secondary { get; set; } = "#C8963E";
        public string SchoolName { get; set; } = "Sample Public School";
        public string? HeaderLine { get; set; } = "Affiliated to the State Board of School Education";
        public string? Recognition { get; set; } = "Recognised by the Department of Education";
        public string? Udise { get; set; } = "UDISE 05010000000";
        public string? Footer { get; set; } = "This is a computer generated report card.";
        public string? RulesText { get; set; } = "1. Students must reach school by 8:30 AM.\n2. The school uniform is compulsory on all days.\n3. Parents must sign the diary every week.\n4. Mobile phones are not allowed on the campus.";
        public bool ShowCover { get; set; }
        public bool ShowRules { get; set; }
        public bool ShowAttendance { get; set; } = true;
        public bool ShowGradeLegend { get; set; } = true;
        public bool ShowRank { get; set; } = true;
        public string? SignatureKey { get; set; }
        public string? LogoKey { get; set; }
        public string StudentName { get; set; } = "Aarav Kumar Sharma";
        public int Seed { get; set; } = 7;
    }

    /// <summary>Invented, internally consistent card data (totals add up, grades follow the bands). Never real people.</summary>
    public static class ReportCardSamples
    {
        private static readonly string[] SubjectNames = { "English", "Hindi", "Mathematics", "Science", "Social Studies", "Computer Science", "Sanskrit", "General Knowledge", "Art & Craft", "Physical Education", "Environmental Studies", "Music", "Moral Science", "Drawing", "Urdu", "Home Science" };
        private static readonly string[] LongSubjectNames = { "English Language and Literature (Communicative)", "Hindi Course A: Vyakaran aur Sahitya", "Mathematics (Standard) including Mental Arithmetic", "Science: Physics, Chemistry and Biology", "Social Science: History, Geography, Civics and Economics",
            "Computer Applications and Information Technology", "Sanskrit (Shastri Level Foundation Course)", "General Knowledge and Current Affairs", "Art, Craft and Creative Expression Workshop", "Physical Education, Yoga and Sports", "Environmental Studies and Sustainability", "Vocal and Instrumental Music",
            "Moral Science and Value Education", "Drawing and Painting Techniques", "Urdu Language and Literature", "Home Science and Life Skills" };
        private static readonly string[] HindiSubjects = { "अंग्रेज़ी", "हिंदी", "गणित", "विज्ञान", "सामाजिक विज्ञान", "कंप्यूटर विज्ञान", "संस्कृत", "सामान्य ज्ञान", "कला और शिल्प", "शारीरिक शिक्षा", "पर्यावरण अध्ययन", "संगीत", "नैतिक शिक्षा", "चित्रकला", "उर्दू", "गृह विज्ञान" };
        private static readonly string[] Remarks = { "Writes neatly; practise tables", "Very good effort", "Needs to read aloud daily", "Excellent understanding", "Careless mistakes; revise the work", "Participates well" };
        private static readonly string[] HindiRemarks = { "बहुत अच्छा प्रयास", "लिखावट में सुधार करें", "उत्कृष्ट प्रदर्शन", "रोज़ अभ्यास करें" };

        public static List<RcBand> DefaultLegend() => new()
        {
            new() { Code = "A", Label = "Outstanding", Min = 80m }, new() { Code = "B", Label = "Very Good", Min = 60m }, new() { Code = "C", Label = "Good", Min = 45m },
            new() { Code = "D", Label = "Fair", Min = 33m }, new() { Code = "E", Label = "Needs improvement", Min = 0m },
        };

        private static string GradeFor(decimal pct) => pct >= 80 ? "A" : pct >= 60 ? "B" : pct >= 45 ? "C" : pct >= 33 ? "D" : "E";

        public static ReportCardModel Build(RcSampleOptions o)
        {
            var rnd = new Random(o.Seed);
            var terms = new List<RcTerm>();
            var termNames = new[] { "Term I", "Term II", "Term III" };
            var start = new DateTime(2026, 4, 1);
            for (var t = 0; t < o.Terms; t++)
            {
                var exams = o.Terms == 3
                    ? new[] { ("PT 1", 10m), ("Half Yearly", 40m) }
                    : t == 0 ? new[] { ("PT 1", 10m), ("PT 2", 10m), ("Half Yearly", 80m) } : new[] { ("PT 3", 10m), ("PT 4", 10m), ("Annual", 80m) };
                terms.Add(new RcTerm
                {
                    TermId = 100 + t, Name = termNames[t], Start = start.AddMonths(t * (12 / o.Terms)).ToString("dd MMM yyyy", CultureInfo.InvariantCulture), End = start.AddMonths((t + 1) * (12 / o.Terms)).AddDays(-1).ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
                    Exams = exams.Select((e, i) => new RcExam { ExamId = 1000 + t * 10 + i, Name = e.Item1, Max = e.Item2, Weight = e.Item2 }).ToList()
                });
            }

            var legend = DefaultLegend();
            var subjects = new List<RcSubject>();
            var termObt = new decimal[o.Terms]; var termOut = new decimal[o.Terms];
            for (var j = 0; j < o.Subjects; j++)
            {
                var name = o.Hindi ? HindiSubjects[j % HindiSubjects.Length] : (o.LongNames ? LongSubjectNames : SubjectNames)[j % SubjectNames.Length];
                var s = new RcSubject { Name = name };
                decimal yo = 0, yout = 0;
                for (var t = 0; t < o.Terms; t++)
                {
                    var cells = new List<RcCell>(); decimal ob = 0, outOf = 0;
                    foreach (var e in terms[t].Exams)
                    {
                        var m = Math.Round(e.Max!.Value * (decimal)(0.45 + rnd.NextDouble() * 0.5) * (o.Fail && j == 0 ? 0.5m : 1m), 1);
                        cells.Add(new RcCell { ExamId = e.ExamId, State = "ENTERED", Marks = m }); ob += m; outOf += e.Weight!.Value;
                    }
                    var pct = Math.Round(ob / outOf * 100, 2);
                    var remark = o.Hindi ? HindiRemarks[(j + t) % HindiRemarks.Length] : Remarks[(j + t) % Remarks.Length];
                    s.Terms.Add(new RcSubjectTerm { TermId = terms[t].TermId, Cells = cells, Obtained = ob, OutOf = outOf, Percent = pct, Grade = GradeFor(pct), Remark = remark, Failed = pct < 33 });
                    termObt[t] += ob; termOut[t] += outOf; yo += ob; yout += outOf;
                }
                s.YearObtained = yo; s.YearOutOf = yout; s.YearPercent = Math.Round(yo / yout * 100, 2); s.YearGrade = GradeFor(s.YearPercent.Value); s.YearFailed = s.YearPercent < 33;
                subjects.Add(s);
            }

            var summaries = new List<RcTermSummary>();
            for (var t = 0; t < o.Terms; t++)
            {
                var pct = Math.Round(termObt[t] / termOut[t] * 100, 2);
                var failed = subjects.Where(x => x.Terms[t].Failed).Select(x => x.Name).ToList();
                summaries.Add(new RcTermSummary
                {
                    TermId = terms[t].TermId, Obtained = termObt[t], OutOf = termOut[t], Percent = pct, Grade = GradeFor(pct), Result = failed.Count > 0 || pct < 33 ? "FAIL" : "PASS", FailedSubjects = failed,
                    Rank = failed.Count > 0 ? null : 4 + t, RankedCount = 38, WorkingDays = 88 + t * 4, PresentDays = 80 + t * 3, Remark = o.Hindi ? "कक्षा में सक्रिय और अनुशासित छात्र।" : "A sincere and well-behaved student who takes part in every activity.",
                    Conduct = "Very good",
                    Activities = new List<RcActivity>
                    {
                        new() { Category = "DISCIPLINE", Area = o.Hindi ? "समय की पाबंदी" : "Punctuality", Grade = "A", GradeLabel = "Outstanding" },
                        new() { Category = "DISCIPLINE", Area = o.Hindi ? "अनुशासन" : "Discipline", Grade = "B", GradeLabel = "Very Good" },
                        new() { Category = "DISCIPLINE", Area = o.Hindi ? "आचरण" : "Conduct", Grade = "A", GradeLabel = "Outstanding" },
                        new() { Category = "CO_SCHOLASTIC", Area = o.Hindi ? "कला" : "Art & Craft", Grade = "A", GradeLabel = "Outstanding" },
                        new() { Category = "CO_SCHOLASTIC", Area = o.Hindi ? "संगीत" : "Music", Grade = "B", GradeLabel = "Very Good" },
                    }
                });
            }
            var allObt = termObt.Sum(); var allOut = termOut.Sum(); var allPct = Math.Round(allObt / allOut * 100, 2);
            var allFailed = subjects.Where(x => x.YearFailed).Select(x => x.Name).ToList();
            var yearFail = allFailed.Count > 0 || allPct < 33;
            return new ReportCardModel
            {
                TemplateKey = o.TemplateKey, Mode = "MARKS", SessionName = "2026-27", Scope = o.Terms == 1 ? "TERM" : "YEAR", ShowRank = o.ShowRank, Primary = o.Primary, Secondary = o.Secondary,
                Header = new RcHeader { SchoolName = o.SchoolName, AddressLine = "12 Rajpur Road, Near Clock Tower, Dehradun, Uttarakhand - 248001", Phone = "0135-2700000", LogoKey = o.LogoKey, Line = o.HeaderLine, Recognition = o.Recognition, Udise = o.Udise },
                Student = new RcStudent
                {
                    StudentId = "SAMPLE001", Name = o.Hindi ? "आरव कुमार शर्मा" : o.LongNames ? "Venkata Subramaniam Raghunathan Krishnamoorthy Iyer-Narayanaswamy" : o.StudentName, ClassName = "Class 6", SectionName = "A", Roll = "14", AdmissionNo = "ADMNO20260014", Dob = "12-03-2014",
                    Father = o.Hindi ? "राजेश कुमार शर्मा" : o.LongNames ? "Raghunathan Krishnamoorthy Iyer-Narayanaswamy" : "Rajesh Kumar Sharma", Mother = o.Hindi ? "सुनीता देवी" : "Sunita Sharma",
                    Doj = "01 Apr 2022", Address = o.LongNames ? "Flat 402, Building C, Sunrise Residency Apartments, Behind the Old Municipal Corporation Office, Sector 12, Dehradun, Uttarakhand - 248001" : "14 Gandhi Road, Dehradun - 248001", Contact = "9876500014"
                },
                Terms = terms, Subjects = subjects, TermSummaries = summaries,
                Overall = o.Terms == 1
                    ? new RcOverall { Obtained = termObt[0], OutOf = termOut[0], Percent = summaries[0].Percent, Grade = summaries[0].Grade, Result = summaries[0].Result, FailedSubjects = summaries[0].FailedSubjects, Rank = summaries[0].Rank, RankedCount = 38 }
                    : new RcOverall { Obtained = allObt, OutOf = allOut, Percent = allPct, Grade = GradeFor(allPct), Result = yearFail ? "FAIL" : "PASS", FailedSubjects = allFailed, Rank = yearFail ? null : 5, RankedCount = 38 },
                Promotion = new RcPromotion { Decision = yearFail ? "NOT_PROMOTED" : "PROMOTED", PromotedTo = yearFail ? null : "Class 7", NewSessionDate = "01 Apr 2027" },
                Legend = legend, Version = 1, PublishedAt = "07 Nov 2026",
                Footer = o.Footer, RulesText = o.RulesText, SignatureKey = o.SignatureKey, ShowCover = o.ShowCover, ShowRules = o.ShowRules, ShowAttendance = o.ShowAttendance, ShowGradeLegend = o.ShowGradeLegend,
            };
        }
    }
}
