using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace smss_api_db_layer.entity
{
    [Table("lut_school_level", Schema = "public")]
    public class LutSchoolLevel
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("school_level_id")]
        public long SchoolLevelId { get; set; }

        [Column("school_level_code")]
        [StringLength(50)]
        public string SchoolLevelCode { get; set; } = null!;

        [Column("school_level_name")]
        [StringLength(150)]
        public string SchoolLevelName { get; set; } = null!;

        [Column("description")]
        public string? Description { get; set; }

        [Column("isactive")]
        public bool IsActive { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
}
