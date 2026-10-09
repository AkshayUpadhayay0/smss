using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    [Table("tb_sections", Schema = "public")]
    public class TbSections
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("section_id")]
        public long SectionId { get; set; }

        [Column("school_id")] public string SchoolId { get; set; } = null!;
        [Column("class_id")] public long ClassId { get; set; }
        [Column("section_name")] public string SectionName { get; set; } = null!;
        [Column("max_strength")] public short? MaxStrength { get; set; }
        [Column("status_id")] public int? StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    /// <summary>A section joined to its class, so list screens need no second lookup.</summary>
    public record SectionWithClass(TbSections Section, string ClassName, short ClassSequenceOrder);
}
