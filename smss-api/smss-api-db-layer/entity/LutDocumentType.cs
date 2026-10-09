using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    [Table("lut_document_type", Schema = "public")]
    public class LutDocumentType
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("document_type_id")]
        public long DocumentTypeId { get; set; }

        [Column("document_type_code")]
        [StringLength(30)]
        public string DocumentTypeCode { get; set; } = null!;

        [Column("document_type_name")]
        [StringLength(100)]
        public string DocumentTypeName { get; set; } = null!;

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
