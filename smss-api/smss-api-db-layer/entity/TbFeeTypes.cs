using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    // School-owned master (Docs/roles-workflow.md section 27). Active/inactive is the isactive boolean, not lut_status.
    [Table("tb_fee_types", Schema = "public")]
    public class TbFeeTypes
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("fee_type_id")]
        public long FeeTypeId { get; set; }

        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("fee_type_name")][StringLength(100)] public string FeeTypeName { get; set; } = null!;
        [Column("fee_type_code")][StringLength(30)] public string FeeTypeCode { get; set; } = null!;
        [Column("description")] public string? Description { get; set; }
        [Column("isactive")] public bool IsActive { get; set; } = true;
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
