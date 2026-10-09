using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    [Table("tb_class_subjects", Schema = "public")]
    public class TbClassSubjects
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("class_subject_id")]
        public long ClassSubjectId { get; set; }

        [Column("school_id")] public string SchoolId { get; set; } = null!;
        [Column("class_id")] public long ClassId { get; set; }
        [Column("subject_id")] public long SubjectId { get; set; }
        [Column("status_id")] public int? StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    /// <summary>A mapping joined to its class and subject names, so screens need no extra lookups.</summary>
    public record ClassSubjectWithNames(TbClassSubjects Mapping, string ClassName, string SubjectName, string? SubjectCode);
}
