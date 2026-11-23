using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>S7: one row per REAL "copy configuration from another year" run (a preview writes nothing).</summary>
    [Table("tb_year_copy_log", Schema = "public")]
    public class TbYearCopyLog
    {
        public const string Completed = "COMPLETED";
        public const string Failed = "FAILED";

        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("copy_id")] public long CopyId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("source_year_id")] public long SourceYearId { get; set; }
        [Column("target_year_id")] public long TargetYearId { get; set; }
        /// <summary>JSON array of the parts selected.</summary>
        [Column("parts", TypeName = "jsonb")] public string Parts { get; set; } = "[]";
        /// <summary>JSON: per part, created / skipped-existing / skipped-with-reason counts.</summary>
        [Column("result", TypeName = "jsonb")] public string Result { get; set; } = "{}";
        [Column("status")][StringLength(10)] public string Status { get; set; } = Completed;
        [Column("error_note")][StringLength(250)] public string? ErrorNote { get; set; }
        [Column("run_by")] public long? RunBy { get; set; }
        [Column("started_at")] public DateTime StartedAt { get; set; }
        [Column("finished_at")] public DateTime FinishedAt { get; set; }
    }
}
