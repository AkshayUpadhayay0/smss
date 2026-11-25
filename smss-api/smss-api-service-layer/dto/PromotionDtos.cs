using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    /// <summary>One edit the admin made on a row of the promotion screen. Anything not listed keeps the prefilled value.</summary>
    public class PromotionDecisionInput
    {
        [Required, StringLength(20)] public string StudentId { get; set; } = null!;
        /// <summary>PROMOTE, REPEAT, PASS_OUT or LEAVE.</summary>
        [StringLength(10)] public string? Decision { get; set; }
        public long? ToClassId { get; set; }
        public long? ToSectionId { get; set; }
        /// <summary>A roll number typed by the admin for the new section; blank = the next free number.</summary>
        [StringLength(30)] public string? RollNumber { get; set; }
    }

    /// <summary>A promotion run of ONE class (all its sections) or ONE section, from one year into a later one. The school is never part of it: it comes from the token.</summary>
    public class PromotionPreviewRequest
    {
        [Range(1, long.MaxValue)] public long SourceYearId { get; set; }
        [Range(1, long.MaxValue)] public long TargetYearId { get; set; }
        [Range(1, long.MaxValue)] public long ClassId { get; set; }
        /// <summary>Null = every section of the class.</summary>
        public long? SectionId { get; set; }
        /// <summary>Also the dues cutoff: dues with a due date on or before it count. Null = today.</summary>
        public DateOnly? PromotionDate { get; set; }
        [MaxLength(500)] public List<PromotionDecisionInput>? Decisions { get; set; }
    }

    public class PromotionTargetDto
    {
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public long? SectionId { get; set; }
        public string? SectionName { get; set; }
    }

    public class PromotionDueDto
    {
        public long FeeDueId { get; set; }
        public string FeeType { get; set; } = null!;
        public DateOnly DueDate { get; set; }
        public decimal AmountDue { get; set; }
        public decimal AmountPaid { get; set; }
        public decimal Outstanding { get; set; }
    }

    public static class PromotionRowStatus
    {
        /// <summary>Will be done by a run.</summary>
        public const string Ready = "READY";
        /// <summary>Unpaid dues up to the promotion date: left out unless a School Admin or Principal overrides with a reason.</summary>
        public const string Blocked = "BLOCKED";
        /// <summary>The choice cannot be carried out as it stands (see Issues).</summary>
        public const string NeedsAttention = "NEEDS_ATTENTION";
        /// <summary>No decision from the teacher and none from the admin yet: left out of the run.</summary>
        public const string NoDecision = "NO_DECISION";
        /// <summary>Already has an enrolment in the target year: skipped, never changed.</summary>
        public const string AlreadyEnrolled = "ALREADY_ENROLLED";
        /// <summary>The enrolment already has an outcome (passed out, left, transferred): skipped.</summary>
        public const string AlreadyRecorded = "ALREADY_RECORDED";
    }

    public class PromotionRowDto
    {
        public string StudentId { get; set; } = null!;
        public string AdmissionNumber { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public long SourceEnrollmentId { get; set; }
        public long SourceSectionId { get; set; }
        public string SourceSectionName { get; set; } = null!;
        public string? SourceRollNumber { get; set; }
        /// <summary>What the class teacher recorded on the report card (PROMOTED, NOT_PROMOTED, DETAINED), or null.</summary>
        public string? TeacherDecision { get; set; }
        /// <summary>The decision in force: PROMOTE, REPEAT, PASS_OUT, LEAVE, or null (no decision yet).</summary>
        public string? Decision { get; set; }
        /// <summary>TEACHER (prefilled), ADMIN (chosen on the screen) or NONE.</summary>
        public string DecisionSource { get; set; } = "NONE";
        /// <summary>Where the student would go for the decision in force (null for pass out, leave or no decision).</summary>
        public PromotionTargetDto? Target { get; set; }
        /// <summary>The defaults, so choosing Promote or Repeat on the screen needs no round trip: the next class with the same-named section, and the same class with the same-named section.</summary>
        public PromotionTargetDto? PromoteDefault { get; set; }
        public PromotionTargetDto? RepeatDefault { get; set; }
        public string? RollNumber { get; set; }
        public string Status { get; set; } = PromotionRowStatus.NoDecision;
        public bool BlockedByDues { get; set; }
        /// <summary>Unpaid dues of the source year with a due date on or before the promotion date.</summary>
        public decimal Outstanding { get; set; }
        /// <summary>Unpaid dues falling due AFTER the promotion date: shown for information, never a block.</summary>
        public decimal NotYetDue { get; set; }
        public List<PromotionDueDto> Dues { get; set; } = new();
        /// <summary>Why the choice cannot be carried out, in plain words.</summary>
        public List<string> Issues { get; set; } = new();
        /// <summary>Things to know that do not stop the run (for example the teacher wrote a different class than the next one).</summary>
        public List<string> Notes { get; set; } = new();
    }

    public class PromotionSectionOptionDto
    {
        public long SectionId { get; set; }
        public string SectionName { get; set; } = null!;
        public int? MaxStrength { get; set; }
        public int Enrolled { get; set; }
    }

    public class PromotionClassOptionDto
    {
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public bool HasFeeStructure { get; set; }
        public List<PromotionSectionOptionDto> Sections { get; set; } = new();
    }

    public class PromotionCountsDto
    {
        public int Total { get; set; }
        public int Ready { get; set; }
        public int Blocked { get; set; }
        public int NeedsAttention { get; set; }
        public int NoDecision { get; set; }
        public int AlreadyEnrolled { get; set; }
        public int AlreadyRecorded { get; set; }
    }

    /// <summary>What a promotion run WOULD do. Nothing has been written.</summary>
    public class PromotionPreviewDto
    {
        public long SourceYearId { get; set; }
        public string SourceYearName { get; set; } = null!;
        public bool SourceYearClosed { get; set; }
        public long TargetYearId { get; set; }
        public string TargetYearName { get; set; } = null!;
        /// <summary>The target year's first day and whether it has already passed: then a run charges fees from that day, needs the back-dating confirmation and a reason.</summary>
        public DateOnly TargetYearStart { get; set; }
        public bool TargetYearStarted { get; set; }
        public string Scope { get; set; } = "CLASS";
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public long? SectionId { get; set; }
        public string? SectionName { get; set; }
        public DateOnly PromotionDate { get; set; }
        /// <summary>The class's next class (the default for Promote), or null when this is the last class.</summary>
        public long? NextClassId { get; set; }
        public string? NextClassName { get; set; }
        public bool IsLastClass { get; set; }
        public int MaxPerRun { get; set; }
        public PromotionCountsDto Counts { get; set; } = new();
        /// <summary>Target classes (of the rows that would be placed) that have no active fee structure in the target year. A run needs one acknowledgement tick for these; it never blocks a school that does not use fees.</summary>
        public List<string> ClassesWithoutFeeStructure { get; set; } = new();
        public bool NeedsFeeAcknowledgement { get; set; }
        /// <summary>The classes and sections the target year offers, with their capacity and how many are enrolled already.</summary>
        public List<PromotionClassOptionDto> TargetOptions { get; set; } = new();
        public List<PromotionRowDto> Rows { get; set; } = new();
    }

    /// <summary>
    /// Runs a promotion. The decisions, targets and roll numbers are the admin's choices; EVERYTHING else (dues, offered class and section, capacity, who is already enrolled, the years) is worked out again
    /// by the server at this moment, so nothing from an earlier preview is trusted.
    /// </summary>
    public class PromotionExecuteRequest : PromotionPreviewRequest
    {
        /// <summary>Must be true: the run saves changes.</summary>
        public bool Confirm { get; set; }
        /// <summary>One reason for the whole run (at least 10 characters): needed when any blocked student is overridden, and when the target year has already started (it is the back-dating reason).</summary>
        [StringLength(500)] public string? OverrideReason { get; set; }
        /// <summary>The blocked students the admin ticked one by one. A student who is not blocked at this moment is ignored.</summary>
        [MaxLength(500)] public List<string>? OverrideStudentIds { get; set; }
        /// <summary>The tick the preview asks for when a target class has no fee structure in the target year.</summary>
        public bool AcknowledgeNoFeeStructure { get; set; }
        /// <summary>The same confirmation as "Assign fees": needed when the target year has already started (dues dated before today are created and logged).</summary>
        public bool ConfirmBackdating { get; set; }
    }

    public static class PromotionItemOutcome
    {
        public const string Done = "DONE";
        public const string Overridden = "OVERRIDDEN";
        public const string Skipped = "SKIPPED";
    }

    public class PromotionResultItemDto
    {
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public string? Decision { get; set; }
        /// <summary>DONE, OVERRIDDEN (done although dues were unpaid) or SKIPPED.</summary>
        public string Outcome { get; set; } = PromotionItemOutcome.Skipped;
        public string? Reason { get; set; }
        public PromotionTargetDto? Target { get; set; }
        public string? RollNumber { get; set; }
        public decimal Outstanding { get; set; }
    }

    public class PromotionFeeFailureDto
    {
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public string Reason { get; set; } = null!;
    }

    public class PromotionResultDto
    {
        /// <summary>The audit batch; null when nothing was done (then nothing at all was written).</summary>
        public long? BatchId { get; set; }
        public int Promoted { get; set; }
        public int Repeated { get; set; }
        public int PassedOut { get; set; }
        public int Left { get; set; }
        public int Overridden { get; set; }
        public int Skipped { get; set; }
        /// <summary>Students with no decision yet: left out, not part of the audit.</summary>
        public int LeftOut { get; set; }
        public int FeesAssigned { get; set; }
        /// <summary>Students placed in a class that has no active fee structure (acknowledged): no fees were assigned.</summary>
        public int FeesNoStructure { get; set; }
        public bool Backdated { get; set; }
        public List<PromotionResultItemDto> Items { get; set; } = new();
        /// <summary>Students who were promoted but whose fees could not be assigned: assign them from "Assign fees for a year" or the student's Fees tab. They are never undone.</summary>
        public List<PromotionFeeFailureDto> FeeFailures { get; set; } = new();
    }

    public class PromotionPendingRowDto
    {
        public string StudentId { get; set; } = null!;
        public string AdmissionNumber { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public long SectionId { get; set; }
        public string SectionName { get; set; } = null!;
        public string? RollNumber { get; set; }
        public string? TeacherDecision { get; set; }
        public decimal Outstanding { get; set; }
        /// <summary>BLOCKED (unpaid dues up to today) or NO_DECISION (not promoted yet).</summary>
        public string Status { get; set; } = "NO_DECISION";
    }

    /// <summary>Students enrolled in the year before the current-flagged year with no enrolment in the current one.</summary>
    public class PromotionPendingDto
    {
        /// <summary>False when the school has neither a next year to promote into nor an earlier year to promote from (or no current-flagged year): the tile is hidden.</summary>
        public bool HasPair { get; set; }
        /// <summary>The year the waiting students are in now, and the year they wait to be promoted into.</summary>
        public string? SourceYearName { get; set; }
        public string? TargetYearName { get; set; }
        public long? SourceYearId { get; set; }
        public long? TargetYearId { get; set; }
        /// <summary>True when the target is the current-flagged year (the year-end switch already happened); false when it is the next year being prepared.</summary>
        public bool TargetIsCurrent { get; set; }
        public int Total { get; set; }
        public int Blocked { get; set; }
        public List<PromotionPendingRowDto> Rows { get; set; } = new();
    }

    // ====================================================================== the correction of one promotion (4b-4)
    /// <summary>Which promotion to correct: the student and the year they were promoted OUT of (their old-year enrolment carries the outcome).</summary>
    public class PromotionCorrectionRequest
    {
        [Required, StringLength(20)] public string StudentId { get; set; } = null!;
        [Range(1, long.MaxValue)] public long SourceYearId { get; set; }
        /// <summary>At least 10 characters; stored in the audit.</summary>
        [StringLength(500)] public string? Reason { get; set; }
        /// <summary>Must be true: the correction removes the new-year enrolment.</summary>
        public bool Confirm { get; set; }
    }

    public class PromotionCorrectionRemovalDto
    {
        public string? NewYearName { get; set; }
        public string? ClassName { get; set; }
        public string? SectionName { get; set; }
        public string? RollNumber { get; set; }
        /// <summary>True when there is a new-year enrolment to remove (promote and repeat); false after pass out or leave.</summary>
        public bool Enrolment { get; set; }
        public int FeeItems { get; set; }
        public int Dues { get; set; }
        public decimal DuesAmount { get; set; }
        public bool FeeStructure { get; set; }
    }

    /// <summary>What a correction would do for one student, or plainly why it cannot be done.</summary>
    public class PromotionCorrectionCheckDto
    {
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public string SourceYearName { get; set; } = null!;
        public string? Decision { get; set; }
        public string? Outcome { get; set; }
        public bool CanCorrect { get; set; }
        /// <summary>Plain-words reasons the correction is refused (empty when it can be done).</summary>
        public List<string> Blockers { get; set; } = new();
        public PromotionCorrectionRemovalDto WillRemove { get; set; } = new();
    }

    public class PromotionCorrectionResultDto
    {
        public long CorrectionId { get; set; }
        public string StudentId { get; set; } = null!;
        public string OutcomeCleared { get; set; } = null!;
        public PromotionCorrectionRemovalDto Removed { get; set; } = new();
    }
}
