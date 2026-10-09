using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    [Table("lut_blood_group", Schema = "public")]
    public class LutBloodGroup
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("blood_group_id")]
        public long BloodGroupId { get; set; }

        [Column("blood_group_code")]
        [StringLength(10)]
        public string BloodGroupCode { get; set; } = null!;

        [Column("blood_group_name")]
        [StringLength(10)]
        public string BloodGroupName { get; set; } = null!;

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
