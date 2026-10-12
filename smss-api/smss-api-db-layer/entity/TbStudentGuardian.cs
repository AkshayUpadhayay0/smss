using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace smss_api_db_layer.entity
{
    [Table("tb_student_guardians", Schema = "public")]
    public class TbStudentGuardian
    {
        [Key][Column("student_guardian_id")] public long StudentGuardianId { get; set; }
        [Column("student_id")] public long StudentId { get; set; }
        [Column("guardian_id")] public long GuardianId { get; set; }
        [Column("relationship")] public string Relationship { get; set; } = null!;
        [Column("is_primary")] public bool IsPrimary { get; set; }
        [Column("is_emergency_contact")] public bool IsEmergencyContact { get; set; }
        [Column("can_pickup_student")] public bool CanPickupStudent { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
