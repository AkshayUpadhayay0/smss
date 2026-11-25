using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>
    /// A student's document of one type: either SUBMITTED (a file in the PRIVATE store; document_path is a key, never a URL) or WAIVED (a reason, who and when, no file). PENDING is never stored: it is
    /// "a required type with no active row". Voiding hides a row (voided_*), nothing is deleted; there is ONE active (not voided) row per student and type (partial unique index).
    /// </summary>
    [Table("tb_student_documents", Schema = "public")]
    public class TbStudentDocument
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("student_document_id")]
        public long StudentDocumentId { get; set; }

        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("student_id")][StringLength(20)] public string StudentId { get; set; } = null!;
        [Column("document_type_id")] public long DocumentTypeId { get; set; }
        [Column("status")][StringLength(10)] public string Status { get; set; } = null!;   // SUBMITTED | WAIVED
        [Column("document_path")] public string? DocumentPath { get; set; }
        [Column("original_file_name")][StringLength(255)] public string? OriginalFileName { get; set; }
        [Column("content_type")][StringLength(100)] public string? ContentType { get; set; }
        [Column("file_size")] public long? FileSize { get; set; }
        [Column("remarks")] public string? Remarks { get; set; }
        [Column("waiver_reason")] public string? WaiverReason { get; set; }
        [Column("waived_by")] public long? WaivedBy { get; set; }
        [Column("waived_at")] public DateTime? WaivedAt { get; set; }
        /// <summary>The application document this row was carried over from at confirmation (unique: a carry-over never runs twice).</summary>
        [Column("source_application_document_id")] public long? SourceApplicationDocumentId { get; set; }
        [Column("voided_at")] public DateTime? VoidedAt { get; set; }
        [Column("voided_by")] public long? VoidedBy { get; set; }
        [Column("void_reason")] public string? VoidReason { get; set; }
        [Column("created_by")] public long? CreatedBy { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
