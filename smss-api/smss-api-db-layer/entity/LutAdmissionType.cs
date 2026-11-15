using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>
    /// Global admission type (New Admission, Transfer, ...), managed by the Super Admin and shared by every school.
    /// Replaces the school-owned tb_admission_types (see Docs/smss-admission-type-global.sql).
    /// </summary>
    [Table("lut_admission_type", Schema = "public")]
    public class LutAdmissionType
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("admission_type_id")]
        public long AdmissionTypeId { get; set; }

        [Column("type_code")]
        [StringLength(30)]
        public string TypeCode { get; set; } = null!;

        [Column("type_name")]
        [StringLength(100)]
        public string TypeName { get; set; } = null!;

        [Column("description")]
        [StringLength(250)]
        public string? Description { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
}
