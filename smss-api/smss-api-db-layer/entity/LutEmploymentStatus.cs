using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>Global lookup of an employee's real-world status (Active, On Leave, Resigned, Terminated, Retired). Independent of the row's Active / Inactive flag.</summary>
    [Table("lut_employment_status", Schema = "public")]
    public class LutEmploymentStatus
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("employment_status_id")]
        public long EmploymentStatusId { get; set; }

        [Column("status_code")][StringLength(30)] public string StatusCode { get; set; } = null!;
        [Column("status_name")][StringLength(50)] public string StatusName { get; set; } = null!;
        [Column("description")][StringLength(250)] public string? Description { get; set; }
        [Column("is_active")] public bool IsActive { get; set; }
        /// <summary>True for Resigned / Terminated / Retired: an employee in this status cannot sign in, whatever their Active / Inactive flag says.</summary>
        [Column("blocks_login")] public bool BlocksLogin { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
