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
}
