using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // S6: several timetables (versions) per section and year, one ACTIVE. No school id: it comes from the token.
    public class TimetableSummaryDto
    {
        public long TimetableId { get; set; }
        public long AcademicYearId { get; set; }
        public long ClassId { get; set; }
        public long SectionId { get; set; }
        public string TimetableName { get; set; } = null!;
        public long ScheduleId { get; set; }
        public string ScheduleName { get; set; } = null!;
        /// <summary>DRAFT, ACTIVE or ARCHIVED.</summary>
        public string State { get; set; } = null!;
        public bool IsActive { get; set; }
        /// <summary>Was active at some time: such a timetable is archived, never deleted.</summary>
        public bool EverActive { get; set; }
        public int SlotCount { get; set; }
        /// <summary>Slots left without a teacher (copied without one).</summary>
        public int NeedsTeacherCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? ActivatedAt { get; set; }
        public string? ActivatedByName { get; set; }
        public string? CreatedByName { get; set; }
        public bool CanDelete { get; set; }
    }

    public class CreateTimetableRequest
    {
        /// <summary>Optional: the school's current year when left out.</summary>
        public long? AcademicYearId { get; set; }
        [Required(ErrorMessage = "Section is required.")] public long? SectionId { get; set; }
        [Required(ErrorMessage = "Name is required."), StringLength(80, ErrorMessage = "The name can be at most 80 characters.")] public string? TimetableName { get; set; }
        /// <summary>Start from a copy of this timetable of the SAME section (teachers included). Empty = a blank draft.</summary>
        public long? CloneFromTimetableId { get; set; }
    }

    public class RenameTimetableRequest
    {
        [Required(ErrorMessage = "Name is required."), StringLength(80, ErrorMessage = "The name can be at most 80 characters.")] public string? TimetableName { get; set; }
    }

    public class AssignSlotRequest
    {
        [Required(ErrorMessage = "Day of week is required.")] public int? DayOfWeek { get; set; }
        [Required(ErrorMessage = "Period is required.")] public long? PeriodId { get; set; }
        [Required(ErrorMessage = "Subject is required.")] public long? SubjectId { get; set; }
        /// <summary>Optional here: a slot without a teacher is flagged "needs a teacher".</summary>
        [StringLength(20)] public string? EmployeeId { get; set; }
        [StringLength(50, ErrorMessage = "The room can be at most 50 characters.")] public string? RoomNo { get; set; }
    }

    public class UnassignSlotRequest
    {
        [Required(ErrorMessage = "Day of week is required.")] public int? DayOfWeek { get; set; }
        [Required(ErrorMessage = "Period is required.")] public long? PeriodId { get; set; }
    }

    public class TimetableProblemDto
    {
        /// <summary>ERROR (blocks activation) or WARNING.</summary>
        public string Severity { get; set; } = "ERROR";
        public string Code { get; set; } = null!;
        public string Message { get; set; } = null!;
        public int? DayOfWeek { get; set; }
        public long? PeriodId { get; set; }
    }

    /// <summary>What a dry run (or a refused activation) found. Nothing is saved by asking.</summary>
    public class TimetableValidationDto
    {
        public long TimetableId { get; set; }
        public string TimetableName { get; set; } = null!;
        /// <summary>No errors: the timetable can be activated (warnings do not block).</summary>
        public bool CanActivate { get; set; }
        public int SlotCount { get; set; }
        public int SlotsWithoutTeacher { get; set; }
        public List<TimetableProblemDto> Errors { get; set; } = new();
        public List<TimetableProblemDto> Warnings { get; set; } = new();
    }

    public class TimetableActivationDto
    {
        public TimetableSummaryDto Timetable { get; set; } = null!;
        public string? PreviousTimetableName { get; set; }
        /// <summary>The warning count: slots that have no teacher (activation is still allowed).</summary>
        public int SlotsWithoutTeacher { get; set; }
    }

    public class ApplyToSectionsRequest
    {
        [Required(ErrorMessage = "Choose at least one section."), MinLength(1, ErrorMessage = "Choose at least one section.")] public List<long>? SectionIds { get; set; }
        /// <summary>The name of each new timetable (the source's name when left out).</summary>
        [StringLength(80, ErrorMessage = "The name can be at most 80 characters.")] public string? TimetableName { get; set; }
    }

    public class ApplySlotNoteDto
    {
        public int DayOfWeek { get; set; }
        public int PeriodNumber { get; set; }
        public string SubjectName { get; set; } = null!;
        public string Reason { get; set; } = null!;
    }

    public class ApplySectionResultDto
    {
        public long SectionId { get; set; }
        public string SectionName { get; set; } = null!;
        /// <summary>True when a new timetable is (or was) created for this section.</summary>
        public bool Created { get; set; }
        public long? TimetableId { get; set; }
        public string TimetableName { get; set; } = null!;
        /// <summary>Why nothing is created for this section, or null.</summary>
        public string? SkipReason { get; set; }
        public int SlotsCopied { get; set; }
        public int SlotsNeedingTeacher { get; set; }
        /// <summary>Slots left out (the subject is not taught in this section).</summary>
        public int SlotsSkipped { get; set; }
        public List<ApplySlotNoteDto> NeedsTeacher { get; set; } = new();
        public List<ApplySlotNoteDto> Skipped { get; set; } = new();
    }

    public class ApplyResultDto
    {
        public long SourceTimetableId { get; set; }
        public bool Preview { get; set; }
        public List<ApplySectionResultDto> Sections { get; set; } = new();
    }
}
