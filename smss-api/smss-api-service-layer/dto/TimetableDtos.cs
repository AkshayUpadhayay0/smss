using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // ---- periods
    public class PeriodRequest
    {
        [Required(ErrorMessage = "Period number is required."), Range(1, 30, ErrorMessage = "Period number must be between 1 and 30.")] public int? PeriodNumber { get; set; }
        [Required(ErrorMessage = "Start time is required.")] public TimeOnly? StartTime { get; set; }
        [Required(ErrorMessage = "End time is required.")] public TimeOnly? EndTime { get; set; }
    }

    public class PeriodDto
    {
        public long PeriodId { get; set; }
        public int PeriodNumber { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public string StatusName { get; set; } = null!;
        /// <summary>How many Active timetable entries use this period (a period in use can't be deactivated).</summary>
        public int ActiveEntryCount { get; set; }
    }

    // ---- timetable
    public class AssignTimetableRequest
    {
        [Required(ErrorMessage = "Academic year is required.")] public long? AcademicYearId { get; set; }
        /// <summary>Optional: the section decides the class. When sent it must be that section's class.</summary>
        public long? ClassId { get; set; }
        [Required(ErrorMessage = "Section is required.")] public long? SectionId { get; set; }
        /// <summary>1 = Monday .. 6 = Saturday. Sunday is never valid.</summary>
        [Required(ErrorMessage = "Day of week is required.")] public int? DayOfWeek { get; set; }
        [Required(ErrorMessage = "Period is required.")] public long? PeriodId { get; set; }
        [Required(ErrorMessage = "Subject is required.")] public long? SubjectId { get; set; }
        [Required(ErrorMessage = "Teacher is required."), StringLength(20)] public string? EmployeeId { get; set; }
        [StringLength(50, ErrorMessage = "The room can be at most 50 characters.")] public string? RoomNo { get; set; }
    }

    public class UnassignTimetableRequest
    {
        [Required(ErrorMessage = "Academic year is required.")] public long? AcademicYearId { get; set; }
        [Required(ErrorMessage = "Section is required.")] public long? SectionId { get; set; }
        [Required(ErrorMessage = "Day of week is required.")] public int? DayOfWeek { get; set; }
        [Required(ErrorMessage = "Period is required.")] public long? PeriodId { get; set; }
    }

    public class TimetableEntryDto
    {
        public long TimetableEntryId { get; set; }
        public long AcademicYearId { get; set; }
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public long SectionId { get; set; }
        public string SectionName { get; set; } = null!;
        public int DayOfWeek { get; set; }
        public long PeriodId { get; set; }
        public int PeriodNumber { get; set; }
        public long SubjectId { get; set; }
        public string SubjectName { get; set; } = null!;
        public string? SubjectCode { get; set; }
        public string EmployeeId { get; set; } = null!;
        public string EmployeeName { get; set; } = null!;
        public string? RoomNo { get; set; }
    }

    public class TimetablePeriodDto
    {
        public long PeriodId { get; set; }
        public int PeriodNumber { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
    }

    public class TimetableDayDto
    {
        public int DayOfWeek { get; set; }
        public string Name { get; set; } = null!;
    }

    public class TimetableCellDto
    {
        public int DayOfWeek { get; set; }
        /// <summary>null = nothing scheduled in this period on this day.</summary>
        public TimetableEntryDto? Entry { get; set; }
    }

    public class TimetableRowDto
    {
        public TimetablePeriodDto Period { get; set; } = null!;
        /// <summary>Always one cell per day, Monday to Saturday.</summary>
        public List<TimetableCellDto> Cells { get; set; } = new();
    }

    /// <summary>A weekly grid: every active period x every day (Monday to Saturday), for one section or for one teacher.</summary>
    public class TimetableGridDto
    {
        public long AcademicYearId { get; set; }
        public string AcademicYearName { get; set; } = null!;
        /// <summary>"section" or "teacher".</summary>
        public string Scope { get; set; } = null!;
        public long? SectionId { get; set; }
        public long? ClassId { get; set; }
        public string? ClassName { get; set; }
        public string? SectionName { get; set; }
        public string? EmployeeId { get; set; }
        public string? EmployeeName { get; set; }
        public List<TimetableDayDto> Days { get; set; } = new();
        public List<TimetableRowDto> Rows { get; set; } = new();
        public int EntryCount { get; set; }
    }

    public class TimetableSubjectOptionDto
    {
        public long SubjectId { get; set; }
        public string SubjectName { get; set; } = null!;
        public string? SubjectCode { get; set; }
    }

    public class TimetableTeacherOptionDto
    {
        public string EmployeeId { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string DesignationName { get; set; } = null!;
    }

    /// <summary>What the assign dialog chooses from for one section: the subjects its class studies, and the people who can teach.</summary>
    public class TimetableLookupsDto
    {
        public List<TimetableSubjectOptionDto> Subjects { get; set; } = new();
        public List<TimetableTeacherOptionDto> Teachers { get; set; } = new();
    }
}
