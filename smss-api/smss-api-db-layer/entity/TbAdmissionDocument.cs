using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace smss_api_db_layer.entity
{
    [Table("tb_admission_documents", Schema = "public")]
    public class TbAdmissionDocument
    {
        [Key][Column("admission_document_id")] public long AdmissionDocumentId { get; set; }
        [Column("admission_id")] public long AdmissionId { get; set; }
        [Column("document_type_id")] public long DocumentTypeId { get; set; }
        [Column("document_url")] public string DocumentUrl { get; set; } = null!;
        [Column("is_verified")] public bool IsVerified { get; set; }
        [Column("verified_by")] public long? VerifiedBy { get; set; }
        [Column("verified_at")] public DateTime? VerifiedAt { get; set; }
        [Column("remarks")] public string? Remarks { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
