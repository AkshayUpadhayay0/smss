using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>
    /// The school's WhatsApp sender: its number, the Meta phone-number id, and the status NOT_REGISTERED / PENDING / ACTIVE. Messages go from the platform number until ACTIVE. A school with no row counts as NOT_REGISTERED.
    /// A row is ACTIVE only with a phone-number id and a verified_at (database check).
    /// </summary>
    [Table("tb_school_whatsapp_senders", Schema = "public")]
    public class TbSchoolWhatsAppSender
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("sender_id")] public long SenderId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("whatsapp_number")][StringLength(20)] public string? WhatsAppNumber { get; set; }
        [Column("phone_number_id")][StringLength(50)] public string? PhoneNumberId { get; set; }
        [Column("status")][StringLength(15)] public string Status { get; set; } = "NOT_REGISTERED";
        [Column("verified_at")] public DateTime? VerifiedAt { get; set; }
        [Column("updated_by")] public long? UpdatedBy { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
