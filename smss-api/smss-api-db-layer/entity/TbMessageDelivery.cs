using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>
    /// One message attempt to a parent / student login (WhatsApp or email). It never holds a token, a link, a password or the message text: only which template, to whom (masked), through which sender and what happened.
    /// Status: LOGGED (the log-only sender wrote it, nothing left the server), SENT, DELIVERED, READ, FAILED (always with an error code), SKIPPED (no number / address to send to).
    /// </summary>
    [Table("tb_message_deliveries", Schema = "public")]
    public class TbMessageDelivery
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("delivery_id")] public long DeliveryId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("guardian_id")] public long? GuardianId { get; set; }
        [Column("user_id")] public long? UserId { get; set; }
        [Column("template_code")][StringLength(50)] public string TemplateCode { get; set; } = null!;
        [Column("purpose")][StringLength(30)] public string Purpose { get; set; } = null!;
        [Column("channel")][StringLength(10)] public string Channel { get; set; } = null!;
        [Column("recipient_masked")][StringLength(60)] public string? RecipientMasked { get; set; }
        [Column("status")][StringLength(10)] public string Status { get; set; } = null!;
        [Column("sender_kind")][StringLength(10)] public string SenderKind { get; set; } = null!;
        [Column("sender_scope")][StringLength(10)] public string? SenderScope { get; set; }
        [Column("provider_message_id")][StringLength(200)] public string? ProviderMessageId { get; set; }
        [Column("attempts")] public int Attempts { get; set; } = 1;
        [Column("error_code")][StringLength(40)] public string? ErrorCode { get; set; }
        [Column("error_message")][StringLength(300)] public string? ErrorMessage { get; set; }
        [Column("resend_of_delivery_id")] public long? ResendOfDeliveryId { get; set; }
        [Column("created_by")] public long? CreatedBy { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
        [Column("sent_at")] public DateTime? SentAt { get; set; }
        [Column("delivered_at")] public DateTime? DeliveredAt { get; set; }
    }
}
