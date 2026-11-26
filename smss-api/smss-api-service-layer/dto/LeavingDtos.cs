using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    public class LeavingDueDto
    {
        public long FeeDueId { get; set; }
        public string FeeType { get; set; } = null!;
        public DateOnly DueDate { get; set; }
        public decimal AmountDue { get; set; }
        public decimal AmountPaid { get; set; }
        public decimal Outstanding { get; set; }
    }

    /// <summary>What "Record leaving" would do for one student, and what stops it (read-only).</summary>
    public class LeavingCheckDto
    {
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public string AdmissionNumber { get; set; } = null!;
        public string? ClassName { get; set; }
        public string? SectionName { get; set; }
        public string YearName { get; set; } = null!;
        public DateOnly EnrollmentDate { get; set; }
        public DateOnly LeavingDate { get; set; }
        public bool CanRecord { get; set; }
        public List<string> Blockers { get; set; } = new();
        /// <summary>Unpaid dues due on or before the leaving date (the block, same rule as promotion).</summary>
        public decimal Outstanding { get; set; }
        public List<LeavingDueDto> Dues { get; set; } = new();
        /// <summary>Unpaid dues falling due after the leaving date (information only).</summary>
        public decimal NotYetDue { get; set; }
        /// <summary>True when Outstanding is above zero: the admin must tick the override and give a reason of at least 10 characters.</summary>
        public bool RequiresOverride { get; set; }
    }

    public class RecordLeavingRequest
    {
        [Required, StringLength(20)] public string StudentId { get; set; } = null!;
        /// <summary>LEFT or TRANSFERRED_OUT.</summary>
        [Required, StringLength(20)] public string Type { get; set; } = null!;
        /// <summary>Not in the future, not before the enrolment date. Empty = today (school time).</summary>
        public DateOnly? LeavingDate { get; set; }
        [Required, StringLength(500)] public string Reason { get; set; } = null!;
        /// <summary>Must be true: leaving closes the student's login at once.</summary>
        public bool Confirm { get; set; }
        /// <summary>Required when there are unpaid dues up to the leaving date.</summary>
        public bool OverrideDues { get; set; }
        /// <summary>At least 10 characters when dues are overridden; stored in the audit.</summary>
        [StringLength(500)] public string? OverrideReason { get; set; }
    }

    public class RecordLeavingResultDto
    {
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public string Outcome { get; set; } = null!;
        public DateOnly LeavingDate { get; set; }
        public decimal Outstanding { get; set; }
        public bool OverrideApplied { get; set; }
    }

    public class LeavingCorrectionCheckDto
    {
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public string YearName { get; set; } = null!;
        public string? Outcome { get; set; }
        public DateOnly? LeavingDate { get; set; }
        public bool CanCorrect { get; set; }
        public List<string> Blockers { get; set; } = new();
    }

    public class LeavingCorrectionRequest
    {
        [Required, StringLength(20)] public string StudentId { get; set; } = null!;
        /// <summary>At least 10 characters; stored in the audit.</summary>
        [StringLength(500)] public string? Reason { get; set; }
        public bool Confirm { get; set; }
    }

    public class LeaverDto
    {
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public string AdmissionNumber { get; set; } = null!;
        public string? ClassName { get; set; }
        public string? SectionName { get; set; }
        public string Outcome { get; set; } = null!;
        public DateOnly? LeavingDate { get; set; }
        public string? Reason { get; set; }
        public decimal? Outstanding { get; set; }
        public bool Overridden { get; set; }
        public string? RecordedBy { get; set; }
        public string YearName { get; set; } = null!;
    }
}
