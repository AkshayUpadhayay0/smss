namespace smss_api_service_layer.dto
{
    /// <summary>One calendar day of the monthly attendance report grid.</summary>
    public class AttendanceReportDayDto
    {
        public DateOnly Date { get; set; }
        /// <summary>1 = Monday ... 7 = Sunday (the same numbering as the Timetable).</summary>
        public int DayOfWeek { get; set; }
        /// <summary>False for a Sunday, a day of a Holiday calendar event, or a day outside the academic year: such a day is greyed and counts for nothing.</summary>
        public bool IsWorkingDay { get; set; }
        /// <summary>"Sunday", "Holiday" or "Outside academic year"; null on a working day.</summary>
        public string? NonWorkingReason { get; set; }
        /// <summary>The Holiday event's title (several are joined with ", "); null unless the day is a holiday.</summary>
        public string? HolidayName { get; set; }
        /// <summary>After today (the school's date): nothing can be marked yet, so it is never "not marked".</summary>
        public bool IsFuture { get; set; }
        /// <summary>How many students of the section have a mark that day (any student, including one who has left).</summary>
        public int MarkedCount { get; set; }
    }

    public class AttendanceReportStudentDto
    {
        public string StudentId { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string? RollNumber { get; set; }
        /// <summary>False for a student who has left (no longer Active) but still has marks that month.</summary>
        public bool IsActive { get; set; }
        /// <summary>The first day this student counts for (their enrolment date); null when they were enrolled before the month.</summary>
        public DateOnly? EnrolledFrom { get; set; }
        /// <summary>One entry per day of the month, same order as Days: Present, Absent, NotMarked, NotEnrolled (before the enrolment date) or NonWorking (a non-working day with no mark).</summary>
        public List<string> Marks { get; set; } = new();
        public int PresentDays { get; set; }
        public int AbsentDays { get; set; }
        /// <summary>Working days up to today, on or after enrolment, with no mark for this student.</summary>
        public int NotMarkedDays { get; set; }
        /// <summary>Present / (Present + Absent) x 100, one decimal, over marked working days only; null when none is marked.</summary>
        public decimal? Percentage { get; set; }
        public bool BelowThreshold { get; set; }
    }

    public class AttendanceReportSummaryDto
    {
        public int StudentCount { get; set; }
        /// <summary>The mean of the students' percentages (students with at least one marked day), one decimal; null when nobody has one.</summary>
        public decimal? AveragePercentage { get; set; }
        public int StudentsBelowThreshold { get; set; }
        public decimal ThresholdPercentage { get; set; }
        /// <summary>Working days of the month up to today.</summary>
        public int WorkingDays { get; set; }
        /// <summary>Working days up to today on which the section has no mark at all: the class teacher missed them.</summary>
        public int DaysNotMarked { get; set; }
        public List<DateOnly> NotMarkedDates { get; set; } = new();
    }

    public class MonthlyAttendanceReportDto
    {
        public long SectionId { get; set; }
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public string SectionName { get; set; } = null!;
        public long AcademicYearId { get; set; }
        public string AcademicYearName { get; set; } = null!;
        public int Year { get; set; }
        public int Month { get; set; }
        public string MonthName { get; set; } = null!;
        public DateOnly Today { get; set; }
        public List<AttendanceReportDayDto> Days { get; set; } = new();
        public List<AttendanceReportStudentDto> Students { get; set; } = new();
        public AttendanceReportSummaryDto Summary { get; set; } = new();
    }

    public class AttendanceReportYearOptionDto
    {
        public long AcademicYearId { get; set; }
        public string YearName { get; set; } = null!;
        public bool IsCurrent { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
    }

    public class AttendanceReportSectionOptionDto
    {
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public long SectionId { get; set; }
        public string SectionName { get; set; } = null!;
    }

    /// <summary>What the report screen's pickers offer the caller: the academic years, and the sections they may report on for the chosen year (every active section for a School Admin / Principal, only their own for a class teacher).</summary>
    public class AttendanceReportOptionsDto
    {
        public long AcademicYearId { get; set; }
        public List<AttendanceReportYearOptionDto> Years { get; set; } = new();
        public List<AttendanceReportSectionOptionDto> Sections { get; set; } = new();
        /// <summary>True when the caller sees the whole school (School Admin, Principal).</summary>
        public bool AllSections { get; set; }
    }

    public class AttendanceReportFileDto
    {
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public string FileName { get; set; } = null!;
    }
}
