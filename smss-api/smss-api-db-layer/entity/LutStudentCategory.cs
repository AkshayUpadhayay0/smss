using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    [Table("lut_student_category", Schema = "public")]
    public class LutStudentCategory
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("student_category_id")]
        public long StudentCategoryId { get; set; }

        [Column("category_code")]
        [StringLength(30)]
        public string CategoryCode { get; set; } = null!;

        [Column("category_name")]
        [StringLength(100)]
        public string CategoryName { get; set; } = null!;

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
