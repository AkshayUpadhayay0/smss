using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>One push per student, type and India day (UNIQUE): the audit trail and the idempotency record of the daily job.</summary>
    [Table("tb_push_notification_log", Schema = "public")]
    public class TbPushNotificationLog
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("notification_id")] public long NotificationId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("user_id")] public long? UserId { get; set; }
        [Column("student_id")][StringLength(20)] public string? StudentId { get; set; }
        [Column("notification_type")][StringLength(30)] public string NotificationType { get; set; } = null!;
        [Column("notify_date", TypeName = "date")] public DateOnly NotifyDate { get; set; }
        [Column("title")][StringLength(100)] public string Title { get; set; } = null!;
        [Column("body")][StringLength(300)] public string Body { get; set; } = null!;
        /// <summary>PENDING | SENT | FAILED | NO_DEVICE</summary>
        [Column("status")][StringLength(20)] public string Status { get; set; } = "PENDING";
        [Column("attempt_count")] public short AttemptCount { get; set; }
        [Column("devices_targeted")] public short DevicesTargeted { get; set; }
        [Column("devices_succeeded")] public short DevicesSucceeded { get; set; }
        [Column("error_summary")][StringLength(500)] public string? ErrorSummary { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("sent_at")] public DateTime? SentAt { get; set; }
    }
}
