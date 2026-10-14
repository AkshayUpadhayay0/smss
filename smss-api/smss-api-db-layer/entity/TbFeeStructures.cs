using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    // One header per class per academic year per school; the fee heads are in Details.
    [Table("tb_fee_structures", Schema = "public")]
    public class TbFeeStructures
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("fee_structure_id")]
        public long FeeStructureId { get; set; }

        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("academic_year_id")] public long AcademicYearId { get; set; }
        [Column("class_id")] public long ClassId { get; set; }
        [Column("structure_name")][StringLength(150)] public string StructureName { get; set; } = null!;
        [Column("isactive")] public bool IsActive { get; set; } = true;
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }

        public ICollection<TbFeeStructureDetails> Details { get; set; } = new List<TbFeeStructureDetails>();
    }
}
