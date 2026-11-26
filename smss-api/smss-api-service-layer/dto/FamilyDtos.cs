namespace smss_api_service_layer.dto
{
    // The read-only family screens. Deliberately small: a guardian never receives another guardian's contact details, internal remarks, a concession reason, a waived late fee, or another child's data.

    /// <summary>One card on the family screen: the child's own enrolment (the current year, or the latest one for a passed-out or not-yet-promoted child) and the status chips.</summary>
    public class FamilyChildCardDto
    {
        public string StudentId { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string AdmissionNumber { get; set; } = null!;
        public string StatusName { get; set; } = null!;
        public string? ProfilePhotoUrl { get; set; }
        public string? AcademicYearName { get; set; }
        public string? ClassName { get; set; }
        public string? SectionName { get; set; }
        public string? RollNumber { get; set; }
        /// <summary>PASSED_OUT, LEFT, TRANSFERRED_OUT (the child is read-only history) or PENDING_PROMOTION (still on last year's class); null for an ordinary current student.</summary>
        public string? ChipState { get; set; }
        public bool ReadOnly { get; set; }
        /// <summary>Today's mark (Present / Absent) or null when it has not been marked (or the child is not current).</summary>
        public string? AttendanceToday { get; set; }
        /// <summary>What is owed on instalments whose due date is today or earlier, in the child's own enrolment year. 0 = nothing due now.</summary>
        public decimal FeeDueNow { get; set; }
    }

    public class FamilyFeeChildDto
    {
        public string StudentId { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public decimal DueNow { get; set; }
    }

    /// <summary>"Paid" only when EVERY child has nothing due now; otherwise the total due now with the per-child breakdown (only the children who owe).</summary>
    public class FamilyFeeTileDto
    {
        public bool AllPaid { get; set; }
        public decimal TotalDueNow { get; set; }
        public List<FamilyFeeChildDto> Children { get; set; } = new();
    }

    public class FamilyChildrenDto
    {
        public List<FamilyChildCardDto> Children { get; set; } = new();
        public FamilyFeeTileDto FeeTile { get; set; } = new();
    }

    /// <summary>A child's profile as a parent sees it: no other guardian's contact details, no Aadhaar, no internal fields.</summary>
    public class FamilyChildProfileDto
    {
        public string StudentId { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string AdmissionNumber { get; set; } = null!;
        public string StatusName { get; set; } = null!;
        public string? ProfilePhotoUrl { get; set; }
        public string? AcademicYearName { get; set; }
        public string? ClassName { get; set; }
        public string? SectionName { get; set; }
        public string? RollNumber { get; set; }
        public string? GenderName { get; set; }
        public DateTime DateOfBirth { get; set; }
        public string? BloodGroupName { get; set; }
        public string? Address { get; set; }
    }

    public class FamilyExamDto
    {
        public string TermName { get; set; } = null!;
        public string ExamName { get; set; } = null!;
        public DateOnly? ExamDate { get; set; }
    }

    /// <summary>The exam dates of the schemes the child's CLASS is part of (never another class's dates).</summary>
    public class FamilyExamScheduleDto
    {
        public string SchemeName { get; set; } = null!;
        public string AcademicYearName { get; set; } = null!;
        public List<FamilyExamDto> Exams { get; set; } = new();
    }
}
