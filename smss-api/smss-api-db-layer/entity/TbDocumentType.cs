using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    [Table("tb_document_types", Schema = "public")]
    public class TbDocumentType
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("document_type_id")]
        public long DocumentTypeId { get; set; }

        [Column("school_id")]
        public string SchoolId { get; set; } = null!;

        [Column("document_name")]
        public string DocumentName { get; set; } = null!;

        [Column("document_code")]
        public string DocumentCode { get; set; } = null!;

        [Column("applies_to")]
        public string AppliesTo { get; set; } = null!; // STUDENT / EMPLOYEE / BOTH

        [Column("is_required")]
        public bool IsRequired { get; set; }

        [Column("status_id")]
        public int StatusId { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
}
