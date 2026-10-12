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
        [Column("occupation")] public string? Occupation { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
