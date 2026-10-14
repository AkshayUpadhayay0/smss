using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    [Table("tb_fee_concessions", Schema = "public")]
    public class TbFeeConcessions
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("concession_id")]
        public long ConcessionId { get; set; }

        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("concession_name")][StringLength(100)] public string ConcessionName { get; set; } = null!;
        [Column("concession_code")][StringLength(30)] public string ConcessionCode { get; set; } = null!;
        /// <summary>PERCENTAGE or FIXED.</summary>
        [Column("concession_type")][StringLength(20)] public string ConcessionType { get; set; } = null!;
        [Column("value", TypeName = "numeric(10,2)")] public decimal Value { get; set; }
        /// <summary>Null = applies to the whole structure, not one fee head.</summary>
        [Column("applicable_fee_type_id")] public long? ApplicableFeeTypeId { get; set; }
        [Column("isactive")] public bool IsActive { get; set; } = true;
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
