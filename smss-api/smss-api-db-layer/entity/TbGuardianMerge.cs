using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>The audit of one merge of two guardians: who, when, which survived, which links moved, which fields were copied, and a snapshot of the absorbed row (enough to reverse it by hand).</summary>
    [Table("tb_guardian_merges", Schema = "public")]
    public class TbGuardianMerge
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("merge_id")] public long MergeId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("survivor_id")] public long SurvivorId { get; set; }
        [Column("absorbed_id")] public long AbsorbedId { get; set; }
        [Column("merged_by")] public long? MergedBy { get; set; }
        [Column("merged_at")] public DateTime MergedAt { get; set; }
        [Column("moved_links", TypeName = "jsonb")] public string MovedLinks { get; set; } = "[]";
        [Column("copied_fields", TypeName = "jsonb")] public string CopiedFields { get; set; } = "{}";
        [Column("absorbed_snapshot", TypeName = "jsonb")] public string AbsorbedSnapshot { get; set; } = "{}";
    }
}
