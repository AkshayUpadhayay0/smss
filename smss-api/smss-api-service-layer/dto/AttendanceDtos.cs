using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    public class AttendanceRecordRequest
    {
        [Required(ErrorMessage = "Student id is required."), StringLength(20)] public string? StudentId { get; set; }
        [Required(ErrorMessage = "Attendance status is required.")] public long? AttendanceStatusId { get; set; }
    }

    /// <summary>A whole section's attendance for one day, in one request.</summary>
    public class SubmitAttendanceRequest
    {
        [Required(ErrorMessage = "Section is required.")] public long? SectionId { get; set; }
        /// <summary>Must be today (server date, India). Past days are changed through the correction endpoint.</summary>
        [Required(ErrorMessage = "Date is required.")] public DateOnly? Date { get; set; }
        [Required(ErrorMessage = "Attendance records are required."), MinLength(1, ErrorMessage = "Attendance records are required."), MaxLength(500)]
        public List<AttendanceRecordRequest>? Records { get; set; }
    }

    /// <summary>
    /// A School Admin's correction. If the student has a mark that day it is changed; if the day has NO mark, it is created (a correction with
    /// nothing to overwrite). Either way the reason, who and when are recorded on the row.
    /// </summary>
    public class CorrectAttendanceRequest
    {
        [Required(ErrorMessage = "Student id is required."), StringLength(20)] public string? StudentId { get; set; }
        [Required(ErrorMessage = "Date is required.")] public DateOnly? Date { get; set; }
        [Required(ErrorMessage = "Attendance status is required.")] public long? AttendanceStatusId { get; set; }
        [Required(ErrorMessage = "A reason is required to correct attendance."), StringLength(500, ErrorMessage = "The reason can be at most 500 characters.")]
        public string? Reason { get; set; }
    }

    public class SubmitAttendanceResultDto
    {
        public long SectionId { get; set; }
        public long AcademicYearId { get; set; }
        public DateOnly Date { get; set; }
        public int Total { get; set; }
        public int Present { get; set; }
        public int Absent { get; set; }
        /// <summary>Marks written for the first time / marks overwritten by this (re)submission.</summary>
        public int Created { get; set; }
        public int Updated { get; set; }
    }

    public class AttendanceStatusDto
    {
        public long AttendanceStatusId { get; set; }
        public string StatusCode { get; set; } = null!;
        public string StatusName { get; set; } = null!;
    }

    public class SectionAttendanceStudentDto
    {
        public string StudentId { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string? RollNumber { get; set; }
        /// <summary>null = not marked for the date.</summary>
        public long? AttendanceStatusId { get; set; }
        public string? StatusCode { get; set; }
        public string? StatusName { get; set; }
        public string? MarkedByName { get; set; }
        public DateTime? MarkedAt { get; set; }
        public bool IsCorrected { get; set; }
        public DateTime? CorrectedAt { get; set; }
        public string? CorrectedByName { get; set; }
        public string? CorrectionReason { get; set; }
        /// <summary>False for a student who has a mark that day but is no longer on the section's active roster (left / moved since).</summary>
        public bool OnRoster { get; set; }
    }

    /// <summary>The roster of a section with each student's mark for a date (or unmarked): prefills the marking screen and shows any past day.</summary>
    public class SectionAttendanceDto
    {
        public long SectionId { get; set; }
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public string SectionName { get; set; } = null!;
        public long AcademicYearId { get; set; }
        public string AcademicYearName { get; set; } = null!;
        public DateOnly Date { get; set; }
        public bool IsToday { get; set; }
        public int RosterCount { get; set; }
        public int MarkedCount { get; set; }
        public bool IsMarked { get; set; }
        /// <summary>True when the caller may submit for this date right now: it is today, and they are the class teacher or a School Admin.</summary>
        public bool CanSubmit { get; set; }
        public List<AttendanceStatusDto> Statuses { get; set; } = new();
        public List<SectionAttendanceStudentDto> Students { get; set; } = new();
    }

    public class StudentAttendanceDayDto
    {
        public DateOnly Date { get; set; }
        public string StatusCode { get; set; } = null!;
        public string StatusName { get; set; } = null!;
        public long SectionId { get; set; }
        public string ClassName { get; set; } = null!;
        public string SectionName { get; set; } = null!;
        public bool IsCorrected { get; set; }
        public string? CorrectionReason { get; set; }
    }

    public class AttendanceSummaryDto
    {
        public int MarkedDays { get; set; }
        /// <summary>Marked days per status code, e.g. { "PRESENT": 18, "ABSENT": 2 }: a new status needs no change here.</summary>
        public Dictionary<string, int> ByStatus { get; set; } = new();
        /// <summary>Present days / marked days x 100, one decimal; null when nothing is marked.</summary>
        public decimal? PresentPercentage { get; set; }
    }

    public class StudentAttendanceHistoryDto
    {
        public string StudentId { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public DateOnly From { get; set; }
        public DateOnly To { get; set; }
        public List<StudentAttendanceDayDto> Days { get; set; } = new();
        public AttendanceSummaryDto Summary { get; set; } = new();
    }

    public class UnmarkedSectionDto
    {
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public long SectionId { get; set; }
        public string SectionName { get; set; } = null!;
        public int RosterCount { get; set; }
        public string? ClassTeacherId { get; set; }
        public string? ClassTeacherName { get; set; }
    }

    /// <summary>"Who hasn't marked": active sections (with students) that have no attendance at all for the date.</summary>
    public class MissingAttendanceDto
    {
        public DateOnly Date { get; set; }
        public long AcademicYearId { get; set; }
        public string AcademicYearName { get; set; } = null!;
        public List<UnmarkedSectionDto> Sections { get; set; } = new();
    }

    public class MarkableSectionDto
    {
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
        public long SectionId { get; set; }
        public string SectionName { get; set; } = null!;
        public int RosterCount { get; set; }
        public bool MarkedToday { get; set; }
    }

    /// <summary>The sections the caller can mark today (a teacher: their own; a School Admin: all active ones).</summary>
    public class MySectionsDto
    {
        public DateOnly Today { get; set; }
        public long AcademicYearId { get; set; }
        public string AcademicYearName { get; set; } = null!;
        public List<MarkableSectionDto> Sections { get; set; } = new();
    }

    public class AttendanceCorrectionDto
    {
        public string StudentId { get; set; } = null!;
        public DateOnly Date { get; set; }
        public long SectionId { get; set; }
        public string StatusCode { get; set; } = null!;
        public string StatusName { get; set; } = null!;
        /// <summary>null when the day had no mark: the School Admin ADDED it (a correction with nothing to overwrite).</summary>
        public string? PreviousStatusName { get; set; }
        /// <summary>True when this call created the mark instead of changing an existing one.</summary>
        public bool Created { get; set; }
        public string? CorrectedByName { get; set; }
        public DateTime CorrectedAt { get; set; }
        public string CorrectionReason { get; set; } = null!;
    }
}
