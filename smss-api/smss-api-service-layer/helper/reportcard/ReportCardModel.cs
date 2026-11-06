using System.Text.Json;
using System.Text.Json.Serialization;

namespace smss_api_service_layer.helper.reportcard
{
    // The model a template draws. It is built once (from live data for a draft preview) and STORED as jsonb when published, so a published card re-renders from exactly what it was
    // first drawn from. No binary data lives in it: a logo is a storage key, resolved when the card is drawn.

    public sealed class RcHeader
    {
        public string SchoolName { get; set; } = "";
        public string AddressLine { get; set; } = "";
        public string Phone { get; set; } = "";
        /// <summary>Storage key (the school record's logo path). Bytes are read when the card is drawn, never stored.</summary>
        public string? LogoKey { get; set; }
    }

    public sealed class RcStudent
    {
        public string StudentId { get; set; } = "";
        public string Name { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string SectionName { get; set; } = "";
        public string Roll { get; set; } = "";
        public string AdmissionNo { get; set; } = "";
        public string Dob { get; set; } = "";
        public string Father { get; set; } = "";
        public string Mother { get; set; } = "";
    }

    public sealed class RcExam { public long ExamId { get; set; } public string Name { get; set; } = ""; public decimal? Max { get; set; } public decimal? Weight { get; set; } }

    public sealed class RcTerm
    {
        public long TermId { get; set; }
        public string Name { get; set; } = "";
        public string Start { get; set; } = "";
        public string End { get; set; } = "";
        public List<RcExam> Exams { get; set; } = new();
    }

    public sealed class RcCell
    {
        public long ExamId { get; set; }
        /// <summary>ENTERED, ABSENT, EXEMPT or MISSING.</summary>
        public string State { get; set; } = "";
        public decimal? Marks { get; set; }
        public string? Grade { get; set; }
        public List<RcSkillGrade> Skills { get; set; } = new();
    }

    public sealed class RcSkillGrade { public string Name { get; set; } = ""; public string State { get; set; } = ""; public string? Grade { get; set; } }

    public sealed class RcSubjectTerm
    {
        public long TermId { get; set; }
        public List<RcCell> Cells { get; set; } = new();
        public decimal Obtained { get; set; }
        public decimal OutOf { get; set; }
        public decimal? Percent { get; set; }
        public string? Grade { get; set; }
        public string? Remark { get; set; }
        public bool Failed { get; set; }
    }

    public sealed class RcSubject
    {
        public string Name { get; set; } = "";
        public bool Included { get; set; } = true;
        /// <summary>A GRADE entry in a MARKS scheme: shown, never counted.</summary>
        public bool GradeOnly { get; set; }
        public List<RcSubjectTerm> Terms { get; set; } = new();
        public decimal YearObtained { get; set; }
        public decimal YearOutOf { get; set; }
        public decimal? YearPercent { get; set; }
        public string? YearGrade { get; set; }
        public bool YearFailed { get; set; }
    }

    public sealed class RcActivity { public string Category { get; set; } = ""; public string Area { get; set; } = ""; public string Grade { get; set; } = ""; public string GradeLabel { get; set; } = ""; }

    public sealed class RcTermSummary
    {
        public long TermId { get; set; }
        public decimal Obtained { get; set; }
        public decimal OutOf { get; set; }
        public decimal? Percent { get; set; }
        public string? Grade { get; set; }
        public string Result { get; set; } = "";
        public List<string> FailedSubjects { get; set; } = new();
        public int? Rank { get; set; }
        public int? RankedCount { get; set; }
        public int? WorkingDays { get; set; }
        public int? PresentDays { get; set; }
        public string? Remark { get; set; }
        public string? Conduct { get; set; }
        public string? RankText { get; set; }
        public List<RcActivity> Activities { get; set; } = new();
    }

    public sealed class RcOverall
    {
        public decimal Obtained { get; set; }
        public decimal OutOf { get; set; }
        public decimal? Percent { get; set; }
        public string? Grade { get; set; }
        public string Result { get; set; } = "";
        public List<string> FailedSubjects { get; set; } = new();
        public int? Rank { get; set; }
        public int? RankedCount { get; set; }
    }

    public sealed class RcPromotion { public string? Decision { get; set; } public string? PromotedTo { get; set; } public string? NewSessionDate { get; set; } }
    public sealed class RcBand { public string Code { get; set; } = ""; public string Label { get; set; } = ""; public decimal? Min { get; set; } }

    public sealed class ReportCardModel
    {
        public int SchemaVersion { get; set; } = 1;
        public string TemplateKey { get; set; } = "term-spread";
        /// <summary>MARKS or GRADES.</summary>
        public string Mode { get; set; } = "MARKS";
        public string SessionName { get; set; } = "";
        /// <summary>TERM or YEAR.</summary>
        public string Scope { get; set; } = "YEAR";
        public bool ShowRank { get; set; }
        public string Primary { get; set; } = "#1F3A5F";
        public string Secondary { get; set; } = "#C8963E";
        public RcHeader Header { get; set; } = new();
        public RcStudent Student { get; set; } = new();
        public List<RcTerm> Terms { get; set; } = new();
        public List<RcSubject> Subjects { get; set; } = new();
        public List<RcTermSummary> TermSummaries { get; set; } = new();
        public RcOverall Overall { get; set; } = new();
        public RcPromotion Promotion { get; set; } = new();
        public List<RcBand> Legend { get; set; } = new();
        public int? Version { get; set; }
        public string? PublishedAt { get; set; }

        public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.Never, WriteIndented = false };
        public string ToJson() => JsonSerializer.Serialize(this, Json);
        public static ReportCardModel FromJson(string json) => JsonSerializer.Deserialize<ReportCardModel>(json, Json) ?? new ReportCardModel();
    }
}
