namespace smss_api_service_layer.dto
{
    /// <summary>Names of the dashboard parts that could not be loaded: the client shows "Unavailable" for them instead of a wrong number.</summary>
    public static class SchoolDashboardParts
    {
        public const string Students = "students";
        public const string Employees = "employees";
        public const string Attendance = "attendance";
        public const string Fees = "fees";
        public const string Admissions = "admissions";
        public const string EmployeeLeave = "employeeLeave";
        public const string StudentLeave = "studentLeave";
        public const string ClassTeachers = "classTeachers";
        public const string Events = "events";
        public const string Notices = "notices";
    }

    public class SchoolDashboardStatsDto
    {
        /// <summary>Students on an Active roster (Active enrollment of an Active student) of the school's active sections in the current academic year.</summary>
        public int? TotalStudents { get; set; }
        /// <summary>Employees with an Active record (the Employees list's Active filter).</summary>
        public int? TotalEmployees { get; set; }
        /// <summary>Marks written today, and how many are Present. Both are 0 and the percentage null while nothing is marked yet.</summary>
        public int? AttendanceMarkedToday { get; set; }
        public int? AttendancePresentToday { get; set; }
        /// <summary>Present / marked x 100, one decimal; null when nothing has been marked today.</summary>
        public decimal? AttendancePercentToday { get; set; }
        public int? SectionsMarkedToday { get; set; }
        public int? SectionsTotal { get; set; }
        /// <summary>Money received in the current India calendar month (Fee dashboard's collection-by-month figure for this month).</summary>
        public decimal? FeeCollectedThisMonth { get; set; }
        /// <summary>The Fee dashboard's outstanding figure for the current academic year.</summary>
        public decimal? FeeOutstanding { get; set; }
        /// <summary>Applications that are Submitted or Under Review.</summary>
        public int? PendingAdmissions { get; set; }
    }

    public class AttendanceTrendPointDto
    {
        public DateOnly Date { get; set; }
        public int Marked { get; set; }
        public int Present { get; set; }
        /// <summary>Present / marked x 100, one decimal; null for a day nothing was marked (a Sunday, a holiday, or today before marking).</summary>
        public decimal? PresentPercent { get; set; }
    }

    public class SchoolDashboardAlertsDto
    {
        public int? PendingEmployeeLeaves { get; set; }
        public int? PendingStudentLeaves { get; set; }
        /// <summary>Active sections of the current academic year that have no class teacher.</summary>
        public int? SectionsWithoutClassTeacher { get; set; }
        public int? FeeOverdueCount { get; set; }
        public decimal? FeeOverdueAmount { get; set; }
    }

    public class DashboardNoticeDto
    {
        public long NoticeId { get; set; }
        public string Title { get; set; } = null!;
        public string Audience { get; set; } = null!;
        public string? CreatedByName { get; set; }
        public DateTime PublishedAt { get; set; }
    }

    public class DashboardAdmissionDto
    {
        public long AdmissionId { get; set; }
        public string ApplicationNumber { get; set; } = null!;
        public string ApplicantFullName { get; set; } = null!;
        public string? AppliedClassName { get; set; }
        /// <summary>The admission status code, e.g. APPLICATION_SUBMITTED / UNDER_REVIEW / APPROVED.</summary>
        public string StatusName { get; set; } = null!;
        public DateTime ApplicationDate { get; set; }
    }

    /// <summary>The School Admin dashboard in one response. Every number is null when its part is listed in PartsUnavailable.</summary>
    public class SchoolDashboardDto
    {
        public DateOnly Today { get; set; }
        /// <summary>The academic year that contains today; null when none does (the year-based parts are then unavailable).</summary>
        public long? AcademicYearId { get; set; }
        public string? AcademicYearName { get; set; }
        public SchoolDashboardStatsDto Stats { get; set; } = new();
        /// <summary>The last 14 calendar days, oldest first, today last. Always 14 entries.</summary>
        public List<AttendanceTrendPointDto> AttendanceTrend { get; set; } = new();
        /// <summary>The last 6 calendar months, oldest first, this month last, zero-filled. Empty when the fees part is unavailable.</summary>
        public List<FeeCollectionMonthDto> FeeCollectionTrend { get; set; } = new();
        public SchoolDashboardAlertsDto Alerts { get; set; } = new();
        public List<SchoolEventDto> UpcomingEvents { get; set; } = new();
        public List<DashboardNoticeDto> RecentNotices { get; set; } = new();
        public List<DashboardAdmissionDto> RecentAdmissions { get; set; } = new();
        public List<string> PartsUnavailable { get; set; } = new();
    }
}
