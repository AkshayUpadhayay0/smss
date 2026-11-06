using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // Examination / Report Card, STAGE 1 (setup). None of these carries a school id: it always comes from the token.

    // ------------------------------------------------------------------ grade scales
    public class BandRequest
    {
        [Required, StringLength(10)] public string? GradeCode { get; set; }
        [Required, StringLength(60)] public string? Label { get; set; }
        /// <summary>PERCENT_BANDS: the LOWER edge, inclusive (the upper edge is the next band's lower edge). RATING: leave out.</summary>
        [Range(0, 100)] public decimal? MinPercent { get; set; }
        [Range(0, 99.9)] public decimal? GradePoint { get; set; }
        public bool IsFail { get; set; }
        [StringLength(200)] public string? Description { get; set; }
    }

    public class SaveGradeScaleRequest
    {
        [Required, StringLength(80)] public string? Name { get; set; }
        /// <summary>PERCENT_BANDS or RATING.</summary>
        [Required] public string? Kind { get; set; }
        [Required] public List<BandRequest>? Bands { get; set; }
    }

    public class GradeScaleFromPresetRequest
    {
        [Required] public string? PresetCode { get; set; }
        [StringLength(80)] public string? Name { get; set; }
    }

    public class GradeBandDto
    {
        public int Sequence { get; set; }
        public string GradeCode { get; set; } = null!;
        public string Label { get; set; } = null!;
        public decimal? MinPercent { get; set; }
        /// <summary>The derived upper edge for display (the next band's lower edge, 100 for the top band): shown as "up to just under X". Null for ratings.</summary>
        public decimal? UpToPercent { get; set; }
        public decimal? GradePoint { get; set; }
        public bool IsFail { get; set; }
        public string? Description { get; set; }
    }

    public class GradeScaleListDto
    {
        public long GradeScaleId { get; set; }
        public string ScaleName { get; set; } = null!;
        public string Kind { get; set; } = null!;
        public string? SourcePresetCode { get; set; }
        public int BandCount { get; set; }
        public int SchemeCount { get; set; }
        public int AreaCount { get; set; }
        public string StatusName { get; set; } = null!;
    }

    public class GradeScaleDto : GradeScaleListDto
    {
        /// <summary>Highest band first (percent scales) or in the given order (ratings).</summary>
        public List<GradeBandDto> Bands { get; set; } = new();
    }

    public class ScalePresetDto
    {
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string Kind { get; set; } = null!;
        public string Note { get; set; } = null!;
        public List<GradeBandDto> Bands { get; set; } = new();
    }

    // ------------------------------------------------------------------ templates and presets
    public class ReportTemplateDto
    {
        public string Key { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string Description { get; set; } = null!;
        public List<string> Modes { get; set; } = new();
        public string Orientation { get; set; } = null!;
        /// <summary>False until the template's renderer exists (Stage 3 / 4): it can already be chosen for a scheme.</summary>
        public bool Implemented { get; set; }
    }

    public class PresetExamDto { public string Name { get; set; } = null!; public decimal MaxMarks { get; set; } public decimal Weight { get; set; } }
    public class PresetTermDto { public string Name { get; set; } = null!; public List<PresetExamDto> Exams { get; set; } = new(); }

    public class SchemePresetDto
    {
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string Description { get; set; } = null!;
        public string EvaluationMode { get; set; } = null!;
        public string TemplateKey { get; set; } = null!;
        public string ScalePresetCode { get; set; } = null!;
        public string ScaleName { get; set; } = null!;
        public bool ShowRank { get; set; }
        public string EntryMode { get; set; } = null!;
        public List<PresetTermDto> Terms { get; set; } = new();
        public List<string> Skills { get; set; } = new();
    }

    // ------------------------------------------------------------------ schemes
    public class SaveSchemeRequest
    {
        [Required, StringLength(80)] public string? Name { get; set; }
        [Required] public long? AcademicYearId { get; set; }
        /// <summary>MARKS or GRADES.</summary>
        [Required] public string? EvaluationMode { get; set; }
        public long? GradeScaleId { get; set; }
        public string? YearCombine { get; set; }
        [Range(0, 100)] public decimal? SubjectPassPercent { get; set; }
        [Range(0, 100)] public decimal? OverallPassPercent { get; set; }
        public bool ShowRank { get; set; }
        public string? RankScope { get; set; }
        public string? AttendanceSource { get; set; }
        public string? PercentageRounding { get; set; }
        [Required, StringLength(30)] public string? TemplateKey { get; set; }
    }

    public class SchemeListDto
    {
        public long SchemeId { get; set; }
        public string SchemeName { get; set; } = null!;
        public long AcademicYearId { get; set; }
        public string AcademicYearName { get; set; } = null!;
        public string EvaluationMode { get; set; } = null!;
        public string TemplateKey { get; set; } = null!;
        public List<string> ClassNames { get; set; } = new();
        public int TermCount { get; set; }
        public int ExamCount { get; set; }
        public int SubjectCount { get; set; }
        public string StatusName { get; set; } = null!;
    }

    public class SchemeClassDto { public long ClassId { get; set; } public string ClassName { get; set; } = null!; }

    public class SchemeExamDto
    {
        public long ExamId { get; set; }
        public string Name { get; set; } = null!;
        public int Sequence { get; set; }
        public decimal DefaultMaxMarks { get; set; }
        public decimal WeightPoints { get; set; }
        public DateOnly? ExamDate { get; set; }
    }

    public class SchemeTermDto
    {
        public long TermId { get; set; }
        public string Name { get; set; } = null!;
        public int Sequence { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public decimal? YearWeight { get; set; }
        public List<SchemeExamDto> Exams { get; set; } = new();
    }

    public class SchemeSkillDto { public long SkillId { get; set; } public string Name { get; set; } = null!; public int Sequence { get; set; } }

    public class SchemeSubjectDto
    {
        public long SchemeSubjectId { get; set; }
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public long SubjectId { get; set; }
        public string SubjectName { get; set; } = null!;
        public int DisplayOrder { get; set; }
        public string? LabelOverride { get; set; }
        public bool IncludeInTotal { get; set; }
        public List<SchemeSkillDto> Skills { get; set; } = new();
    }

    public class SchemeCellDto
    {
        public long ExamSubjectId { get; set; }
        public long ExamId { get; set; }
        public long SchemeSubjectId { get; set; }
        public long ClassId { get; set; }
        public decimal? MaxMarks { get; set; }
        public decimal? WeightPoints { get; set; }
        public string EntryMode { get; set; } = null!;
        public decimal? EffectiveMax { get; set; }
        public decimal? EffectiveWeight { get; set; }
    }

    public class SchemeDto : SchemeListDto
    {
        public long? GradeScaleId { get; set; }
        public string? GradeScaleName { get; set; }
        public string YearCombine { get; set; } = null!;
        public decimal SubjectPassPercent { get; set; }
        public decimal OverallPassPercent { get; set; }
        public bool ShowRank { get; set; }
        public string RankScope { get; set; } = null!;
        public string AttendanceSource { get; set; } = null!;
        public string PercentageRounding { get; set; } = null!;
        public List<SchemeClassDto> Classes { get; set; } = new();
        public List<SchemeTermDto> Terms { get; set; } = new();
        public List<SchemeSubjectDto> Subjects { get; set; } = new();
        public List<SchemeCellDto> Cells { get; set; } = new();
        /// <summary>Things worth a look (a subject no longer mapped to its class, a class with no subjects, a template whose renderer is not built yet, ...). Never blocking.</summary>
        public List<string> Warnings { get; set; } = new();
    }

    public class SetSchemeClassesRequest { [Required] public List<long>? ClassIds { get; set; } }

    public class SaveExamRequest
    {
        public long? ExamId { get; set; }
        [Required, StringLength(60)] public string? Name { get; set; }
        [Required, Range(0.01, 1000)] public decimal? DefaultMaxMarks { get; set; }
        /// <summary>What this exam adds to the term total; leave out for "the same as its max marks".</summary>
        [Range(0.01, 1000)] public decimal? WeightPoints { get; set; }
    }

    public class SaveTermRequest
    {
        public long? TermId { get; set; }
        [Required, StringLength(60)] public string? Name { get; set; }
        [Required] public DateOnly? StartDate { get; set; }
        [Required] public DateOnly? EndDate { get; set; }
        [Range(0.01, 10000)] public decimal? YearWeight { get; set; }
        [Required] public List<SaveExamRequest>? Exams { get; set; }
    }

    public class SaveTermsRequest { [Required] public List<SaveTermRequest>? Terms { get; set; } }

    public class SaveSchemeSubjectRequest
    {
        public long? SchemeSubjectId { get; set; }
        [Required] public long? ClassId { get; set; }
        [Required] public long? SubjectId { get; set; }
        public int DisplayOrder { get; set; }
        [StringLength(60)] public string? LabelOverride { get; set; }
        public bool IncludeInTotal { get; set; } = true;
    }

    public class SaveSubjectsRequest { [Required] public List<SaveSchemeSubjectRequest>? Subjects { get; set; } }

    public class SaveCellRequest
    {
        [Required] public long? ExamId { get; set; }
        [Required] public long? SchemeSubjectId { get; set; }
        [Range(0.01, 1000)] public decimal? MaxMarks { get; set; }
        [Range(0.01, 1000)] public decimal? WeightPoints { get; set; }
        /// <summary>MARKS or GRADE. In a MARKS scheme a GRADE cell is display-only: its grade is shown on the card but never summed, ranked or failed.</summary>
        [Required] public string? EntryMode { get; set; }
    }

    /// <summary>The whole matrix: a cell that is not listed does not exist (the subject is not examined in that exam).</summary>
    public class SaveMatrixRequest { [Required] public List<SaveCellRequest>? Cells { get; set; } }

    public class SaveSubjectSkillsRequest
    {
        [Required] public long? SchemeSubjectId { get; set; }
        public List<string>? Skills { get; set; }
    }

    public class SaveSkillsRequest { [Required] public List<SaveSubjectSkillsRequest>? Subjects { get; set; } }

    public class CloneSchemeRequest
    {
        [StringLength(80)] public string? Name { get; set; }
        /// <summary>Target academic year; leave out for the same year (then the classes are NOT copied: a class can be in one scheme per year).</summary>
        public long? AcademicYearId { get; set; }
    }

    public class PresetTermDatesRequest { [Required] public DateOnly? StartDate { get; set; } [Required] public DateOnly? EndDate { get; set; } }

    public class SchemeFromPresetRequest
    {
        [Required] public string? PresetCode { get; set; }
        [Required] public long? AcademicYearId { get; set; }
        [StringLength(80)] public string? SchemeName { get; set; }
        [Required] public List<long>? ClassIds { get; set; }
        /// <summary>Optional: one date range per preset term, in order. Left out, the academic year is split evenly.</summary>
        public List<PresetTermDatesRequest>? TermDates { get; set; }
        public string? TemplateKey { get; set; }
        public bool? ShowRank { get; set; }
        public string? PercentageRounding { get; set; }
        public decimal? SubjectPassPercent { get; set; }
        public decimal? OverallPassPercent { get; set; }
    }

    // ------------------------------------------------------------------ activity areas
    public class SaveActivityAreaRequest
    {
        [Required] public string? Category { get; set; }
        [Required, StringLength(60)] public string? Name { get; set; }
        public int Sequence { get; set; }
        [Required] public long? RatingScaleId { get; set; }
    }

    public class ActivityAreaDto
    {
        public long AreaId { get; set; }
        public string Category { get; set; } = null!;
        public string Name { get; set; } = null!;
        public int Sequence { get; set; }
        public long RatingScaleId { get; set; }
        public string RatingScaleName { get; set; } = null!;
        public string StatusName { get; set; } = null!;
    }

    public class StarterAreasResultDto { public int Created { get; set; } public int AlreadyThere { get; set; } public List<ActivityAreaDto> Areas { get; set; } = new(); }

    // ------------------------------------------------------------------ exam schedule (dates only)
    public class ScheduleClassDateDto { public long ClassId { get; set; } public string ClassName { get; set; } = null!; public DateOnly ExamDate { get; set; } }

    public class ScheduleExamDto
    {
        public long ExamId { get; set; }
        public string TermName { get; set; } = null!;
        public string ExamName { get; set; } = null!;
        public DateOnly? ExamDate { get; set; }
        public List<ScheduleClassDateDto> ClassDates { get; set; } = new();
    }

    public class ScheduleSchemeDto
    {
        public long SchemeId { get; set; }
        public string SchemeName { get; set; } = null!;
        public string AcademicYearName { get; set; } = null!;
        public List<string> ClassNames { get; set; } = new();
        /// <summary>The scheme's classes (id and name), for the per-class dates.</summary>
        public List<SchemeClassDto> Classes { get; set; } = new();
        public List<ScheduleExamDto> Exams { get; set; } = new();
    }

    public class SaveScheduleClassDate { [Required] public long? ClassId { get; set; } [Required] public DateOnly? ExamDate { get; set; } }

    public class SaveScheduleExam
    {
        [Required] public long? ExamId { get; set; }
        public DateOnly? ExamDate { get; set; }
        public List<SaveScheduleClassDate>? ClassDates { get; set; }
    }

    public class SaveScheduleRequest
    {
        [Required] public long? SchemeId { get; set; }
        [Required] public List<SaveScheduleExam>? Exams { get; set; }
    }
}
