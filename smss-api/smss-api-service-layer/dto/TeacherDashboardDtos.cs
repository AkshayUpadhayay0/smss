namespace smss_api_service_layer.dto
{
    public class TeacherProfileDto
    {
        public string EmployeeId { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string DesignationName { get; set; } = null!;
        public string DepartmentName { get; set; } = null!;
        /// <summary>Relative upload path (/uploads/...); the client prepends the API base URL.</summary>
        public string? PhotoUrl { get; set; }
    }

    public class TeacherSectionDto
    {
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public long SectionId { get; set; }
        public string SectionName { get; set; } = null!;
    }

    /// <summary>Names of the dashboard parts that could not be loaded (the client shows "Unavailable" for them instead of a wrong number).</summary>
    public static class TeacherDashboardParts
    {
        public const string ClassTeacher = "classTeacherOf";
        public const string Subjects = "subjectsTaught";
        public const string Students = "totalStudents";
        public const string Fees = "feePending";
        public const string Attendance = "attendance";
    }

    /// <summary>
    /// Everything the Teacher dashboard shows, in one response. The four class-teacher numbers are null ("not applicable") when ClassTeacherOf is empty.
    /// FeePending is a head count only: no amounts and no student list.
    /// </summary>
    public class TeacherDashboardDto
    {
        public DateOnly Today { get; set; }
        public TeacherProfileDto Profile { get; set; } = null!;
        /// <summary>Every section the teacher is the Active class teacher of this academic year; empty = not a class teacher.</summary>
        public List<TeacherSectionDto> ClassTeacherOf { get; set; } = new();
        /// <summary>Distinct subjects of the teacher's Active timetable entries, across every section they teach.</summary>
        public List<string> SubjectsTaught { get; set; } = new();
        public int? TotalStudents { get; set; }
        /// <summary>Students with an installment due this (India) calendar month that is not fully paid.</summary>
        public int? FeePending { get; set; }
        /// <summary>Summed over the class-teacher sections that are marked today; null when none is marked yet (see NotMarkedYet) or not applicable.</summary>
        public int? PresentToday { get; set; }
        public int? AbsentToday { get; set; }
        /// <summary>True when the teacher has class-teacher sections and none of them has been marked today.</summary>
        public bool NotMarkedYet { get; set; }
        /// <summary>With several sections, how many of them are still unmarked today (0 when all are marked, or when none is: then NotMarkedYet is set).</summary>
        public int UnmarkedSections { get; set; }
        public List<string> PartsUnavailable { get; set; } = new();
    }
}
