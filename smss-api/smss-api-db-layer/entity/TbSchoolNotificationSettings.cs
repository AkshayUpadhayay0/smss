using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>A school's push-notification settings. One row per school (school_id is the key); no row means the defaults (reminders off).</summary>
    [Table("tb_school_notification_settings", Schema = "public")]
    public class TbSchoolNotificationSettings
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.None)][Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("fee_reminder_enabled")] public bool FeeReminderEnabled { get; set; }
        /// <summary>India wall-clock time.</summary>
        [Column("fee_reminder_send_time", TypeName = "time")] public TimeOnly FeeReminderSendTime { get; set; } = new(9, 0);
        [Column("fee_reminder_grace_days")] public short FeeReminderGraceDays { get; set; }
        /// <summary>India date of the last completed run.</summary>
        [Column("last_fee_reminder_run_date", TypeName = "date")] public DateOnly? LastFeeReminderRunDate { get; set; }
        [Column("updated_by")] public long? UpdatedBy { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
