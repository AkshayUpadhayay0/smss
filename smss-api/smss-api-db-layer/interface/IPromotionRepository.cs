using Microsoft.EntityFrameworkCore.Storage;

namespace smss_api_db_layer.@interface
{
    public class PromoYearRow
    {
        public long YearId { get; set; }
        public string YearName { get; set; } = null!;
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public bool IsCurrent { get; set; }
        public bool IsClosed { get; set; }
        /// <summary>The year's own status is Active.</summary>
        public bool IsActive { get; set; }
    }

    public class PromoClassRow
    {
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public long? NextClassId { get; set; }
    }

    public class PromoSectionRow
    {
        public long SectionId { get; set; }
        public long ClassId { get; set; }
        public string SectionName { get; set; } = null!;
    }

    /// <summary>A student with an Active enrolment in the source year, in the class (or section) being promoted.</summary>
    public class PromoCandidateRow
    {
        public long EnrollmentId { get; set; }
        public string StudentId { get; set; } = null!;
        public string AdmissionNumber { get; set; } = null!;
        public string StudentName { get; set; } = null!;
        public long SectionId { get; set; }
        public string SectionName { get; set; } = null!;
        public string? RollNumber { get; set; }
        /// <summary>The outcome already recorded on this enrolment (PROMOTED, REPEATED, PASSED_OUT, TRANSFERRED_OUT, LEFT), or null.</summary>
        public string? Outcome { get; set; }
    }

    public class PromoTargetEnrollmentRow
    {
        public string StudentId { get; set; } = null!;
        public long EnrollmentId { get; set; }
        public long SectionId { get; set; }
    }

    /// <summary>The class teacher's decision on the report card (PROMOTED, NOT_PROMOTED, DETAINED) and the class they wrote down.</summary>
    public class PromoTeacherDecisionRow
    {
        public string StudentId { get; set; } = null!;
        public string Decision { get; set; } = null!;
        public string? PromotedTo { get; set; }
    }

    /// <summary>An unpaid due of the student in the SOURCE year (amount due above amount paid).</summary>
    public class PromoDueRow
    {
        public string StudentId { get; set; } = null!;
        public long FeeDueId { get; set; }
        public string FeeTypeName { get; set; } = null!;
        public DateOnly DueDate { get; set; }
        public decimal AmountDue { get; set; }
        public decimal AmountPaid { get; set; }
    }

    /// <summary>One section the target year offers (a class with no section still appears once, with SectionId null).</summary>
    public class PromoOfferRow
    {
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public int Sequence { get; set; }
        public long? SectionId { get; set; }
        public string? SectionName { get; set; }
        /// <summary>The target year's capacity of the section; null = no limit.</summary>
        public int? MaxStrength { get; set; }
        /// <summary>Active enrolments the section already has in the target year.</summary>
        public int Enrolled { get; set; }
        /// <summary>The class has an ACTIVE fee structure in the target year.</summary>
        public bool HasFeeStructure { get; set; }
    }

    public class PromoRollRow
    {
        public long SectionId { get; set; }
        public string RollNumber { get; set; } = null!;
    }

    /// <summary>One row of a promotion run's audit (tb_promotion_items).</summary>
    public class PromoItemWrite
    {
        public string StudentId { get; set; } = null!;
        public long SourceEnrollmentId { get; set; }
        public long? TargetEnrollmentId { get; set; }
        public string Decision { get; set; } = null!;
        public long FromClassId { get; set; }
        public long FromSectionId { get; set; }
        public long? ToClassId { get; set; }
        public long? ToSectionId { get; set; }
        public string? RollNumber { get; set; }
        public decimal OutstandingAmount { get; set; }
        public bool OverrideApplied { get; set; }
        public string? OverrideReason { get; set; }
        public string? SkipReason { get; set; }
    }

    /// <summary>One promotion run's audit row (tb_promotion_batches), written with its final counts.</summary>
    public class PromoBatchWrite
    {
        public string SchoolId { get; set; } = null!;
        public long SourceYearId { get; set; }
        public long TargetYearId { get; set; }
        public string Scope { get; set; } = null!;
        public long ClassId { get; set; }
        public long? SectionId { get; set; }
        public DateOnly PromotionDate { get; set; }
        public long CreatedBy { get; set; }
        public int StudentsTotal { get; set; }
        public int Promoted { get; set; }
        public int Repeated { get; set; }
        public int PassedOut { get; set; }
        public int Left { get; set; }
        public int Skipped { get; set; }
        public int Overrides { get; set; }
        public string? OverrideReason { get; set; }
        public bool NoFeeStructureAcknowledged { get; set; }
    }

    /// <summary>A student waiting to be promoted into the current-flagged year.</summary>
    public class PromoPendingRow
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
        /// <summary>Unpaid dues of the earlier year due on or before today.</summary>
        public decimal Outstanding { get; set; }
    }

    public class PromoPendingYears
    {
        public long CurrentYearId { get; set; }
        public string CurrentYearName { get; set; } = null!;
        /// <summary>The latest year starting before the current-flagged one; null when there is none.</summary>
        public long? EarlierYearId { get; set; }
        public string? EarlierYearName { get; set; }
    }

    public class PromoCount { public int Value { get; set; } }

    /// <summary>Everything the correction needs to know about one student's promotion out of a year.</summary>
    public class PromoCorrectionContext
    {
        public string StudentName { get; set; } = null!;
        public long SourceEnrollmentId { get; set; }
        public string SourceYearName { get; set; } = null!;
        public bool SourceYearClosed { get; set; }
        public string? Outcome { get; set; }
        /// <summary>The live (finished, not yet reversed) promotion item of that enrolment, or null when the outcome was not written by a promotion run.</summary>
        public long? ItemId { get; set; }
        public long? BatchId { get; set; }
        public string? Decision { get; set; }
        public long? TargetEnrollmentId { get; set; }
        public long? TargetYearId { get; set; }
        public string? TargetYearName { get; set; }
        public bool TargetYearClosed { get; set; }
        public long? TargetClassId { get; set; }
        public string? TargetClassName { get; set; }
        public long? TargetSectionId { get; set; }
        public string? TargetSectionName { get; set; }
        public string? TargetRoll { get; set; }
        public DateOnly? TargetEnrollmentDate { get; set; }
    }

    /// <summary>What exists in the NEW year for the student that a correction must not destroy.</summary>
    public class PromoCorrectionBlockers
    {
        public int Attendance { get; set; }
        public int PaymentAllocations { get; set; }
        public int PaidDues { get; set; }
        public int Marks { get; set; }
        public int Concessions { get; set; }
        public int Transport { get; set; }
    }

    public class PromoFeeRemoval
    {
        public int FeeItems { get; set; }
        public int Dues { get; set; }
        public decimal DuesAmount { get; set; }
        public bool FeeStructure { get; set; }
    }

    public class PromoCorrectionWrite
    {
        public string SchoolId { get; set; } = null!;
        public string StudentId { get; set; } = null!;
        public long BatchId { get; set; }
        public long ItemId { get; set; }
        public long SourceEnrollmentId { get; set; }
        public long? RemovedEnrollmentId { get; set; }
        public string Decision { get; set; } = null!;
        public string OutcomeCleared { get; set; } = null!;
        public string RemovedJson { get; set; } = "{}";
        public string Reason { get; set; } = null!;
        public long CorrectedBy { get; set; }
    }

    /// <summary>Promotion, read side (4b-2). Every method takes the school id; nothing here writes.</summary>
    public interface IPromotionRepository
    {
        Task<PromoYearRow?> GetYearAsync(string schoolId, long yearId, CancellationToken ct);
        Task<List<PromoClassRow>> ListClassesAsync(string schoolId, CancellationToken ct);
        Task<List<PromoSectionRow>> ListSectionsAsync(string schoolId, long classId, CancellationToken ct);
        Task<List<PromoCandidateRow>> ListCandidatesAsync(string schoolId, long sourceYearId, long classId, long? sectionId, CancellationToken ct);
        Task<List<PromoTargetEnrollmentRow>> ListTargetEnrollmentsAsync(string schoolId, long targetYearId, IReadOnlyCollection<string> studentIds, CancellationToken ct);
        Task<List<PromoTeacherDecisionRow>> ListTeacherDecisionsAsync(string schoolId, long sourceYearId, IReadOnlyCollection<string> studentIds, CancellationToken ct);
        Task<List<PromoDueRow>> ListUnpaidDuesAsync(string schoolId, long sourceYearId, IReadOnlyCollection<string> studentIds, CancellationToken ct);
        Task<List<PromoOfferRow>> ListTargetOfferAsync(string schoolId, long targetYearId, CancellationToken ct);
        Task<List<PromoRollRow>> ListTargetRollsAsync(string schoolId, long targetYearId, CancellationToken ct);

        // ---- 4b-3: the run (everything below must be called inside the transaction taken with BeginTransactionAsync, after LockSchoolAsync)
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        /// <summary>Takes the school's promotion lock for the rest of the transaction: a second run waits here and then sees what the first one did.</summary>
        Task LockSchoolAsync(string schoolId, CancellationToken ct);
        /// <summary>Adds the new-year enrolment (Active, admission type "existing student") and returns its id.</summary>
        Task<long> InsertEnrollmentAsync(string schoolId, string studentId, long yearId, long sectionId, string? roll, DateOnly enrollmentDate, long sourceEnrollmentId, CancellationToken ct);
        /// <summary>Records the outcome on the old enrolment (only while it has none) and nothing else on it. Returns false when it already had one.</summary>
        Task<bool> SetOutcomeAsync(string schoolId, long enrollmentId, string outcome, CancellationToken ct);
        Task<long> InsertBatchAsync(PromoBatchWrite batch, CancellationToken ct);
        Task InsertItemAsync(string schoolId, long batchId, PromoItemWrite item, CancellationToken ct);

        // ---- pending promotion (read)
        Task<PromoPendingYears?> GetPendingYearsAsync(string schoolId, CancellationToken ct);
        Task<int> CountPendingAsync(string schoolId, DateOnly today, CancellationToken ct);
        Task<List<PromoPendingRow>> ListPendingAsync(string schoolId, DateOnly today, int limit, CancellationToken ct);

        // ---- 4b-4: the correction of ONE promotion (read side, then the writes inside BeginTransactionAsync + LockSchoolAsync)
        Task<PromoCorrectionContext?> GetCorrectionContextAsync(string schoolId, string studentId, long sourceYearId, CancellationToken ct);
        Task<PromoCorrectionBlockers> GetCorrectionBlockersAsync(string schoolId, string studentId, long targetYearId, CancellationToken ct);
        Task<List<string>> ListConcessionNamesAsync(string schoolId, string studentId, long targetYearId, CancellationToken ct);
        /// <summary>The new year's unpaid fee items, dues and fee structure assignment of the student (counts only).</summary>
        Task<PromoFeeRemoval> CountFeesAsync(string schoolId, string studentId, long yearId, CancellationToken ct);
        /// <summary>Removes the student's fee items, their dues and the fee structure assignment for the year (the caller has checked that nothing was paid). Returns what it removed.</summary>
        Task<PromoFeeRemoval> RemoveFeesAsync(string schoolId, string studentId, long yearId, CancellationToken ct);
        /// <summary>Marks the item reversed and detaches it from the enrolment that is about to be removed.</summary>
        Task ReverseItemAsync(string schoolId, long itemId, CancellationToken ct);
        Task<bool> DeleteEnrollmentAsync(string schoolId, long enrollmentId, CancellationToken ct);
        /// <summary>Clears outcome and outcome_at of the old enrolment (only while it has the expected outcome).</summary>
        Task<bool> ClearOutcomeAsync(string schoolId, long enrollmentId, string expectedOutcome, CancellationToken ct);
        Task<long> InsertCorrectionAsync(PromoCorrectionWrite c, CancellationToken ct);
    }
}
