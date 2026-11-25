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
        /// <summary>Active students with an Active enrolment in the VIEWED academic year.</summary>
        public int? TotalStudents { get; set; }
        /// <summary>Employees with an Active record.</summary>
        public int? TotalStaff { get; set; }
        public int? AttendanceMarkedToday { get; set; }
        public int? AttendancePresentToday { get; set; }
        /// <summary>Present / marked x 100, one decimal; null when nothing has been marked today.</summary>
        public decimal? AttendancePercentToday { get; set; }
        /// <summary>The Fee dashboard's figures for the VIEWED academic year, read-only (the Principal has no drill-down or collection rights). Null when the year has no fee items (see HasFeeSetup).</summary>
        public decimal? FeeTotalDue { get; set; }
        public decimal? FeeCollected { get; set; }
        public decimal? FeeOutstanding { get; set; }
    }

    public class PrincipalDashboardAlertsDto
    {
        /// <summary>Employee leave requests waiting for a School Admin / Principal decision.</summary>
        public int? PendingEmployeeLeaves { get; set; }
        /// <summary>Applications that have been added and are waiting to be confirmed.</summary>
        public int? ApplicationsWaitingToBeConfirmed { get; set; }
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
        /// <summary>The academic year being VIEWED (the one asked for, else the school's current year).</summary>
        public long? AcademicYearId { get; set; }
        public string? AcademicYearName { get; set; }
        public bool AcademicYearIsCurrent { get; set; }
        public bool AcademicYearIsClosed { get; set; }
        /// <summary>The school's current year name, so the client can say "viewing 2027-28, current is 2026-27".</summary>
        public string? CurrentYearName { get; set; }
        /// <summary>A sentence saying which year is shown when it is not simply the current one; null otherwise.</summary>
        public string? ViewingNote { get; set; }
        /// <summary>False = the viewed year has no fee items: the fee figures are null and the client says "No fee structure yet" (they are not zeros).</summary>
        public bool HasFeeSetup { get; set; }
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
