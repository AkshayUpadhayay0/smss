namespace smss_api_service_layer.dto
{
    /// <summary>Names of the Principal dashboard parts that could not be loaded: the client shows "Unavailable" for them, never a wrong 0.</summary>
    public static class PrincipalDashboardParts
    {
        public const string Students = "students";
        public const string Staff = "staff";
        public const string Attendance = "attendance";
        public const string Fees = "fees";
        public const string EmployeeLeave = "employeeLeave";
        public const string Admissions = "admissions";
        public const string Results = "results";
        public const string Events = "events";
        public const string Notices = "notices";
    }

    public class PrincipalDashboardStatsDto
    {
        /// <summary>Students on an Active roster of the school's active sections in the current academic year.</summary>
        public int? TotalStudents { get; set; }
        /// <summary>Employees with an Active record.</summary>
        public int? TotalStaff { get; set; }
        public int? AttendanceMarkedToday { get; set; }
        public int? AttendancePresentToday { get; set; }
        /// <summary>Present / marked x 100, one decimal; null when nothing has been marked today.</summary>
        public decimal? AttendancePercentToday { get; set; }
        /// <summary>The Fee dashboard's figures for the current academic year, read-only (the Principal has no drill-down or collection rights).</summary>
        public decimal? FeeTotalDue { get; set; }
        public decimal? FeeCollected { get; set; }
        public decimal? FeeOutstanding { get; set; }
    }

    public class PrincipalDashboardAlertsDto
    {
        /// <summary>Employee leave requests waiting for a School Admin / Principal decision.</summary>
        public int? PendingEmployeeLeaves { get; set; }
        /// <summary>Applications Under Review: the ones the Principal can approve or reject.</summary>
        public int? AdmissionsToDecide { get; set; }
        /// <summary>Applications just Submitted, not yet taken into review.</summary>
        public int? AdmissionsAwaitingReview { get; set; }
    }

    /// <summary>One published result set (a section's report cards for a term or the year).</summary>
    public class DashboardPublicationDto
    {
        public long PublicationId { get; set; }
        public string ClassName { get; set; } = null!;
        public string SectionName { get; set; } = null!;
        /// <summary>TERM or YEAR.</summary>
        public string Scope { get; set; } = null!;
        public string? TermName { get; set; }
        public int Version { get; set; }
        public string Status { get; set; } = null!;
        public DateTime PublishedAt { get; set; }
        public int StudentCount { get; set; }
    }

    public class PrincipalDashboardDto
    {
        public DateOnly Today { get; set; }
        public long? AcademicYearId { get; set; }
        public string? AcademicYearName { get; set; }
        public PrincipalDashboardStatsDto Stats { get; set; } = new();
        /// <summary>The last 14 calendar days, oldest first, today last. Always 14 entries.</summary>
        public List<AttendanceTrendPointDto> AttendanceTrend { get; set; } = new();
        public PrincipalDashboardAlertsDto Alerts { get; set; } = new();
        /// <summary>The latest published results (newest first, at most 5). Empty when nothing has been published yet.</summary>
        public List<DashboardPublicationDto> RecentPublications { get; set; } = new();
        public List<SchoolEventDto> UpcomingEvents { get; set; } = new();
        public List<DashboardNoticeDto> RecentNotices { get; set; } = new();
        public List<string> PartsUnavailable { get; set; } = new();
    }
}
