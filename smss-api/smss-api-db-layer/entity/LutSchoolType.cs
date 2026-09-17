using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace smss_api_db_layer.entity
{
    [Table("lut_school_type", Schema = "public")]
    public class LutSchoolType
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("school_type_id")]
        public long SchoolTypeId { get; set; }

        [Column("school_type_code")]
        [StringLength(50)]
        public string SchoolTypeCode { get; set; } = null!;

        [Column("school_type_name")]
        [StringLength(150)]
        public string SchoolTypeName { get; set; } = null!;

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
