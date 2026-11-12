using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>One admin-initiated password reset. Deliberately has no password column of any kind.</summary>
    [Table("tb_password_reset_audit", Schema = "public")]
    public class TbPasswordResetAudit
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("audit_id")] public long AuditId { get; set; }
        [Column("school_id")][StringLength(100)] public string? SchoolId { get; set; }
        [Column("target_user_id")] public long TargetUserId { get; set; }
        [Column("target_login_id")][StringLength(50)] public string TargetLoginId { get; set; } = null!;
        [Column("reset_by_user_id")] public long ResetByUserId { get; set; }
        [Column("reason")][StringLength(500)] public string Reason { get; set; } = null!;
        [Column("reset_at")] public DateTime ResetAt { get; set; }
    }
}
