using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>A school's ID-card branding. One row per school (school_id is the key); no row means the defaults.</summary>
    [Table("tb_school_id_card_settings", Schema = "public")]
    public class TbSchoolIdCardSettings
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.None)][Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("template_code")][StringLength(30)] public string TemplateCode { get; set; } = null!;
        [Column("primary_color")][StringLength(7)] public string PrimaryColor { get; set; } = null!;
        [Column("secondary_color")][StringLength(7)] public string SecondaryColor { get; set; } = null!;
        [Column("tagline")][StringLength(120)] public string? Tagline { get; set; }
        [Column("website")][StringLength(150)] public string? Website { get; set; }
        [Column("email")][StringLength(150)] public string? Email { get; set; }
        /// <summary>Key in the PRIVATE file store; never a URL.</summary>
        [Column("principal_signature_path")][StringLength(300)] public string? PrincipalSignaturePath { get; set; }
        [Column("terms_text")][StringLength(300)] public string? TermsText { get; set; }
        [Column("show_dob")] public bool ShowDob { get; set; } = true;
        [Column("show_address")] public bool ShowAddress { get; set; } = true;
        [Column("show_blood_group")] public bool ShowBloodGroup { get; set; } = true;
        [Column("show_mother_name")] public bool ShowMotherName { get; set; } = true;
        [Column("updated_by")] public long? UpdatedBy { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
