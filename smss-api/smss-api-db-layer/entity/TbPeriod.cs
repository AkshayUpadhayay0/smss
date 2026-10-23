using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>One period of the school's bell schedule (not academic-year-scoped).</summary>
    [Table("tb_periods", Schema = "public")]
    public class TbPeriod
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("period_id")]
        public long PeriodId { get; set; }

        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("period_number")] public short PeriodNumber { get; set; }
        [Column("start_time", TypeName = "time")] public TimeOnly StartTime { get; set; }
        [Column("end_time", TypeName = "time")] public TimeOnly EndTime { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
