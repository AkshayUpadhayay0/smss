using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>S3: for ONE section in ONE year, ADD a subject its class does not have or REMOVE one its class has. The class mapping itself is untouched.</summary>
    [Table("tb_section_subject_overrides", Schema = "public")]
    public class TbSectionSubjectOverride
    {
        public const string Add = "ADD";
        public const string Remove = "REMOVE";

        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("override_id")] public long OverrideId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("academic_year_id")] public long AcademicYearId { get; set; }
        [Column("class_id")] public long ClassId { get; set; }
        [Column("section_id")] public long SectionId { get; set; }
        [Column("subject_id")] public long SubjectId { get; set; }
        [Column("action")][StringLength(6)] public string Action { get; set; } = null!;
        [Column("status_id")] public int StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
        [Column("created_by")] public long? CreatedBy { get; set; }
        [Column("updated_by")] public long? UpdatedBy { get; set; }
    }

    /// <summary>One row of vw_section_effective_subjects: a subject a section is taught in a year (class mapping - REMOVE + ADD). Read-only; THE definition every reader uses.</summary>
    public class SectionEffectiveSubject
    {
        [Column("school_id")] public string SchoolId { get; set; } = null!;
        [Column("academic_year_id")] public long AcademicYearId { get; set; }
        [Column("class_id")] public long ClassId { get; set; }
        [Column("section_id")] public long SectionId { get; set; }
        [Column("subject_id")] public long SubjectId { get; set; }
        /// <summary>CLASS (from the class mapping) or ADD (an override).</summary>
        [Column("source")] public string Source { get; set; } = null!;
    }
}
