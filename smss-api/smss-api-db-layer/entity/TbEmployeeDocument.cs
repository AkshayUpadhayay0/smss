using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>An uploaded employee document. The file lives in the PRIVATE store (document_path is a storage key, not a URL) and is only served by an authenticated endpoint.</summary>
    [Table("tb_employee_documents", Schema = "public")]
    public class TbEmployeeDocument
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("employee_document_id")]
        public long EmployeeDocumentId { get; set; }

        [Column("employee_id")][StringLength(20)] public string EmployeeId { get; set; } = null!;
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
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
