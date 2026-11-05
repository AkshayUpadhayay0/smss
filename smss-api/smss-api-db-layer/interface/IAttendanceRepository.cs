using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record AttendanceYearRow(long AcademicYearId, string YearName, DateOnly StartDate, DateOnly EndDate);
    public record AttendanceStatusRow(long Id, string Code, string Name);
    public record RosterStudentRow(string StudentId, string FirstName, string? MiddleName, string LastName, string? RollNumber);
    public record EnrollmentRef(long SectionId, DateOnly EnrollmentDate);
    public record MarkRow(TbStudentAttendance Mark, string StatusCode, string StatusName, string? MarkedByName, string? CorrectedByName);
    public record StudentRef(string StudentId, string FirstName, string? MiddleName, string LastName);
    public record StudentDayRow(DateOnly Date, long SectionId, string ClassName, string SectionName, string StatusCode, string StatusName, DateTime? CorrectedAt, string? CorrectionReason);
    public record UnmarkedSectionRow(long SectionId, long ClassId, string ClassName, string SectionName, int RosterCount, string? ClassTeacherId, string? ClassTeacherName);
    /// <summary>One enrollment of the section for the year (any status) with the student's own status, for the monthly report.</summary>
    public record ReportRosterRow(string StudentId, string FirstName, string? MiddleName, string LastName, string? RollNumber, DateOnly EnrollmentDate, int EnrollmentStatusId, int StudentStatusId);
    /// <summary>One mark of the section: who, which day, and the status code (PRESENT / ABSENT).</summary>
    public record MonthMarkRow(string StudentId, DateOnly Date, string StatusCode);
    /// <summary>A Holiday calendar event (Active) overlapping the month.</summary>
    public record HolidayRow(string Title, DateOnly StartDate, DateOnly EndDate);
    public record TeacherSectionRow(long SectionId, long ClassId, string ClassName, string SectionName, int RosterCount, int MarkedToday);

    // Every method takes the school id: another school's students, sections and marks can never be reached through this repository.
    public interface IAttendanceRepository
    {
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        /// <summary>Transaction-scoped lock for one section-day, so two submissions for it queue up instead of racing.</summary>
        Task LockSectionDayAsync(string schoolId, long sectionId, DateOnly date, CancellationToken ct);

        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct);
        /// <summary>The academic year whose dates contain <paramref name="date"/> (the current year first if they overlap), or null.</summary>
        Task<YearRef?> GetYearContainingAsync(string schoolId, DateOnly date, CancellationToken ct);
        Task<YearRef?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct);
        /// <summary>Like <see cref="GetYearContainingAsync"/>, with the year's start and end dates.</summary>
        Task<AttendanceYearRow?> GetYearSpanContainingAsync(string schoolId, DateOnly date, CancellationToken ct);
        Task<SectionRef?> GetSectionAsync(string schoolId, long sectionId, CancellationToken ct);
        Task<List<AttendanceStatusRow>> GetAttendanceStatusesAsync(CancellationToken ct);

        /// <summary>The ACTIVE roster of a section for a year: an Active enrollment of an Active student in that section. Ordered by roll number then name.</summary>
        Task<List<RosterStudentRow>> GetRosterAsync(string schoolId, long academicYearId, long sectionId, int activeStatusId, CancellationToken ct);
        Task<List<MarkRow>> GetMarksAsync(string schoolId, long sectionId, DateOnly date, CancellationToken ct);
        /// <summary>Existing marks for these students on a day, anywhere in the school, TRACKED (to update or to detect a clash).</summary>
        Task<List<TbStudentAttendance>> GetExistingForUpdateAsync(string schoolId, DateOnly date, IReadOnlyCollection<string> studentIds, CancellationToken ct);
        Task<TbStudentAttendance?> GetMarkForUpdateAsync(string schoolId, string studentId, DateOnly date, CancellationToken ct);
        Task AddRangeAsync(IEnumerable<TbStudentAttendance> marks, CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);

        Task<StudentRef?> GetStudentAsync(string schoolId, string studentId, CancellationToken ct);
        /// <summary>The section the student is enrolled in for a year (any enrollment status), or null.</summary>
        Task<long?> GetStudentSectionAsync(string schoolId, string studentId, long academicYearId, CancellationToken ct);
        /// <summary>The student's enrollment for a year (any enrollment status): their section and the date they were enrolled from. Null when not enrolled that year.</summary>
        Task<EnrollmentRef?> GetStudentEnrollmentAsync(string schoolId, string studentId, long academicYearId, CancellationToken ct);
        Task<List<StudentDayRow>> GetStudentDaysAsync(string schoolId, string studentId, DateOnly fromDate, DateOnly toDate, CancellationToken ct);

        /// <summary>Active sections of Active classes with at least one active student and NO attendance row on the date, with their class teacher.</summary>
        Task<List<UnmarkedSectionRow>> GetUnmarkedSectionsAsync(string schoolId, long academicYearId, DateOnly date, int activeStatusId, CancellationToken ct);
        /// <summary>Sections a teacher may mark (their Active class-teacher assignments for the year), or every active section when <paramref name="employeeId"/> is null.</summary>
        Task<List<TeacherSectionRow>> GetMarkableSectionsAsync(string schoolId, long academicYearId, DateOnly today, string? employeeId, int activeStatusId, CancellationToken ct);

        // ---- monthly report: a fixed number of grouped reads (roster, marks, holidays), never one per student
        Task<AttendanceYearRow?> GetYearSpanAsync(string schoolId, long academicYearId, CancellationToken ct);
        Task<List<AttendanceYearRow>> GetYearsAsync(string schoolId, CancellationToken ct);
        /// <summary>The year the school marks as current, or null.</summary>
        Task<long?> GetCurrentYearIdAsync(string schoolId, CancellationToken ct);
        /// <summary>Every enrollment of the section for the year (any status) with its student's status.</summary>
        Task<List<ReportRosterRow>> GetReportRosterAsync(string schoolId, long academicYearId, long sectionId, CancellationToken ct);
        /// <summary>The section's marks between two dates, one row per mark.</summary>
        Task<List<MonthMarkRow>> GetMonthMarksAsync(string schoolId, long sectionId, DateOnly from, DateOnly to, CancellationToken ct);
        /// <summary>Active calendar events of the Holiday category that overlap the dates (school-wide).</summary>
        Task<List<HolidayRow>> GetHolidaysAsync(string schoolId, DateOnly from, DateOnly to, int activeStatusId, CancellationToken ct);
        Task<string?> GetSchoolNameAsync(string schoolId, CancellationToken ct);

        /// <summary>The login id (employee / student id) and display name of a user in this school.</summary>
        Task<(string? LoginId, string? Name)> GetUserAsync(string schoolId, long userId, CancellationToken ct);
    }
}
