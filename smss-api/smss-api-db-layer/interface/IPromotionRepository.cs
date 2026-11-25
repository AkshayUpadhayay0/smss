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
    }
}
