using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>Which classes exist in which academic year (S2). Active = offered, Inactive = withdrawn from that year. The class master is unchanged.</summary>
    [Table("tb_academic_year_classes", Schema = "public")]
    public class TbAcademicYearClass
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("year_class_id")] public long YearClassId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("academic_year_id")] public long AcademicYearId { get; set; }
        [Column("class_id")] public long ClassId { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
        [Column("created_by")] public long? CreatedBy { get; set; }
        [Column("updated_by")] public long? UpdatedBy { get; set; }
    }

    /// <summary>Which sections exist in which academic year (S2), with an optional per-year capacity (null = the section master's max_strength).</summary>
    [Table("tb_academic_year_sections", Schema = "public")]
    public class TbAcademicYearSection
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("year_section_id")] public long YearSectionId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("academic_year_id")] public long AcademicYearId { get; set; }
        [Column("class_id")] public long ClassId { get; set; }
        [Column("section_id")] public long SectionId { get; set; }
        [Column("max_strength")] public short? MaxStrength { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
        [Column("created_by")] public long? CreatedBy { get; set; }
        [Column("updated_by")] public long? UpdatedBy { get; set; }
    }
}
