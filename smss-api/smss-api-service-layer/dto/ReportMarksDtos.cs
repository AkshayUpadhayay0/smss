using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // Examination / Report Card, STAGE 2: marks entry and the class teacher's inputs.

    // ------------------------------------------------------------------ what may I enter
    public class MarksContextRowDto
    {
        public long SchemeId { get; set; }
        public string SchemeName { get; set; } = "";
        public string EvaluationMode { get; set; } = "";
        public long TermId { get; set; }
        public string TermName { get; set; } = "";
        public long ExamId { get; set; }
        public string ExamName { get; set; } = "";
        public long ExamSubjectId { get; set; }
        public long SubjectId { get; set; }
        public string SubjectName { get; set; } = "";
        public long ClassId { get; set; }
        public string ClassName { get; set; } = "";
        public long SectionId { get; set; }
        public string SectionName { get; set; } = "";
        /// <summary>MARKS or GRADE.</summary>
        public string EntryMode { get; set; } = "";
        public decimal? MaxMarks { get; set; }
        public int SkillCount { get; set; }
        /// <summary>NOT_STARTED, DRAFT, SUBMITTED or ACCEPTED.</summary>
        public string Status { get; set; } = "NOT_STARTED";
        public bool Locked { get; set; }
        /// <summary>The caller may type marks here (School Admin, the section's class teacher, or the teacher of this subject in this section).</summary>
        public bool CanEnter { get; set; }
        /// <summary>The caller may accept or reopen here (School Admin or the section's class teacher).</summary>
        public bool CanReview { get; set; }
    }

    public class MarksContextDto
    {
        public long AcademicYearId { get; set; }
        public string AcademicYearName { get; set; } = "";
        public List<MarksContextRowDto> Rows { get; set; } = new();
    }

    // ------------------------------------------------------------------ the grid
    public class GradeOptionDto { public string Code { get; set; } = ""; public string Label { get; set; } = ""; }
    public class GridSkillDto { public long SkillId { get; set; } public string Name { get; set; } = ""; }

    public class GridCellDto
    {
        public long? SkillId { get; set; }
        /// <summary>ENTERED, ABSENT, EXEMPT, or null when nothing has been recorded yet.</summary>
        public string? State { get; set; }
        public decimal? Marks { get; set; }
        public string? GradeCode { get; set; }
    }

    public class GridStudentDto
    {
        public string StudentId { get; set; } = "";
        public string AdmissionNumber { get; set; } = "";
        public string RollNumber { get; set; } = "";
        public string FullName { get; set; } = "";
        /// <summary>False for a student who has left the section but still has marks: shown, never editable.</summary>
        public bool OnRoster { get; set; } = true;
        /// <summary>The per-subject remark for this student and this term (shared by every exam of the subject in the term). Not a mark.</summary>
        public string? Remark { get; set; }
        public List<GridCellDto> Cells { get; set; } = new();
    }

    public class GridCountsDto
    {
        public int Students { get; set; }
        public int Cells { get; set; }
        public int Entered { get; set; }
        public int Absent { get; set; }
        public int Exempt { get; set; }
        public int Missing { get; set; }
    }

    public class MarksGridDto
    {
        public long ExamSubjectId { get; set; }
        public long SectionId { get; set; }
        public long SchemeId { get; set; }
        public string SchemeName { get; set; } = "";
        public string TermName { get; set; } = "";
        public string ExamName { get; set; } = "";
        public string SubjectName { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string SectionName { get; set; } = "";
        public string EntryMode { get; set; } = "";
        public decimal? MaxMarks { get; set; }
        public List<GridSkillDto> Skills { get; set; } = new();
        public List<GradeOptionDto> GradeOptions { get; set; } = new();
        /// <summary>DRAFT, SUBMITTED or ACCEPTED (a grid nobody has saved yet is DRAFT with version 0).</summary>
        public string Status { get; set; } = "DRAFT";
        /// <summary>The token a save must carry. 0 = nothing saved yet.</summary>
        public int Version { get; set; }
        public bool Locked { get; set; }
        public bool CanEdit { get; set; }
        /// <summary>Whether the per-subject remarks can be edited now: who may enter marks here, or the class teacher, while the term-section is OPEN (whatever the grid state).</summary>
        public bool CanEditRemarks { get; set; }
        public bool CanSubmit { get; set; }
        public bool CanAccept { get; set; }
        public bool CanReopen { get; set; }
        /// <summary>Plain words for why the grid is read-only (shown instead of silently greying it out).</summary>
        public string? ReadOnlyReason { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime? AcceptedAt { get; set; }
        public GridCountsDto Counts { get; set; } = new();
        public List<GridStudentDto> Students { get; set; } = new();
    }

    public class GridEntryRequest
    {
        [Required, MaxLength(20)] public string StudentId { get; set; } = null!;
        public long? SkillId { get; set; }
        /// <summary>ENTERED, ABSENT, EXEMPT, or NONE to clear what was recorded.</summary>
        [Required, MaxLength(8)] public string State { get; set; } = null!;
        public decimal? Marks { get; set; }
        [MaxLength(10)] public string? GradeCode { get; set; }
    }

    public class SaveGridRequest
    {
        [Required, Range(1, long.MaxValue)] public long? ExamSubjectId { get; set; }
        [Required, Range(1, long.MaxValue)] public long? SectionId { get; set; }
        /// <summary>The version the caller's grid was read at. A stale version is refused with 409.</summary>
        [Range(0, int.MaxValue)] public int Version { get; set; }
        [Required, MaxLength(5000)] public List<GridEntryRequest> Entries { get; set; } = new();
        /// <summary>Optional: remarks saved together with the draft. Only the students listed are changed; an empty text clears that student's remark.</summary>
        [MaxLength(5000)] public List<RemarkEntry>? Remarks { get; set; }
    }

    public class RemarkEntry
    {
        [Required, MaxLength(20)] public string StudentId { get; set; } = null!;
        [MaxLength(400)] public string? Remark { get; set; }
    }

    /// <summary>Remarks on their own: allowed while the term-section is OPEN even when the marks grid is submitted or accepted (a remark is not a mark).</summary>
    public class SaveRemarksRequest
    {
        [Required, Range(1, long.MaxValue)] public long? ExamSubjectId { get; set; }
        [Required, Range(1, long.MaxValue)] public long? SectionId { get; set; }
        [Required, MaxLength(5000)] public List<RemarkEntry> Remarks { get; set; } = new();
    }

    public class GridActionRequest
    {
        [Required, Range(1, long.MaxValue)] public long? ExamSubjectId { get; set; }
        [Required, Range(1, long.MaxValue)] public long? SectionId { get; set; }
        [Range(0, int.MaxValue)] public int Version { get; set; }
        [MaxLength(300)] public string? Reason { get; set; }
    }

    // ------------------------------------------------------------------ Excel
    public class UploadIssueDto
    {
        /// <summary>The row in the sheet (0 for a problem with the file as a whole).</summary>
        public int Row { get; set; }
        public string Message { get; set; } = "";
    }

    public class UploadPreviewDto
    {
        public bool Applied { get; set; }
        public int Rows { get; set; }
        public int Cells { get; set; }
        public List<UploadIssueDto> Issues { get; set; } = new();
        /// <summary>What the file would put in the grid. Nothing has been saved unless Applied.</summary>
        public List<GridEntryRequest> Entries { get; set; } = new();
        /// <summary>Set when Applied: the grid as it is now.</summary>
        public MarksGridDto? Grid { get; set; }
    }

    // ------------------------------------------------------------------ class review
    public class CompletenessCellDto
    {
        public long ExamId { get; set; }
        public long ExamSubjectId { get; set; }
        public string EntryMode { get; set; } = "";
        /// <summary>NOT_STARTED, DRAFT, SUBMITTED or ACCEPTED.</summary>
        public string Status { get; set; } = "NOT_STARTED";
        public int Expected { get; set; }
        public int Entered { get; set; }
        public int Absent { get; set; }
        public int Exempt { get; set; }
        public int Missing { get; set; }
    }

    public class CompletenessSubjectDto
    {
        public long SchemeSubjectId { get; set; }
        public string SubjectName { get; set; } = "";
        public List<CompletenessCellDto> Cells { get; set; } = new();
    }

    public class CompletenessExamDto { public long ExamId { get; set; } public string Name { get; set; } = ""; }

    public class CompletenessTermDto
    {
        public long TermId { get; set; }
        public string TermName { get; set; } = "";
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public bool Locked { get; set; }
        public DateTime? LockedAt { get; set; }
        public string? LastUnlockReason { get; set; }
        public int Cells { get; set; }
        public int Accepted { get; set; }
        public int Submitted { get; set; }
        public int Draft { get; set; }
        public int NotStarted { get; set; }
        /// <summary>Every exam subject of the term is ACCEPTED: the term can be locked.</summary>
        public bool ReadyToLock { get; set; }
        public List<CompletenessExamDto> Exams { get; set; } = new();
        public List<CompletenessSubjectDto> Subjects { get; set; } = new();
    }

    public class CompletenessDto
    {
        public long SchemeId { get; set; }
        public string SchemeName { get; set; } = "";
        public string EvaluationMode { get; set; } = "";
        public string AttendanceSource { get; set; } = "";
        public long SectionId { get; set; }
        public long ClassId { get; set; }
        public string ClassName { get; set; } = "";
        public string SectionName { get; set; } = "";
        public int RosterCount { get; set; }
        public bool CanEdit { get; set; }
        public bool CanLock { get; set; }
        public List<CompletenessTermDto> Terms { get; set; } = new();
    }

    public class LockRequest
    {
        [Required, Range(1, long.MaxValue)] public long? TermId { get; set; }
        [Required, Range(1, long.MaxValue)] public long? SectionId { get; set; }
        [MaxLength(300)] public string? Reason { get; set; }
        /// <summary>The caller has seen the students who will be left out (added after the marks were accepted) and wants to lock anyway.</summary>
        public bool ConfirmExcluded { get; set; }
    }

    public class ExcludedStudentDto { public string StudentId { get; set; } = ""; public string Name { get; set; } = ""; public string RollNumber { get; set; } = ""; }

    public class LockResultDto
    {
        public long TermId { get; set; }
        public long SectionId { get; set; }
        public bool Locked { get; set; }
        public DateTime? LockedAt { get; set; }
        /// <summary>True on the 409 that asks for confirmation: nothing was locked yet.</summary>
        public bool NeedsConfirmation { get; set; }
        /// <summary>The late students the lock leaves out (also the ones that WERE left out, after a confirmed lock).</summary>
        public List<ExcludedStudentDto> Excluded { get; set; } = new();
    }

    // activity grades
    public class ActivityGradeAreaDto
    {
        public long AreaId { get; set; }
        public string Category { get; set; } = "";
        public string Name { get; set; } = "";
        public List<GradeOptionDto> Options { get; set; } = new();
    }

    public class ActivityStudentDto
    {
        public string StudentId { get; set; } = "";
        public string RollNumber { get; set; } = "";
        public string FullName { get; set; } = "";
        /// <summary>area id (as text) -> grade code.</summary>
        public Dictionary<string, string> Grades { get; set; } = new();
    }

    public class ActivityGradesDto
    {
        public long TermId { get; set; }
        public string TermName { get; set; } = "";
        public long SectionId { get; set; }
        public bool Locked { get; set; }
        public bool CanEdit { get; set; }
        public string? ReadOnlyReason { get; set; }
        public List<ActivityGradeAreaDto> Areas { get; set; } = new();
        public List<ActivityStudentDto> Students { get; set; } = new();
    }

    public class ActivityGradeEntry
    {
        [Required, MaxLength(20)] public string StudentId { get; set; } = null!;
        [Required, Range(1, long.MaxValue)] public long? AreaId { get; set; }
        /// <summary>The grade code, or null / empty to clear it.</summary>
        [MaxLength(10)] public string? GradeCode { get; set; }
    }

    public class SaveActivityGradesRequest
    {
        [Required, Range(1, long.MaxValue)] public long? TermId { get; set; }
        [Required, Range(1, long.MaxValue)] public long? SectionId { get; set; }
        [Required, MaxLength(5000)] public List<ActivityGradeEntry> Grades { get; set; } = new();
    }

    // term inputs
    public class TermStudentDto
    {
        public string StudentId { get; set; } = "";
        public string RollNumber { get; set; } = "";
        public string FullName { get; set; } = "";
        public string? Remark { get; set; }
        public string? Conduct { get; set; }
        public string? RankText { get; set; }
        public int? ManualWorkingDays { get; set; }
        public int? ManualPresentDays { get; set; }
    }

    public class TermStudentsDto
    {
        public long TermId { get; set; }
        public string TermName { get; set; } = "";
        public long SectionId { get; set; }
        public string AttendanceSource { get; set; } = "";
        public string EvaluationMode { get; set; } = "";
        public bool Locked { get; set; }
        public bool CanEdit { get; set; }
        public string? ReadOnlyReason { get; set; }
        public List<TermStudentDto> Students { get; set; } = new();
    }

    public class TermStudentEntry
    {
        [Required, MaxLength(20)] public string StudentId { get; set; } = null!;
        [MaxLength(500)] public string? Remark { get; set; }
        [MaxLength(60)] public string? Conduct { get; set; }
        [MaxLength(30)] public string? RankText { get; set; }
        [Range(0, 366)] public int? ManualWorkingDays { get; set; }
        [Range(0, 366)] public int? ManualPresentDays { get; set; }
    }

    public class SaveTermStudentsRequest
    {
        [Required, Range(1, long.MaxValue)] public long? TermId { get; set; }
        [Required, Range(1, long.MaxValue)] public long? SectionId { get; set; }
        [Required, MaxLength(2000)] public List<TermStudentEntry> Students { get; set; } = new();
    }

    // year inputs
    public class YearStudentDto
    {
        public string StudentId { get; set; } = "";
        public string RollNumber { get; set; } = "";
        public string FullName { get; set; } = "";
        public string? PromotionDecision { get; set; }
        public string? PromotedTo { get; set; }
        public DateOnly? NewSessionDate { get; set; }
    }

    public class YearStudentsDto
    {
        public long SchemeId { get; set; }
        public long SectionId { get; set; }
        public bool Locked { get; set; }
        public bool CanEdit { get; set; }
        public string? ReadOnlyReason { get; set; }
        public List<YearStudentDto> Students { get; set; } = new();
    }

    public class YearStudentEntry
    {
        [Required, MaxLength(20)] public string StudentId { get; set; } = null!;
        /// <summary>PROMOTED, NOT_PROMOTED, DETAINED, or null / empty for undecided.</summary>
        [MaxLength(12)] public string? PromotionDecision { get; set; }
        [MaxLength(60)] public string? PromotedTo { get; set; }
        public DateOnly? NewSessionDate { get; set; }
    }

    public class SaveYearStudentsRequest
    {
        [Required, Range(1, long.MaxValue)] public long? SchemeId { get; set; }
        [Required, Range(1, long.MaxValue)] public long? SectionId { get; set; }
        [Required, MaxLength(2000)] public List<YearStudentEntry> Students { get; set; } = new();
    }

    // attendance preview
    public class TermAttendanceStudentDto
    {
        public string StudentId { get; set; } = "";
        public string RollNumber { get; set; } = "";
        public string FullName { get; set; } = "";
        /// <summary>AUTO: the days this student was marked (present + absent). MANUAL: the number typed in.</summary>
        public int? WorkingDays { get; set; }
        public int? PresentDays { get; set; }
        public int AbsentDays { get; set; }
        /// <summary>Working days up to today that nobody marked for this student (AUTO only).</summary>
        public int NotMarkedDays { get; set; }
        public decimal? Percentage { get; set; }
    }

    public class TermAttendanceDto
    {
        public long TermId { get; set; }
        public string TermName { get; set; } = "";
        public long SectionId { get; set; }
        public DateOnly From { get; set; }
        public DateOnly To { get; set; }
        public DateOnly Today { get; set; }
        /// <summary>AUTO (from the Attendance module) or MANUAL (typed by the class teacher).</summary>
        public string Source { get; set; } = "";
        /// <summary>Working days of the term so far (school-wide rules: no Sundays, no Holidays, only inside the academic year).</summary>
        public int WorkingDays { get; set; }
        /// <summary>Working days so far on which nobody in the section was marked at all.</summary>
        public int UnmarkedDays { get; set; }
        public List<DateOnly> UnmarkedDates { get; set; } = new();
        public string? Warning { get; set; }
        public List<TermAttendanceStudentDto> Students { get; set; } = new();
    }
}
