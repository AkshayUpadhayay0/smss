namespace smss_api_service_layer.dto
{
    public class DashboardProfileDto
    {
        public string StudentId { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string AdmissionNumber { get; set; } = null!;
        /// <summary>The class / section / roll number of the school's current academic year (null when the student is not enrolled in it).</summary>
        public string? ClassName { get; set; }
        public string? SectionName { get; set; }
        public string? RollNumber { get; set; }
        public string? AcademicYearName { get; set; }
        /// <summary>Relative upload path (/uploads/...); the client prepends the API base URL.</summary>
        public string? ProfilePhotoUrl { get; set; }
    }

    /// <summary>The current academic year so far. Two statuses only (Present / Absent): those are the only ones that exist.</summary>
    public class DashboardAttendanceDto
    {
        public int PresentCount { get; set; }
        public int AbsentCount { get; set; }
        /// <summary>Every marked day, whatever its status.</summary>
        public int TotalMarkedDays { get; set; }
        /// <summary>Present / marked days x 100, one decimal; null when nothing is marked yet.</summary>
        public decimal? PresentPercentage { get; set; }
        public DateOnly From { get; set; }
        public DateOnly To { get; set; }
    }

    public static class ClassStatuses
    {
        public const string Completed = "Completed";
        public const string InProgress = "InProgress";
        public const string Upcoming = "Upcoming";
    }

    public class TodayClassDto
    {
        public int PeriodNumber { get; set; }
        public string SubjectName { get; set; } = null!;
        public string TeacherName { get; set; } = null!;
        public string? RoomNo { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        /// <summary>Completed / InProgress / Upcoming, computed against the India clock when the dashboard is read; never stored.</summary>
        public string Status { get; set; } = null!;
    }

    /// <summary>Everything the Student dashboard shows, in one response. A part that could not be loaded is empty / null (and logged) instead of failing the page.</summary>
    public class StudentDashboardDto
    {
        public DashboardProfileDto Profile { get; set; } = null!;
        public DashboardAttendanceDto? Attendance { get; set; }
        /// <summary>Events from today to the end of the academic year.</summary>
        public int EventsCount { get; set; }
        public int UnreadNoticesCount { get; set; }
        public DateOnly Today { get; set; }
        public List<TodayClassDto> TodayClasses { get; set; } = new();
        public List<NoticeDto> RecentNotices { get; set; } = new();
        public List<SchoolEventDto> UpcomingEvents { get; set; } = new();
        public List<StudentAssessmentResultDto> RecentResults { get; set; } = new();
        public List<StudentLeaveDto> RecentLeave { get; set; } = new();
    }
}
