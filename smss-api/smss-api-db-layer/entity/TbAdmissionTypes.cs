using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    [Table("tb_admission_types", Schema = "public")]
    public class TbAdmissionTypes
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("admission_type_id")]
        public long AdmissionTypeId { get; set; }

        [Column("school_id")] public string SchoolId { get; set; } = null!;
        [Column("type_name")] public string TypeName { get; set; } = null!;
        [Column("description")] public string? Description { get; set; }
        [Column("status_id")] public int? StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
