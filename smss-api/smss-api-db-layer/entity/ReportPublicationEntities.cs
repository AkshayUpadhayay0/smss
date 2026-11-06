using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    // Examination / Report Card, STAGE 3 (results and publishing). Mirrors Docs/smss-report-card-stage3.sql.

    [Table("tb_report_publications", Schema = "public")]
    public class TbReportPublication
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("publication_id")] public long PublicationId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("scheme_id")] public long SchemeId { get; set; }
        [Column("section_id")] public long SectionId { get; set; }
        [Column("class_id")] public long ClassId { get; set; }
        [Column("scope")][StringLength(4)] public string Scope { get; set; } = null!;
        [Column("term_id")] public long? TermId { get; set; }
        [Column("version")] public int Version { get; set; }
        [Column("status")][StringLength(10)] public string Status { get; set; } = "PUBLISHED";
        [Column("published_by")] public long PublishedBy { get; set; }
        [Column("published_at")] public DateTime PublishedAt { get; set; }
        [Column("note")][StringLength(300)] public string? Note { get; set; }
        [Column("config_snapshot", TypeName = "jsonb")] public string ConfigSnapshot { get; set; } = "{}";
        [Column("excluded_students")] public string? ExcludedStudents { get; set; }
        [Column("student_count")] public int StudentCount { get; set; }
    }

    [Table("tb_report_publication_students", Schema = "public")]
    public class TbReportPublicationStudent
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("pub_student_id")] public long PubStudentId { get; set; }
        [Column("publication_id")] public long PublicationId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("student_id")][StringLength(20)] public string StudentId { get; set; } = null!;
        [Column("student_name")][StringLength(200)] public string StudentName { get; set; } = null!;
        [Column("admission_number")][StringLength(50)] public string AdmissionNumber { get; set; } = null!;
        [Column("roll_number")][StringLength(30)] public string? RollNumber { get; set; }
        [Column("result_status")][StringLength(10)] public string ResultStatus { get; set; } = null!;
        [Column("total_obtained", TypeName = "numeric(10,2)")] public decimal? TotalObtained { get; set; }
        [Column("total_max", TypeName = "numeric(10,2)")] public decimal? TotalMax { get; set; }
        [Column("percentage", TypeName = "numeric(6,2)")] public decimal? Percentage { get; set; }
        [Column("grade_code")][StringLength(10)] public string? GradeCode { get; set; }
        [Column("rank_position")] public int? RankPosition { get; set; }
        [Column("card_model", TypeName = "jsonb")] public string CardModel { get; set; } = "{}";
    }
}
