using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    [Table("lut_gender", Schema = "public")]
    public class LutGender
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("gender_id")]
        public long GenderId { get; set; }

        [Column("gender_code")]
        [StringLength(20)]
        public string GenderCode { get; set; } = null!;

        [Column("gender_name")]
        [StringLength(50)]
        public string GenderName { get; set; } = null!;

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
