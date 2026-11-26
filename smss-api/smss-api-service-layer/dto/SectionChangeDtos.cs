using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    public class SectionTargetDto
    {
        public long SectionId { get; set; }
        public string SectionName { get; set; } = null!;
        public int? MaxStrength { get; set; }
        public int Enrolled { get; set; }
        public bool IsFull { get; set; }
        /// <summary>The roll number the student would get if none is typed.</summary>
        public string NextRoll { get; set; } = "1";
    }

    /// <summary>What a section change would do for one student, and what stops it (read-only).</summary>
    public class SectionChangeCheckDto
    {
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public string AdmissionNumber { get; set; } = null!;
        public string ClassName { get; set; } = null!;
        public string YearName { get; set; } = null!;
        public long CurrentSectionId { get; set; }
        public string CurrentSectionName { get; set; } = null!;
        public string? CurrentRoll { get; set; }
        public bool CanChange { get; set; }
        public List<string> Blockers { get; set; } = new();
        public List<SectionTargetDto> Targets { get; set; } = new();
    }

    public class ChangeSectionRequest
    {
        [Required, StringLength(20)] public string StudentId { get; set; } = null!;
        [Range(1, long.MaxValue)] public long ToSectionId { get; set; }
        /// <summary>Not in the future, not before the enrolment date. Empty = today (school time).</summary>
        public DateOnly? EffectiveDate { get; set; }
        /// <summary>At least 10 characters; stored in the history.</summary>
        [StringLength(500)] public string? Reason { get; set; }
        /// <summary>A typed roll number for the new section (unique there); blank = the next free number.</summary>
        [StringLength(30)] public string? RollNumber { get; set; }
        public bool Confirm { get; set; }
    }

    public class SectionChangeResultDto
    {
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public string FromSection { get; set; } = null!;
        public string ToSection { get; set; } = null!;
        public string? FromRoll { get; set; }
        public string? ToRoll { get; set; }
        public DateOnly EffectiveDate { get; set; }
    }

    public class SectionChangeHistoryDto
    {
        public long ChangeId { get; set; }
        public string YearName { get; set; } = null!;
        public string FromSection { get; set; } = null!;
        public string ToSection { get; set; } = null!;
        public string? FromRoll { get; set; }
        public string? ToRoll { get; set; }
        public DateOnly EffectiveDate { get; set; }
        public string Reason { get; set; } = null!;
        public string? ChangedBy { get; set; }
        public DateTime ChangedAt { get; set; }
    }
}
