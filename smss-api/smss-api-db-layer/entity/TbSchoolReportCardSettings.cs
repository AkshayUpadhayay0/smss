using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>A school's report card branding and options. One row per school (school_id is the key); no row means the defaults. Mirrors Docs/smss-report-card-stage5a.sql.</summary>
    [Table("tb_school_report_card_settings", Schema = "public")]
    public class TbSchoolReportCardSettings
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.None)][Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("primary_color")][StringLength(7)] public string PrimaryColor { get; set; } = "#1F3A5F";
        [Column("secondary_color")][StringLength(7)] public string SecondaryColor { get; set; } = "#C8963E";
        [Column("header_line")][StringLength(150)] public string? HeaderLine { get; set; }
        [Column("recognition_text")][StringLength(150)] public string? RecognitionText { get; set; }
        [Column("udise_code")][StringLength(20)] public string? UdiseCode { get; set; }
        [Column("footer_text")][StringLength(200)] public string? FooterText { get; set; }
        [Column("rules_text")][StringLength(1500)] public string? RulesText { get; set; }
        /// <summary>Key in the PRIVATE file store; never a URL. Every upload gets a new file name.</summary>
        [Column("principal_signature_path")][StringLength(300)] public string? PrincipalSignaturePath { get; set; }
        [Column("show_cover")] public bool ShowCover { get; set; }
        [Column("show_rules")] public bool ShowRules { get; set; }
        [Column("show_attendance")] public bool ShowAttendance { get; set; } = true;
        [Column("show_grade_legend")] public bool ShowGradeLegend { get; set; } = true;
        /// <summary>Print the student's photo on the designs that have a photo slot. Added in the stage 5b photo migration; off by default.</summary>
        [Column("show_photo")] public bool ShowPhoto { get; set; }
        [Column("updated_by")] public long? UpdatedBy { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
