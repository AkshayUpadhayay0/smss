using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace smss_api_db_layer.entity
{
    [Table("tb_guardians", Schema = "public")]
    public class TbGuardian
    {
        [Key][Column("guardian_id")] public long GuardianId { get; set; }
        [Column("school_id")] public string SchoolId { get; set; } = null!;
        [Column("first_name")] public string FirstName { get; set; } = null!;
        [Column("last_name")] public string? LastName { get; set; }
        [Column("relationship")] public string Relationship { get; set; } = null!;
        [Column("mobile_number")] public string MobileNumber { get; set; } = null!;
        [Column("email")] public string? Email { get; set; }
        /// <summary>The guardian's WhatsApp number WITH the country code ("+919876543210"); null for guardians added before it was collected.</summary>
        [Column("whatsapp_number")] public string? WhatsAppNumber { get; set; }
        [Column("occupation")] public string? Occupation { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        /// <summary>The guardian's login (tb_users), one per guardian; null until a parent account exists (a later stage).</summary>
        [Column("user_id")] public long? UserId { get; set; }
        /// <summary>Set when this guardian was merged into another (the survivor). A merged guardian is Inactive and is never linked to anyone again.</summary>
        [Column("merged_into_guardian_id")] public long? MergedIntoGuardianId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
