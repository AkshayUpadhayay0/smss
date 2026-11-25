using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace smss_api_db_layer.entity
{
    /// <summary>An extra existing guardian ticked on an admission application (the sibling's other parent). Linked to the student when the application is confirmed.</summary>
    [Table("tb_admission_guardians", Schema = "public")]
    [PrimaryKey(nameof(AdmissionId), nameof(GuardianId))]
    public class TbAdmissionGuardian
    {
        [Column("admission_id")] public long AdmissionId { get; set; }
        [Column("guardian_id")] public long GuardianId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("relationship")][StringLength(30)] public string? Relationship { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
    }
}
