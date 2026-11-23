using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>One SLOT of a bell schedule (S4): a numbered PERIOD, or a BREAK (no number, a label). Not academic-year-scoped.</summary>
    public static class SlotTypes { public const string Period = "PERIOD"; public const string Break = "BREAK"; }

    [Table("tb_periods", Schema = "public")]
    public class TbPeriod
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("period_id")]
        public long PeriodId { get; set; }

        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        /// <summary>Null for a break.</summary>
        [Column("period_number")] public short? PeriodNumber { get; set; }
        [Column("schedule_id")] public long ScheduleId { get; set; }
        /// <summary>PERIOD or BREAK. A break can never hold a timetable entry (the database refuses it).</summary>
        [Column("slot_type")][StringLength(8)] public string SlotType { get; set; } = SlotTypes.Period;
        [Column("slot_label")][StringLength(30)] public string? SlotLabel { get; set; }
        /// <summary>Display order inside the schedule (kept in clock-time order by the service).</summary>
        [Column("slot_order")] public short SlotOrder { get; set; }
        [Column("start_time", TypeName = "time")] public TimeOnly StartTime { get; set; }
        [Column("end_time", TypeName = "time")] public TimeOnly EndTime { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
