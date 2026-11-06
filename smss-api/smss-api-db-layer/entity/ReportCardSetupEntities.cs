using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    // Examination / Report Card, STAGE 1 (setup). Mirrors Docs/smss-report-card-stage1.sql. No navigation properties, as elsewhere in the project.

    [Table("tb_grade_scales", Schema = "public")]
    public class TbGradeScale
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("grade_scale_id")] public long GradeScaleId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("scale_name")][StringLength(80)] public string ScaleName { get; set; } = null!;
        [Column("scale_kind")][StringLength(15)] public string ScaleKind { get; set; } = null!;
        [Column("source_preset_code")][StringLength(40)] public string? SourcePresetCode { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    [Table("tb_grade_scale_bands", Schema = "public")]
    public class TbGradeScaleBand
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("band_id")] public long BandId { get; set; }
        [Column("grade_scale_id")] public long GradeScaleId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("sequence")] public short Sequence { get; set; }
        [Column("grade_code")][StringLength(10)] public string GradeCode { get; set; } = null!;
        [Column("label")][StringLength(60)] public string Label { get; set; } = null!;
        [Column("min_percent", TypeName = "numeric(5,2)")] public decimal? MinPercent { get; set; }
        [Column("grade_point", TypeName = "numeric(4,1)")] public decimal? GradePoint { get; set; }
        [Column("is_fail")] public bool IsFail { get; set; }
        [Column("description")][StringLength(200)] public string? Description { get; set; }
    }

    [Table("tb_report_schemes", Schema = "public")]
    public class TbReportScheme
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("scheme_id")] public long SchemeId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("academic_year_id")] public long AcademicYearId { get; set; }
        [Column("scheme_name")][StringLength(80)] public string SchemeName { get; set; } = null!;
        [Column("evaluation_mode")][StringLength(10)] public string EvaluationMode { get; set; } = null!;
        [Column("grade_scale_id")] public long? GradeScaleId { get; set; }
        [Column("year_combine")][StringLength(15)] public string YearCombine { get; set; } = "SUM_OF_TERMS";
        [Column("subject_pass_percent", TypeName = "numeric(5,2)")] public decimal SubjectPassPercent { get; set; } = 33;
        [Column("overall_pass_percent", TypeName = "numeric(5,2)")] public decimal OverallPassPercent { get; set; } = 33;
        [Column("show_rank")] public bool ShowRank { get; set; }
        [Column("rank_scope")][StringLength(10)] public string RankScope { get; set; } = "SECTION";
        [Column("attendance_source")][StringLength(10)] public string AttendanceSource { get; set; } = "AUTO";
        [Column("percentage_rounding")][StringLength(15)] public string PercentageRounding { get; set; } = "ROUND_HALF_UP";
        [Column("template_key")][StringLength(30)] public string TemplateKey { get; set; } = null!;
        [Column("status_id")] public int StatusId { get; set; }
        [Column("created_by")] public long? CreatedBy { get; set; }
        [Column("updated_by")] public long? UpdatedBy { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    [Table("tb_report_scheme_classes", Schema = "public")]
    public class TbReportSchemeClass
    {
        [Column("scheme_id")] public long SchemeId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("academic_year_id")] public long AcademicYearId { get; set; }
        [Column("class_id")] public long ClassId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
    }

    [Table("tb_report_terms", Schema = "public")]
    public class TbReportTerm
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("term_id")] public long TermId { get; set; }
        [Column("scheme_id")] public long SchemeId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("term_name")][StringLength(60)] public string TermName { get; set; } = null!;
        [Column("sequence")] public short Sequence { get; set; }
        [Column("start_date", TypeName = "date")] public DateOnly StartDate { get; set; }
        [Column("end_date", TypeName = "date")] public DateOnly EndDate { get; set; }
        [Column("year_weight", TypeName = "numeric(6,2)")] public decimal? YearWeight { get; set; }
    }

    [Table("tb_report_exams", Schema = "public")]
    public class TbReportExam
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("exam_id")] public long ExamId { get; set; }
        [Column("term_id")] public long TermId { get; set; }
        [Column("scheme_id")] public long SchemeId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("exam_name")][StringLength(60)] public string ExamName { get; set; } = null!;
        [Column("sequence")] public short Sequence { get; set; }
        [Column("default_max_marks", TypeName = "numeric(6,2)")] public decimal DefaultMaxMarks { get; set; }
        [Column("weight_points", TypeName = "numeric(6,2)")] public decimal WeightPoints { get; set; }
        [Column("exam_date", TypeName = "date")] public DateOnly? ExamDate { get; set; }
    }

    [Table("tb_report_exam_class_dates", Schema = "public")]
    public class TbReportExamClassDate
    {
        [Column("exam_id")] public long ExamId { get; set; }
        [Column("scheme_id")] public long SchemeId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("class_id")] public long ClassId { get; set; }
        [Column("exam_date", TypeName = "date")] public DateOnly ExamDate { get; set; }
    }

    [Table("tb_report_scheme_subjects", Schema = "public")]
    public class TbReportSchemeSubject
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("scheme_subject_id")] public long SchemeSubjectId { get; set; }
        [Column("scheme_id")] public long SchemeId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("class_id")] public long ClassId { get; set; }
        [Column("subject_id")] public long SubjectId { get; set; }
        [Column("display_order")] public short DisplayOrder { get; set; }
        [Column("label_override")][StringLength(60)] public string? LabelOverride { get; set; }
        [Column("include_in_total")] public bool IncludeInTotal { get; set; } = true;
    }

    [Table("tb_report_exam_subjects", Schema = "public")]
    public class TbReportExamSubject
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("exam_subject_id")] public long ExamSubjectId { get; set; }
        [Column("exam_id")] public long ExamId { get; set; }
        [Column("scheme_id")] public long SchemeId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("scheme_subject_id")] public long SchemeSubjectId { get; set; }
        [Column("class_id")] public long ClassId { get; set; }
        [Column("max_marks", TypeName = "numeric(6,2)")] public decimal? MaxMarks { get; set; }
        [Column("weight_points", TypeName = "numeric(6,2)")] public decimal? WeightPoints { get; set; }
        [Column("entry_mode")][StringLength(5)] public string EntryMode { get; set; } = "MARKS";
    }

    [Table("tb_report_subject_skills", Schema = "public")]
    public class TbReportSubjectSkill
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("skill_id")] public long SkillId { get; set; }
        [Column("scheme_subject_id")] public long SchemeSubjectId { get; set; }
        [Column("scheme_id")] public long SchemeId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("skill_name")][StringLength(60)] public string SkillName { get; set; } = null!;
        [Column("sequence")] public short Sequence { get; set; }
    }

    [Table("tb_report_activity_areas", Schema = "public")]
    public class TbReportActivityArea
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("area_id")] public long AreaId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("category")][StringLength(15)] public string Category { get; set; } = null!;
        [Column("area_name")][StringLength(60)] public string AreaName { get; set; } = null!;
        [Column("sequence")] public short Sequence { get; set; }
        [Column("rating_scale_id")] public long RatingScaleId { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
