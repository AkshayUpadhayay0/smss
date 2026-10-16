using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    [Table("tb_fee_structure_details", Schema = "public")]
    public class TbFeeStructureDetails
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("fee_structure_detail_id")]
        public long FeeStructureDetailId { get; set; }

        [Column("fee_structure_id")] public long FeeStructureId { get; set; }
        [ForeignKey(nameof(FeeStructureId))] public TbFeeStructures? FeeStructure { get; set; }

        [Column("fee_type_id")] public long FeeTypeId { get; set; }
        [Column("frequency_id")] public long FrequencyId { get; set; }
        [Column("amount", TypeName = "numeric(12,2)")] public decimal Amount { get; set; }
        /// <summary>Day of the month a recurring fee head falls due (1-31); null for One Time / Annual.</summary>
        [Column("due_day")] public short? DueDay { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
