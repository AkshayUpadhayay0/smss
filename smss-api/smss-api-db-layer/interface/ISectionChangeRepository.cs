using Microsoft.EntityFrameworkCore.Storage;

namespace smss_api_db_layer.@interface
{
    /// <summary>The student's latest enrolment and what a mid-year section change needs to know about it.</summary>
    public class SecContext
    {
        public string StudentName { get; set; } = null!;
        public string AdmissionNumber { get; set; } = null!;
        public bool StudentActive { get; set; }
        public long EnrollmentId { get; set; }
        public bool EnrolmentActive { get; set; }
        public string? Outcome { get; set; }
        public long YearId { get; set; }
        public string YearName { get; set; } = null!;
        public bool YearIsCurrent { get; set; }
        public bool YearClosed { get; set; }
        public DateOnly EnrollmentDate { get; set; }
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public long SectionId { get; set; }
        public string SectionName { get; set; } = null!;
        public string? RollNumber { get; set; }
        /// <summary>Published report cards of this student in this year (the current PUBLISHED version only).</summary>
        public int PublishedCards { get; set; }
    }

    /// <summary>A section of the same class that the same academic year offers.</summary>
    public class SecTargetRow
    {
        public long SectionId { get; set; }
        public string SectionName { get; set; } = null!;
        public int? MaxStrength { get; set; }
        /// <summary>Live enrolments in it now (the shared live-enrolment rule).</summary>
        public int Enrolled { get; set; }
    }

    public record SecChangeWrite(string SchoolId, string StudentId, long EnrollmentId, long YearId, long FromSectionId, long ToSectionId, string? FromRoll, string? ToRoll, DateOnly EffectiveDate, string Reason, long UserId);

    public class SecHistoryRow
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

    /// <summary>Mid-year section change (Stage 8c). Plain SQL, every statement filtered by the school id.</summary>
    public interface ISectionChangeRepository
    {
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        /// <summary>One section change at a time per school (a transaction-scoped advisory lock), so a capacity or roll check cannot be raced.</summary>
        Task LockAsync(string schoolId, CancellationToken ct);
        Task<SecContext?> GetContextAsync(string schoolId, string studentId, CancellationToken ct);
        /// <summary>The OTHER sections of the class that the year offers (active), with their strength and live count.</summary>
        Task<List<SecTargetRow>> ListTargetsAsync(string schoolId, long yearId, long classId, long exceptSectionId, CancellationToken ct);
        /// <summary>Every roll number in the section and year (any enrolment: a leaver's roll is still taken), for the uniqueness check and the next free number.</summary>
        Task<List<string?>> ListRollsAsync(string schoolId, long yearId, long sectionId, CancellationToken ct);
        /// <summary>Moves the single enrolment (section and roll) and writes the history row. False when the enrolment was no longer in the from section.</summary>
        Task<bool> ApplyAsync(SecChangeWrite w, CancellationToken ct);
        Task<List<SecHistoryRow>> ListHistoryAsync(string schoolId, string studentId, CancellationToken ct);
    }
}
