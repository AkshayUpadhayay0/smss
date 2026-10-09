using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    [Table("lut_religion_category", Schema = "public")]
    public class LutReligionCategory
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("religion_category_id")]
        public long ReligionCategoryId { get; set; }

        [Column("religion_code")]
        [StringLength(30)]
        public string ReligionCode { get; set; } = null!;

        [Column("religion_name")]
        [StringLength(100)]
        public string ReligionName { get; set; } = null!;

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
