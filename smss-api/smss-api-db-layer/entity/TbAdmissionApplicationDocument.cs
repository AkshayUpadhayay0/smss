using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>A file attached to an admission application. The file lives in the PRIVATE store (document_path is a key, never a URL); removing hides the row.</summary>
    [Table("tb_admission_application_documents", Schema = "public")]
    public class TbAdmissionApplicationDocument
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("application_document_id")]
        public long ApplicationDocumentId { get; set; }

        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("admission_id")] public long AdmissionId { get; set; }
        [Column("document_type_id")] public long DocumentTypeId { get; set; }
        [Column("document_path")] public string DocumentPath { get; set; } = null!;
        [Column("original_file_name")][StringLength(255)] public string OriginalFileName { get; set; } = null!;
        [Column("content_type")][StringLength(100)] public string ContentType { get; set; } = null!;
        [Column("file_size")] public long FileSize { get; set; }
        [Column("remarks")] public string? Remarks { get; set; }
        [Column("uploaded_by")] public long? UploadedBy { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
