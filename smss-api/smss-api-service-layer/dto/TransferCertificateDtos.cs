using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    /// <summary>What issuing a certificate for one student would do, and what stops it (read-only).</summary>
    public class TcCheckDto
    {
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public string AdmissionNumber { get; set; } = null!;
        public string? ClassName { get; set; }
        public string? SectionName { get; set; }
        public string YearName { get; set; } = null!;
        public string? Outcome { get; set; }
        public DateOnly LeavingDate { get; set; }
        public bool CanIssue { get; set; }
        public List<string> Blockers { get; set; } = new();
        /// <summary>Unpaid dues due on or before the leaving date.</summary>
        public decimal Outstanding { get; set; }
        public List<LeavingDueDto> Dues { get; set; } = new();
        public bool RequiresOverride { get; set; }
        public long? ExistingCertificateId { get; set; }
        public string? ExistingCertificateNumber { get; set; }
        public string SuggestedConduct { get; set; } = "Good";
        public string? PrincipalName { get; set; }
    }

    public class IssueTcRequest
    {
        [Required, StringLength(20)] public string StudentId { get; set; } = null!;
        /// <summary>Entered at issue; empty = Good.</summary>
        [StringLength(100)] public string? Conduct { get; set; }
        [StringLength(500)] public string? Remarks { get; set; }
        /// <summary>Must be true: a certificate number is used up for good.</summary>
        public bool Confirm { get; set; }
        public bool OverrideDues { get; set; }
        /// <summary>At least 10 characters when dues are overridden; stored on the certificate row (never printed).</summary>
        [StringLength(500)] public string? OverrideReason { get; set; }

        // Optional typed boxes. They are kept ONLY in this certificate's snapshot (never on the student record) and printed only when filled in.
        [StringLength(60)] public string? Nationality { get; set; }
        [StringLength(60)] public string? Religion { get; set; }
        [StringLength(60)] public string? Category { get; set; }
        /// <summary>Qualified for promotion to the higher class: true = yes, false = no, null = not stated.</summary>
        public bool? QualifiedForPromotion { get; set; }
        [StringLength(60)] public string? PromotedToClass { get; set; }
        [Range(0, 366)] public int? WorkingDays { get; set; }
        [Range(0, 366)] public int? DaysPresent { get; set; }
        [StringLength(200)] public string? FeeConcession { get; set; }
        public DateOnly? ApplicationDate { get; set; }
        /// <summary>The board's wording for general conduct, when it differs from the plain conduct above.</summary>
        [StringLength(200)] public string? BoardConduct { get; set; }
    }

    public class TcCopyDto
    {
        public string Kind { get; set; } = null!;
        public int CopyNo { get; set; }
        public DateTime PrintedAt { get; set; }
        public string? PrintedBy { get; set; }
    }

    public class TcDto
    {
        public long CertificateId { get; set; }
        public string CertificateNumber { get; set; } = null!;
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public string? ClassName { get; set; }
        public string? SectionName { get; set; }
        public string? Outcome { get; set; }
        /// <summary>ISSUED or VOID.</summary>
        public string Status { get; set; } = null!;
        public string Conduct { get; set; } = null!;
        public string? Remarks { get; set; }
        public bool FeesCleared { get; set; }
        public bool OverrideApplied { get; set; }
        public string? OverrideReason { get; set; }
        public decimal OutstandingAmount { get; set; }
        public DateTime IssuedAt { get; set; }
        public string? IssuedBy { get; set; }
        public DateTime? VoidedAt { get; set; }
        public string? VoidedBy { get; set; }
        public string? VoidReason { get; set; }
        public int Duplicates { get; set; }
        /// <summary>Only on the single-certificate read: every print, who and when.</summary>
        public List<TcCopyDto>? Copies { get; set; }
    }

    public class VoidTcRequest
    {
        /// <summary>At least 10 characters; stored on the row.</summary>
        [StringLength(500)] public string? Reason { get; set; }
        public bool Confirm { get; set; }
    }

    /// <summary>A PDF answer, or the reason there is none.</summary>
    public class TcFileResult
    {
        public int StatusCode { get; set; }
        public string Message { get; set; } = "";
        public byte[]? Content { get; set; }
        public string FileName { get; set; } = "";
    }
}
