using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    /// <summary>One required document type for one student and where it stands: PENDING (nothing yet; derived, never stored), SUBMITTED or WAIVED.</summary>
    public class StudentRequiredDocumentDto
    {
        public long DocumentTypeId { get; set; }
        public string DocumentTypeName { get; set; } = null!;
        public string DocumentTypeCode { get; set; } = null!;
        /// <summary>PENDING / SUBMITTED / WAIVED.</summary>
        public string Status { get; set; } = null!;
        public long? StudentDocumentId { get; set; }
    }

    /// <summary>A stored document row (SUBMITTED file or WAIVED reason). A Teacher gets the type and status only: no file name, remarks or waiver reason.</summary>
    public class StudentDocumentDto
    {
        public long StudentDocumentId { get; set; }
        public long DocumentTypeId { get; set; }
        public string DocumentTypeName { get; set; } = null!;
        /// <summary>SUBMITTED or WAIVED.</summary>
        public string Status { get; set; } = null!;
        public string? FileName { get; set; }
        public string? ContentType { get; set; }
        public long? FileSize { get; set; }
        public string? Remarks { get; set; }
        public string? WaiverReason { get; set; }
        /// <summary>Who uploaded the file, or who waived it.</summary>
        public string? ByName { get; set; }
        public DateTime At { get; set; }
        /// <summary>True when the row was carried over from the admission application at confirmation.</summary>
        public bool FromApplication { get; set; }
        public bool Voided { get; set; }
        public DateTime? VoidedAt { get; set; }
        public string? VoidReason { get; set; }
    }

    public class StudentDocumentsDto
    {
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        /// <summary>The required types that apply to this student (admitted on or after the type's required-from date), each with its status.</summary>
        public List<StudentRequiredDocumentDto> Required { get; set; } = new();
        public int PendingCount { get; set; }
        /// <summary>Every active document of the student, required or not (newest first).</summary>
        public List<StudentDocumentDto> Documents { get; set; } = new();
        /// <summary>Upload, waive and void: School Admin, Principal, Admission Staff. A Teacher (the class teacher) only reads the statuses.</summary>
        public bool CanManage { get; set; }
    }

    public class PendingStudentDocumentsRowDto
    {
        public string StudentId { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string AdmissionNumber { get; set; } = null!;
        public string? ClassName { get; set; }
        public string? SectionName { get; set; }
        public List<string> Missing { get; set; } = new();
    }

    public class PendingStudentDocumentsDto
    {
        /// <summary>Students missing at least one required document (the count the dashboards show, before any filter).</summary>
        public int TotalStudents { get; set; }
        public List<PendingStudentDocumentsRowDto> Students { get; set; } = new();
    }

    public class WaiveStudentDocumentRequestDto
    {
        [Required] public long DocumentTypeId { get; set; }
        [MaxLength(500)] public string? Reason { get; set; }
    }

    public class VoidStudentDocumentRequestDto
    {
        [MaxLength(500)] public string? Reason { get; set; }
    }
}
