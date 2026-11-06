using System.Globalization;
using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;

namespace smss_api_service_layer.helper.reportcard
{
    /// <summary>Everything one result run produced for a section: the engine's results, a card model per student, and the facts a publish decision needs.</summary>
    public sealed class ResultSet
    {
        public SchemeGraph Graph { get; init; } = null!;
        public long SchemeId { get; init; }
        public string SchemeName { get; init; } = "";
        public long SectionId { get; init; }
        public long ClassId { get; init; }
        public string ClassName { get; init; } = "";
        public string SectionName { get; init; } = "";
        public string Scope { get; init; } = "YEAR";
        public long? TermId { get; init; }
        public string? TermName { get; init; }
        public string Mode { get; init; } = "MARKS";
        public string TemplateKey { get; init; } = "";
        public bool TemplateAvailable { get; init; }
        public string? TemplateMessage { get; init; }
        public List<ResultStudent> Students { get; init; } = new();                    // the section's students (never the other sections used only for a class-wide rank)
        public Dictionary<string, StudentResult> Results { get; init; } = new();
        public Dictionary<string, ReportCardModel> Models { get; init; } = new();
        public RcConfigSnapshot Config { get; init; } = new();
        /// <summary>Which of the scope's terms are NOT locked for this section (empty = ready to publish).</summary>
        public List<string> UnlockedTerms { get; init; } = new();
        public string AttendanceSource { get; init; } = "AUTO";
        public List<DateOnly> UnmarkedDates { get; init; } = new();

        public string StatusOf(string studentId) => Scope == "TERM" ? Results[studentId].Terms[0].Status : Results[studentId].Year.Status;
    }

    /// <summary>The configuration a publication freezes: scheme parameters, scale bands, rounding, template, school header values and the storage KEY of the logo. Never bytes.</summary>
    public sealed class RcConfigSnapshot
    {
        public int SchemaVersion { get; set; } = 1;
        public string SchemeName { get; set; } = "";
        public string SessionName { get; set; } = "";
        public string Scope { get; set; } = "";
        public string? TermName { get; set; }
        public string EvaluationMode { get; set; } = "";
        public string YearCombine { get; set; } = "";
        public decimal SubjectPassPercent { get; set; }
        public decimal OverallPassPercent { get; set; }
        public string Rounding { get; set; } = "";
        public string RankScope { get; set; } = "";
        public bool ShowRank { get; set; }
        public string AttendanceSource { get; set; } = "";
        public string TemplateKey { get; set; } = "";
        public List<RcBand> Bands { get; set; } = new();
        public RcHeader Header { get; set; } = new();
        /// <summary>Storage key of the principal's signature image. Report-card settings (a later stage) fill it; until then it is null and the signature row is a plain line.</summary>
        public string? SignatureKey { get; set; }
        public string Primary { get; set; } = "";
        public string Secondary { get; set; } = "";
        /// <summary>Students left out of this publication (no complete result), by id and name, so the version history can say who was missing.</summary>
        public List<RcExcluded> Excluded { get; set; } = new();
    }

    public sealed class RcExcluded { public string StudentId { get; set; } = ""; public string Name { get; set; } = ""; public string Roll { get; set; } = ""; }

    /// <summary>
    /// Loads a section's data in a FIXED number of set-based queries (nothing per student), runs the calculation engine and builds each student's ReportCardModel.
    /// Used for the result preview, for draft card previews and for publishing, so all three are guaranteed to show the same numbers.
    /// </summary>
    public sealed class ReportResultBuilder
    {
        public const string DefaultPrimary = "#1F3A5F", DefaultSecondary = "#C8963E";
        private readonly IReportSetupRepository _setup;
        private readonly IReportMarksRepository _marks;
        private readonly IReportResultRepository _results;
        private readonly IAttendanceRepository _attendance;
        private readonly IStudentIdCardRepository _school;
        private readonly ILogger<ReportResultBuilder> _logger;

        public ReportResultBuilder(IReportSetupRepository setup, IReportMarksRepository marks, IReportResultRepository results, IAttendanceRepository attendance, IStudentIdCardRepository school, ILogger<ReportResultBuilder> logger)
        { _setup = setup; _marks = marks; _results = results; _attendance = attendance; _school = school; _logger = logger; }

        /// <summary>Null when the scheme, section, class-in-scheme or term does not exist for this school (the caller answers 404).</summary>
        public async Task<ResultSet?> BuildAsync(string schoolId, long schemeId, long sectionId, string scope, long? termId, int activeId, DateOnly today, CancellationToken ct)
        {
            var graph = await _setup.LoadGraphAsync(schoolId, schemeId, false, ct);
            if (graph == null || graph.Scheme.StatusId != activeId) return null;
            var section = await _marks.GetSectionAsync(schoolId, sectionId, activeId, ct);
            if (section == null || graph.Classes.All(c => c.ClassId != section.ClassId)) return null;
            var year = await _marks.YearAsync(schoolId, graph.Scheme.AcademicYearId, ct);
            if (year == null) return null;
            var sch = graph.Scheme;
            var terms = graph.Terms.OrderBy(t => t.Sequence).ToList();
            TbReportTerm? term = null;
            if (scope == "TERM") { term = terms.FirstOrDefault(t => t.TermId == termId); if (term == null) return null; terms = new List<TbReportTerm> { term }; }
            var termIds = terms.Select(t => t.TermId).ToHashSet();
            var marksMode = sch.EvaluationMode == "MARKS";
            var truncate = sch.PercentageRounding == "TRUNCATE";

            // ---- setup
            var subjectNames = await _results.GetSubjectNamesAsync(schoolId, graph.Subjects.Where(s => s.ClassId == section.ClassId).Select(s => s.SubjectId).Distinct().ToList(), ct);
            var subjects = graph.Subjects.Where(s => s.ClassId == section.ClassId).OrderBy(s => s.DisplayOrder).ThenBy(s => subjectNames.GetValueOrDefault(s.SubjectId, "")).ToList();
            var exams = graph.Exams.Where(e => termIds.Contains(e.TermId)).ToDictionary(e => e.ExamId);
            var cells = graph.Cells.Where(c => c.ClassId == section.ClassId && exams.ContainsKey(c.ExamId)).ToList();
            var bands = sch.GradeScaleId == null ? new List<ResultBand>() : await _results.GetBandsAsync(schoolId, sch.GradeScaleId.Value, ct);
            var calc = new CalcScheme
            {
                IsMarksMode = marksMode, WeightedYear = sch.YearCombine == "WEIGHTED", SubjectPassPercent = sch.SubjectPassPercent, OverallPassPercent = sch.OverallPassPercent, Truncate = truncate,
                ShowRank = sch.ShowRank && marksMode, RankByClass = sch.RankScope == "CLASS",
                Bands = bands.Where(b => b.MinPercent != null || !marksMode).Select(b => new CalcBand(b.Code, b.Label, b.MinPercent ?? b.Sequence, b.IsFail)).ToList(),
                Terms = terms.Select(t => new CalcTerm(t.TermId, t.Sequence, t.TermName, t.YearWeight)).ToList(),
                Subjects = subjects.Select(s => new CalcSubject(s.SchemeSubjectId, s.LabelOverride ?? subjectNames.GetValueOrDefault(s.SubjectId, "Subject"), s.IncludeInTotal, s.DisplayOrder)).ToList(),
                Cells = cells.Select(c => new CalcCell(c.ExamSubjectId, exams[c.ExamId].TermId, c.ExamId, c.SchemeSubjectId, c.EntryMode == "MARKS", c.EntryMode == "MARKS" ? c.MaxMarks ?? exams[c.ExamId].DefaultMaxMarks : null,
                    c.EntryMode == "MARKS" ? c.WeightPoints ?? exams[c.ExamId].WeightPoints : null, c.EntryMode == "GRADE" ? graph.Skills.Where(k => k.SchemeSubjectId == c.SchemeSubjectId).OrderBy(k => k.Sequence).Select(k => k.SkillId).ToList() : new List<long>())).ToList()
            };

            // ---- the people: the section, plus the rest of the class when the rank is class-wide
            var sectionIds = new List<long> { section.SectionId };
            if (sch.RankScope == "CLASS" && marksMode && sch.ShowRank) sectionIds = await _results.GetClassSectionIdsAsync(schoolId, section.ClassId, activeId, ct);
            var pool = await _results.GetStudentsAsync(schoolId, sch.AcademicYearId, sectionIds, activeId, ct);
            var students = pool.Where(s => s.SectionId == section.SectionId).OrderBy(s => s.RollNumber, RollComparer.Instance).ThenBy(s => s.FirstName, StringComparer.OrdinalIgnoreCase).ToList();
            var marks = await _results.GetMarksAsync(schoolId, cells.Select(c => c.ExamSubjectId).ToList(), pool.Select(s => s.StudentId).ToList(), ct);
            var byStudent = marks.GroupBy(m => m.StudentId).ToDictionary(g => g.Key, g => g.ToList());
            var calcStudents = pool.Select(s =>
            {
                var cs = new CalcStudent { StudentId = s.StudentId, SectionId = s.SectionId, ClassId = s.ClassId };
                foreach (var m in byStudent.GetValueOrDefault(s.StudentId) ?? new())
                    cs.Entries[(m.ExamSubjectId, m.SkillId)] = new CalcEntry(m.State == "ENTERED" ? EntryKind.Entered : m.State == "ABSENT" ? EntryKind.Absent : EntryKind.Exempt, m.Marks, m.GradeCode);
                return cs;
            }).ToList();
            var results = ReportCalculator.Calculate(calc, calcStudents);

            // ---- the class teacher's inputs and the term attendance
            var activity = await _results.GetActivityGradesAsync(sch.SchemeId, section.SectionId, ct);
            var termInputs = (await _results.GetTermInputsAsync(sch.SchemeId, section.SectionId, ct)).ToDictionary(t => (t.TermId, t.StudentId));
            var yearInputs = (await _marks.GetYearStudentsAsync(sch.SchemeId, section.SectionId, false, ct)).ToDictionary(y => y.StudentId);
            var areas = (await _marks.GetActivityAreasAsync(schoolId, activeId, ct)).ToDictionary(a => a.AreaId);
            var areaBands = new Dictionary<long, List<ResultBand>>();
            foreach (var scaleId in areas.Values.Select(a => a.RatingScaleId).Distinct()) areaBands[scaleId] = await _results.GetBandsAsync(schoolId, scaleId, ct);
            var parents = (await _results.GetParentsAsync(schoolId, students.Select(s => s.StudentId).ToList(), ct)).GroupBy(p => p.StudentId).ToDictionary(g => g.Key, g => g.ToList());

            var attendance = new Dictionary<long, (TermAttendanceResult? Auto, Dictionary<string, (int? Working, int? Present)> Manual)>();
            var unmarked = new List<DateOnly>();
            if (sch.AttendanceSource == "AUTO")
            {
                var from = terms.Min(t => t.StartDate); var to = terms.Max(t => t.EndDate);
                var roster = await _attendance.GetReportRosterAsync(schoolId, sch.AcademicYearId, section.SectionId, ct);
                var attMarks = await _attendance.GetMonthMarksAsync(schoolId, section.SectionId, from, to, ct);
                var holidays = await _attendance.GetHolidaysAsync(schoolId, from, to, activeId, ct);
                foreach (var t in terms)
                {
                    var a = TermAttendanceRules.Compute(t.StartDate, t.EndDate, today, year.StartDate, year.EndDate, holidays, roster, attMarks, activeId);
                    attendance[t.TermId] = (a, new());
                    unmarked.AddRange(a.UnmarkedDates);
                }
            }
            else foreach (var t in terms) attendance[t.TermId] = (null, students.ToDictionary(s => s.StudentId, s => termInputs.TryGetValue((t.TermId, s.StudentId), out var ti) ? ((int?)ti.ManualWorkingDays, (int?)ti.ManualPresentDays) : ((int?)null, (int?)null)));

            // ---- locks, template, school header
            var locks = (await _marks.GetTermSectionsAsync(sch.SchemeId, section.SectionId, ct)).Where(l => l.Status == "LOCKED").Select(l => l.TermId).ToHashSet();
            var unlocked = terms.Where(t => !locks.Contains(t.TermId)).Select(t => t.TermName).ToList();
            var template = ReportCardDesigns.Find(sch.TemplateKey);
            var templateOk = template != null && template.SupportedModes.Contains(sch.EvaluationMode);
            var schoolRow = await _school.GetSchoolAsync(schoolId, ct);
            var header = new RcHeader
            {
                SchoolName = schoolRow?.SchoolName ?? "",
                AddressLine = string.Join(", ", new[] { schoolRow?.AddressLine1, schoolRow?.AddressLine2, schoolRow?.CityName, schoolRow?.StateName }.Where(x => !string.IsNullOrWhiteSpace(x))) + (string.IsNullOrWhiteSpace(schoolRow?.Pincode) ? "" : " - " + schoolRow!.Pincode),
                Phone = schoolRow?.MobileNumber ?? "", LogoKey = string.IsNullOrWhiteSpace(schoolRow?.LogoUrl) ? null : schoolRow!.LogoUrl
            };
            var legend = bands.Where(b => b.MinPercent != null || !marksMode).Select(b => new RcBand { Code = b.Code, Label = b.Label, Min = b.MinPercent }).ToList();
            var config = new RcConfigSnapshot
            {
                SchemeName = sch.SchemeName, SessionName = year.YearName, Scope = scope, TermName = term?.TermName, EvaluationMode = sch.EvaluationMode, YearCombine = sch.YearCombine, SubjectPassPercent = sch.SubjectPassPercent,
                OverallPassPercent = sch.OverallPassPercent, Rounding = sch.PercentageRounding, RankScope = sch.RankScope, ShowRank = sch.ShowRank, AttendanceSource = sch.AttendanceSource, TemplateKey = sch.TemplateKey,
                Bands = legend, Header = header, SignatureKey = null, Primary = DefaultPrimary, Secondary = DefaultSecondary
            };

            var rcTerms = terms.Select(t => new RcTerm
            {
                TermId = t.TermId, Name = t.TermName, Start = t.StartDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture), End = t.EndDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
                Exams = graph.Exams.Where(e => e.TermId == t.TermId).OrderBy(e => e.Sequence).Select(e => new RcExam { ExamId = e.ExamId, Name = e.ExamName, Max = marksMode ? e.DefaultMaxMarks : null, Weight = marksMode ? e.WeightPoints : null }).ToList()
            }).ToList();

            var models = new Dictionary<string, ReportCardModel>();
            foreach (var s in students)
            {
                var r = results[s.StudentId];
                var model = new ReportCardModel
                {
                    TemplateKey = sch.TemplateKey, Mode = sch.EvaluationMode, SessionName = year.YearName, Scope = scope, ShowRank = sch.ShowRank && marksMode, Primary = DefaultPrimary, Secondary = DefaultSecondary, Header = header, Legend = legend,
                    Student = new RcStudent
                    {
                        StudentId = s.StudentId, Name = LeaveRules.FullName(s.FirstName, s.MiddleName, s.LastName), ClassName = s.ClassName, SectionName = s.SectionName, Roll = s.RollNumber ?? "", AdmissionNo = s.AdmissionNumber,
                        Dob = s.DateOfBirth.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
                        Father = parents.GetValueOrDefault(s.StudentId)?.FirstOrDefault(p => p.Relationship == "FATHER")?.Name ?? "", Mother = parents.GetValueOrDefault(s.StudentId)?.FirstOrDefault(p => p.Relationship == "MOTHER")?.Name ?? ""
                    },
                    Terms = rcTerms
                };
                string? Remark(string? grade) => grade == null ? null : bands.FirstOrDefault(b => string.Equals(b.Code, grade, StringComparison.OrdinalIgnoreCase))?.Label;
                foreach (var subj in calc.Subjects)
                {
                    var rs = new RcSubject { Name = subj.Name, Included = subj.IncludeInTotal };
                    foreach (var tr in r.Terms)
                    {
                        var sr = tr.Subjects.FirstOrDefault(x => x.SchemeSubjectId == subj.SchemeSubjectId);
                        if (sr == null) continue;
                        rs.GradeOnly = marksMode && sr.Cells.Count > 0 && sr.Cells.All(c => !c.IsMarks);
                        rs.Terms.Add(new RcSubjectTerm
                        {
                            TermId = tr.TermId, Obtained = sr.Obtained, OutOf = sr.OutOf, Percent = sr.Percent, Grade = sr.Grade, Remark = Remark(sr.Grade), Failed = sr.Failed,
                            Cells = sr.Cells.Select(c => new RcCell
                            {
                                ExamId = c.ExamId, State = c.State, Marks = c.Marks, Grade = c.Grade,
                                Skills = c.Skills.Select(k => new RcSkillGrade { Name = graph.Skills.FirstOrDefault(x => x.SkillId == k.SkillId)?.SkillName ?? "", State = k.State, Grade = k.Grade }).ToList()
                            }).ToList()
                        });
                    }
                    var y = r.Year.Subjects.FirstOrDefault(x => x.SchemeSubjectId == subj.SchemeSubjectId);
                    if (y != null) { rs.YearObtained = y.Obtained; rs.YearOutOf = y.OutOf; rs.YearPercent = y.Percent; rs.YearGrade = y.Grade; rs.YearFailed = y.Failed; }
                    if (rs.Terms.Count > 0) model.Subjects.Add(rs);
                }
                string SubjectName(long id) => calc.Subjects.FirstOrDefault(x => x.SchemeSubjectId == id)?.Name ?? "";
                foreach (var tr in r.Terms)
                {
                    var ts = new RcTermSummary
                    {
                        TermId = tr.TermId, Obtained = tr.Obtained, OutOf = tr.OutOf, Percent = tr.Percent, Grade = tr.Grade, Result = tr.Status, FailedSubjects = tr.FailedSubjects.Select(SubjectName).ToList(), Rank = tr.Rank, RankedCount = tr.RankedCount
                    };
                    if (termInputs.TryGetValue((tr.TermId, s.StudentId), out var ti)) { ts.Remark = ti.Remark; ts.Conduct = ti.Conduct; ts.RankText = ti.RankText; }
                    var att = attendance.GetValueOrDefault(tr.TermId);
                    if (att.Auto != null && att.Auto.ByStudent.TryGetValue(s.StudentId, out var row)) { ts.WorkingDays = row.Present + row.Absent; ts.PresentDays = row.Present; }
                    else if (att.Manual != null && att.Manual.TryGetValue(s.StudentId, out var man)) { ts.WorkingDays = man.Working; ts.PresentDays = man.Present; }
                    foreach (var g in activity.Where(a => a.TermId == tr.TermId && a.StudentId == s.StudentId))
                        if (areas.TryGetValue(g.AreaId, out var area))
                            ts.Activities.Add(new RcActivity { Category = area.Category, Area = area.Name, Grade = g.GradeCode, GradeLabel = areaBands.GetValueOrDefault(area.RatingScaleId)?.FirstOrDefault(b => string.Equals(b.Code, g.GradeCode, StringComparison.OrdinalIgnoreCase))?.Label ?? "" });
                    ts.Activities = ts.Activities.OrderBy(a => a.Category, StringComparer.Ordinal).ThenBy(a => a.Area, StringComparer.OrdinalIgnoreCase).ToList();
                    model.TermSummaries.Add(ts);
                }
                if (scope == "TERM")
                {
                    var tr = r.Terms[0];
                    model.Overall = new RcOverall { Obtained = tr.Obtained, OutOf = tr.OutOf, Percent = tr.Percent, Grade = tr.Grade, Result = tr.Status, FailedSubjects = tr.FailedSubjects.Select(SubjectName).ToList(), Rank = tr.Rank, RankedCount = tr.RankedCount };
                }
                else
                    model.Overall = new RcOverall { Obtained = r.Year.Obtained, OutOf = r.Year.OutOf, Percent = r.Year.Percent, Grade = r.Year.Grade, Result = r.Year.Status, FailedSubjects = r.Year.FailedSubjects.Select(SubjectName).ToList(), Rank = r.Year.Rank, RankedCount = r.Year.RankedCount };
                if (yearInputs.TryGetValue(s.StudentId, out var yi)) model.Promotion = new RcPromotion { Decision = yi.PromotionDecision, PromotedTo = yi.PromotedTo, NewSessionDate = yi.NewSessionDate?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) };
                models[s.StudentId] = model;
            }

            return new ResultSet
            {
                Graph = graph, SchemeId = sch.SchemeId, SchemeName = sch.SchemeName, SectionId = section.SectionId, ClassId = section.ClassId, ClassName = section.ClassName, SectionName = section.SectionName, Scope = scope, TermId = term?.TermId,
                TermName = term?.TermName, Mode = sch.EvaluationMode, TemplateKey = sch.TemplateKey, TemplateAvailable = templateOk,
                TemplateMessage = templateOk ? null : "This design is not available yet, choose Term Spread.", Students = students, Results = results, Models = models, Config = config, UnlockedTerms = unlocked,
                AttendanceSource = sch.AttendanceSource, UnmarkedDates = unmarked.Distinct().OrderBy(d => d).ToList()
            };
        }
    }
}
