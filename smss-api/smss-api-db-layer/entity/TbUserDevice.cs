using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>One app install that can receive pushes. fcm_token is UNIQUE: a token belongs to one user at a time (a shared phone re-assigns it on sign-in).</summary>
    [Table("tb_user_devices", Schema = "public")]
    public class TbUserDevice
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("device_id")] public long DeviceId { get; set; }
        [Column("user_id")] public long UserId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("fcm_token")][StringLength(1024)] public string FcmToken { get; set; } = null!;
        [Column("platform")][StringLength(10)] public string Platform { get; set; } = null!;
        [Column("device_label")][StringLength(100)] public string? DeviceLabel { get; set; }
        [Column("app_version")][StringLength(30)] public string? AppVersion { get; set; }
        [Column("is_active")] public bool IsActive { get; set; } = true;
        [Column("last_seen_at")] public DateTime LastSeenAt { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
