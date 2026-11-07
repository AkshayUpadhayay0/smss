namespace smss_api_service_layer.helper.reportcard
{
    /// <summary>
    /// GRADES-mode sample cards (no marks, no percentage): a skill-grid shape (several exams per term, each subject graded on up to five skills) or a descriptive shape (one assessment per term,
    /// one grade per learning area, with a remark and "things to improve"). Built from the MARKS sample so the school, student and settings parts are identical.
    /// </summary>
    public static class ReportCardGradeSamples
    {
        public static readonly string[] SkillNames = { "Reading", "Writing", "Oral", "Cycle-wise grade", "Internal assessment" };
        private static readonly string[] Improve = { "Hold the pencil correctly", "Practise joining letters", "Read aloud every day", "Count objects up to 50", "Wait for a turn to speak", "Colour inside the lines" };
        private static readonly string[] HindiImprove = { "पेंसिल सही तरीके से पकड़ें", "अक्षर जोड़ने का अभ्यास करें", "रोज़ ज़ोर से पढ़ें", "50 तक गिनती का अभ्यास करें" };
        private static readonly string[] GradeRemarks = { "Enjoys the activities", "Reads with confidence", "Needs a little more practice", "Very good effort", "Takes part happily" };
        private static readonly string[] HindiGradeRemarks = { "गतिविधियों में रुचि लेता है", "आत्मविश्वास से पढ़ता है", "थोड़ा और अभ्यास चाहिए", "बहुत अच्छा प्रयास" };
        private static readonly string[] Ranks = { "1st", "3rd", "5th" };

        public static List<RcBand> RatingLegend() => new() { new() { Code = "A", Label = "Outstanding" }, new() { Code = "B", Label = "Very Good" }, new() { Code = "C", Label = "Fair" } };

        public static ReportCardModel Build(RcSampleOptions o, ReportCardModel marks)
        {
            var rnd = new Random(o.Seed + 31);
            var grades = new[] { "A", "A", "B", "B", "C" };
            string G() => grades[rnd.Next(grades.Length)];
            var skills = SkillNames.Take(Math.Clamp(o.Skills, 0, 5)).ToList();
            var m = marks;
            m.Mode = "GRADES"; m.ShowRank = false; m.Legend = RatingLegend();
            m.Student.ClassName = skills.Count > 0 ? "UKG" : "LKG";
            m.Student.ClassTeacher = o.Hindi ? "श्रीमती सुनीता रावत" : o.LongNames ? "Smt. Venkatalakshmi Subramaniam Raghunathan" : "Mrs. Sunita Rawat";

            var examNames = new[] { new[] { "PT 1", "PT 2", "Half Yearly" }, new[] { "PT 3", "PT 4", "Final" }, new[] { "PT 5", "PT 6", "Annual" } };
            foreach (var (t, ti) in m.Terms.Select((t, i) => (t, i)))
                t.Exams = (skills.Count > 0 ? examNames[ti % 3] : new[] { "Term Assessment" }).Select((n, i) => new RcExam { ExamId = 1000 + ti * 10 + i, Name = n }).ToList();

            foreach (var s in m.Subjects)
            {
                s.GradeOnly = false; s.YearObtained = 0; s.YearOutOf = 0; s.YearPercent = null; s.YearGrade = null; s.YearFailed = false;
                s.Terms = m.Terms.Select((t, ti) =>
                {
                    var cells = t.Exams.Select(e => new RcCell
                    {
                        ExamId = e.ExamId, State = "ENTERED",
                        Grade = skills.Count == 0 ? G() : null,
                        Skills = skills.Select(k => new RcSkillGrade { Name = k, State = "ENTERED", Grade = G() }).ToList()
                    }).ToList();
                    var last = cells[^1];
                    var latest = skills.Count == 0 ? last.Grade : last.Skills.GroupBy(k => k.Grade).OrderByDescending(g => g.Count()).First().Key;
                    var improve = o.Hindi ? HindiImprove : Improve; var remarks = o.Hindi ? HindiGradeRemarks : GradeRemarks;
                    return new RcSubjectTerm
                    {
                        TermId = t.TermId, Cells = cells, Obtained = 0, OutOf = 0, Percent = null, Grade = latest, Failed = false,
                        Remark = remarks[(ti + s.Name.Length) % remarks.Length],
                        Improvement = skills.Count == 0 && latest != "A" ? improve[(ti + s.Name.Length) % improve.Length] : null,
                    };
                }).ToList();
            }

            foreach (var (ts, ti) in m.TermSummaries.Select((x, i) => (x, i)))
            {
                ts.Obtained = 0; ts.OutOf = 0; ts.Percent = null; ts.Grade = null; ts.Result = "PASS"; ts.FailedSubjects = new(); ts.Rank = null; ts.RankedCount = null; ts.RankText = Ranks[ti % Ranks.Length];
                ts.Activities = new List<RcActivity>
                {
                    new() { Category = "DISCIPLINE", Area = o.Hindi ? "अनुशासन" : "Discipline", Grade = "A", GradeLabel = "Outstanding" },
                    new() { Category = "DISCIPLINE", Area = o.Hindi ? "स्वच्छता" : "Cleanliness", Grade = "B", GradeLabel = "Very Good" },
                };
            }
            m.Overall = new RcOverall { Result = "PASS" };
            m.Promotion = new RcPromotion { Decision = "PROMOTED", PromotedTo = skills.Count > 0 ? "Class 1" : "UKG", NewSessionDate = "01 Apr 2027" };
            return m;
        }
    }
}
