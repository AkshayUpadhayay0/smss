using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // S4: bell schedules (named sets of slots) and which schedule a class follows on which day in a year. No school id: it comes from the token.
    public class ScheduleDto
    {
        public long ScheduleId { get; set; }
        public string ScheduleName { get; set; } = null!;
        public bool IsDefault { get; set; }
        public List<ScheduleSlotDto> Slots { get; set; } = new();
        /// <summary>Year assignments that point at this schedule.</summary>
        public int Assignments { get; set; }
        /// <summary>Timetable entries (any status) sitting on this schedule's slots.</summary>
        public int TimetableEntries { get; set; }
        /// <summary>Null = it can be deleted; otherwise why not.</summary>
        public string? DeleteBlockedReason { get; set; }
    }

    public class ScheduleSlotDto
    {
        public long PeriodId { get; set; }
        /// <summary>PERIOD or BREAK.</summary>
        public string SlotType { get; set; } = "PERIOD";
        public int? PeriodNumber { get; set; }
        public string? Label { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public int SlotOrder { get; set; }
        public bool IsActive { get; set; }
        /// <summary>Active timetable entries on this slot (a slot in use can't be deactivated, deleted or turned into a break).</summary>
        public int ActiveEntryCount { get; set; }
    }

    public class CreateScheduleRequest
    {
        [Required(ErrorMessage = "Schedule name is required."), StringLength(60, ErrorMessage = "The name can be at most 60 characters.")]
        public string? ScheduleName { get; set; }
        /// <summary>Start from a copy of this schedule's slots (optional).</summary>
        public long? CopyFromScheduleId { get; set; }
    }

    public class RenameScheduleRequest
    {
        [Required(ErrorMessage = "Schedule name is required."), StringLength(60, ErrorMessage = "The name can be at most 60 characters.")]
        public string? ScheduleName { get; set; }
    }

    public class ScheduleSlotRequest
    {
        [Required(ErrorMessage = "Slot type is required (PERIOD or BREAK).")] public string? SlotType { get; set; }
        /// <summary>Required for a PERIOD (1 to 30), ignored for a BREAK.</summary>
        [Range(1, 30, ErrorMessage = "Period number must be between 1 and 30.")] public int? PeriodNumber { get; set; }
        /// <summary>Required for a BREAK ("Lunch"), optional for a PERIOD.</summary>
        [StringLength(30, ErrorMessage = "The label can be at most 30 characters.")] public string? Label { get; set; }
        [Required(ErrorMessage = "Start time is required.")] public TimeOnly? StartTime { get; set; }
        [Required(ErrorMessage = "End time is required.")] public TimeOnly? EndTime { get; set; }
    }

    public class YearSchedulesDto
    {
        public long AcademicYearId { get; set; }
        public string YearName { get; set; } = null!;
        public bool IsClosed { get; set; }
        public long? DefaultScheduleId { get; set; }
        public List<ScheduleChoiceDto> Schedules { get; set; } = new();
        public List<ScheduleClassDto> Classes { get; set; } = new();
        public List<ScheduleAssignmentDto> Rows { get; set; } = new();
    }

    public class ScheduleChoiceDto { public long ScheduleId { get; set; } public string ScheduleName { get; set; } = null!; public bool IsDefault { get; set; } }
    public class ScheduleClassDto { public long ClassId { get; set; } public string ClassName { get; set; } = null!; }

    public class ScheduleAssignmentDto
    {
        /// <summary>Null = every class the year offers.</summary>
        public long? ClassId { get; set; }
        public string? ClassName { get; set; }
        /// <summary>Null = every day; 1 = Monday .. 6 = Saturday.</summary>
        public int? DayOfWeek { get; set; }
        public long ScheduleId { get; set; }
        public string ScheduleName { get; set; } = null!;
    }

    /// <summary>The WHOLE set of assignments of the year (empty = every class and day follows the default schedule).</summary>
    public class SaveYearSchedulesRequest
    {
        [Required(ErrorMessage = "The assignment list is required (it may be empty).")]
        public List<ScheduleAssignmentRequest>? Rows { get; set; }
    }

    public class ScheduleAssignmentRequest
    {
        public long? ClassId { get; set; }
        [Range(1, 6, ErrorMessage = "Day must be 1 (Monday) to 6 (Saturday), or empty for every day.")] public int? DayOfWeek { get; set; }
        [Range(1, long.MaxValue, ErrorMessage = "Schedule is required.")] public long ScheduleId { get; set; }
    }
}
