using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>Global lookup: Present, Absent (Late / Half-Day later are just new rows).</summary>
    [Table("lut_attendance_status", Schema = "public")]
    public class LutAttendanceStatus
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("attendance_status_id")]
        public long AttendanceStatusId { get; set; }

        [Column("status_code")][StringLength(30)] public string StatusCode { get; set; } = null!;
        [Column("status_name")][StringLength(50)] public string StatusName { get; set; } = null!;
        [Column("is_active")] public bool IsActive { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
