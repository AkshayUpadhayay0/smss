using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>A school's own tax type (e.g. GST 5%), chosen by the accountant when collecting a fee payment. Never deleted: payments keep pointing at it.</summary>
    [Table("tb_tax_types", Schema = "public")]
    public class TbTaxType
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("tax_type_id")]
        public long TaxTypeId { get; set; }

        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("tax_name")][StringLength(100)] public string TaxName { get; set; } = null!;
        /// <summary>Stored upper-case; unique per school ignoring case.</summary>
        [Column("tax_code")][StringLength(30)] public string TaxCode { get; set; } = null!;
        [Column("rate_percent", TypeName = "numeric(5,2)")] public decimal RatePercent { get; set; }
        [Column("description")] public string? Description { get; set; }
        [Column("is_active")] public bool IsActive { get; set; } = true;
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
