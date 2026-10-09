using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    [Table("tb_classes", Schema = "public")]
    public class TbClasses
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("class_id")]
        public long ClassId { get; set; }

        [Column("school_id")] public string SchoolId { get; set; } = null!;
        [Column("class_name")] public string ClassName { get; set; } = null!;
        [Column("class_code")] public string? ClassCode { get; set; }
        [Column("sequence_order")] public short SequenceOrder { get; set; }
        [Column("status_id")] public int? StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
