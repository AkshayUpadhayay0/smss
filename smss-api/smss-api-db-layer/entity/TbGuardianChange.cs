using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>One change to a guardian's mobile number, WhatsApp number or email: who, when, the old and the new value, and whether it was a single edit, a bulk change or a merge.</summary>
    [Table("tb_guardian_changes", Schema = "public")]
    public class TbGuardianChange
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("change_id")] public long ChangeId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("guardian_id")] public long GuardianId { get; set; }
        /// <summary>mobile_number | whatsapp_number | email</summary>
        [Column("field_name")][StringLength(20)] public string FieldName { get; set; } = null!;
        [Column("old_value")][StringLength(150)] public string? OldValue { get; set; }
        [Column("new_value")][StringLength(150)] public string? NewValue { get; set; }
        /// <summary>EDIT | BULK | MERGE</summary>
        [Column("source")][StringLength(10)] public string Source { get; set; } = null!;
        [Column("changed_by")] public long? ChangedBy { get; set; }
        [Column("changed_at")] public DateTime ChangedAt { get; set; }
    }
}
