using Microsoft.EntityFrameworkCore.Storage;

namespace smss_api_db_layer.@interface
{
    /// <summary>The student's latest enrolment and what "Record leaving" needs to know about it.</summary>
    public class LeavingContext
    {
        public string StudentName { get; set; } = null!;
        public string AdmissionNumber { get; set; } = null!;
        public bool StudentActive { get; set; }
        public long EnrollmentId { get; set; }
        public bool EnrolmentActive { get; set; }
        public long YearId { get; set; }
        public string YearName { get; set; } = null!;
        public DateOnly YearStart { get; set; }
        public DateOnly YearEnd { get; set; }
        public bool YearIsCurrent { get; set; }
        public bool YearClosed { get; set; }
        public DateOnly EnrollmentDate { get; set; }
        public string? Outcome { get; set; }
        public string? ClassName { get; set; }
        public string? SectionName { get; set; }
    }

    public class LeavingDueRow
    {
        public long FeeDueId { get; set; }
        public string FeeTypeName { get; set; } = null!;
        public DateOnly DueDate { get; set; }
        public decimal AmountDue { get; set; }
        public decimal AmountPaid { get; set; }
    }

    public record LeavingWrite(string SchoolId, string StudentId, long EnrollmentId, string Outcome, DateOnly LeavingDate, string Reason, long UserId, bool OverrideApplied, string? OverrideReason, decimal Outstanding);

    public class LeavingCorrectionContext
    {
        public string StudentName { get; set; } = null!;
        public long EnrollmentId { get; set; }
        public long YearId { get; set; }
        public string YearName { get; set; } = null!;
        public bool YearClosed { get; set; }
        public string? Outcome { get; set; }
        public DateOnly? LeavingDate { get; set; }
        public int LaterEnrolments { get; set; }
        /// <summary>The outcome was written by a promotion run (it has no leaving date): it is corrected on the promotion screen, not here.</summary>
        public bool FromPromotion { get; set; }
        public int Certificates { get; set; }
    }

    public class LeaverRow
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

    /// <summary>Record leaving (Stage 8a). Plain SQL, every statement filtered by the school id.</summary>
    public interface ILeavingRepository
    {
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        /// <summary>The student's most recent enrolment (by year start, then id); null when the student does not exist in this school or has no enrolment.</summary>
        Task<LeavingContext?> GetContextAsync(string schoolId, string studentId, CancellationToken ct);
        /// <summary>Unpaid dues of the enrolment's year (all of them; the service splits due-by-the-leaving-date from later).</summary>
        Task<List<LeavingDueRow>> ListUnpaidDuesAsync(string schoolId, string studentId, long yearId, CancellationToken ct);
        /// <summary>Writes the outcome and leaving columns (only while the enrolment has no outcome), revokes the student's refresh tokens and writes the audit row. False when the enrolment was not open.</summary>
        Task<bool> ApplyAsync(LeavingWrite w, DateTime nowUtc, CancellationToken ct);
        Task<LeavingCorrectionContext?> GetCorrectionContextAsync(string schoolId, string studentId, CancellationToken ct);
        Task<bool> ApplyCorrectionAsync(string schoolId, string studentId, long enrollmentId, string clearedOutcome, string reason, long userId, CancellationToken ct);
        Task<List<LeaverRow>> ListLeaversAsync(string schoolId, long? yearId, CancellationToken ct);
    }
}
