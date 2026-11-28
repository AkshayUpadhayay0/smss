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
        /// <summary>Unpaid from years before the year shown for this child (derived; nothing is carried forward).</summary>
        public decimal FeeEarlierYears { get; set; }
    }

    public class FamilyFeeChildDto
    {
        public string StudentId { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public decimal DueNow { get; set; }
        /// <summary>Still unpaid from years BEFORE the one shown for this child (derived; nothing is carried forward). Shown beside the year's own amount.</summary>
        public decimal EarlierYears { get; set; }
    }

    /// <summary>"Paid" only when EVERY child has nothing due now; otherwise the total due now with the per-child breakdown (only the children who owe).</summary>
    public class FamilyFeeTileDto
    {
        public bool AllPaid { get; set; }
        public decimal TotalDueNow { get; set; }
        /// <summary>The children's unpaid amounts from earlier years, added up. AllPaid is false while this is above zero.</summary>
        public decimal TotalEarlierYears { get; set; }
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

    /// <summary>A notice as a parent sees it: the words and the date, never who wrote it as an id and never the audience tier.</summary>
    public class FamilyNoticeDto
    {
        public long NoticeId { get; set; }
        public string Title { get; set; } = null!;
        public string Body { get; set; } = null!;
        public DateTime PublishedAt { get; set; }
        public bool IsRead { get; set; }
        public string? PostedBy { get; set; }
    }

    /// <summary>A school calendar event as a parent sees it (read only).</summary>
    public class FamilyEventDto
    {
        public long EventId { get; set; }
        public string Title { get; set; } = null!;
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public bool IsAllDay { get; set; }
        public TimeOnly? StartTime { get; set; }
        public TimeOnly? EndTime { get; set; }
        public string CategoryName { get; set; } = null!;
        public string ColorHex { get; set; } = null!;
        public string? Description { get; set; }
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
