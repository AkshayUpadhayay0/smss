using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record TtSubjectRow(long SubjectId, string Name, string? Code, int? StatusId, bool MappedToClass);
    public record TtTeacherRow(string EmployeeId, string FirstName, string? MiddleName, string LastName, string EmployeeType, int StatusId, bool EmploymentBlocksLogin, string DesignationName);
    public record TtEntryRow(TbTimetableEntry Entry, string ClassName, string SectionName, string SubjectName, string? SubjectCode, string EmployeeName, short PeriodNumber);
    public record TtConflictRow(string ClassName, string SectionName, string SubjectName, short PeriodNumber, string? TimetableName = null);
    /// <summary>One timetable of a section for the list screen, with its counts.</summary>
    public record TimetableListRow(TbTimetable Timetable, string ScheduleName, int Slots, int NeedsTeacher, string? ActivatedByName, string? CreatedByName);
    /// <summary>A teacher's Active booking in an ACTIVE timetable (what a clash message names).</summary>
    public record TeacherBookingRow(string EmployeeId, short DayOfWeek, long PeriodId, short PeriodNumber, string ClassName, string SectionName, string SubjectName, string TimetableName, long TimetableId);
    public record OfferedSectionRow(long SectionId, string SectionName, int? StatusId);

    // Every method takes the school id: another school's timetable can never be reached through this repository.
    public interface ITimetableRepository
    {
        // A closed academic year accepts no configuration, attendance, enrolment, homework or timetable writes.
        Task<bool> IsYearClosedAsync(string schoolId, long academicYearId, CancellationToken ct);

        /// <summary>Does the employee have an Active entry for exactly this section + subject in the year (any day / period)?</summary>
        Task<bool> IsTeachingSectionSubjectAsync(string schoolId, string employeeId, long sectionId, long subjectId, long academicYearId, int activeStatusId, CancellationToken ct);

        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        /// <summary>Transaction-scoped lock for one (year, weekday, period): every write that could clash on that slot (the section's slot AND any teacher's booking in it) queues up.</summary>
        Task LockSlotAsync(string schoolId, long academicYearId, short dayOfWeek, long periodId, CancellationToken ct);

        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct);
        Task<YearRef?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct);
        Task<YearRef?> GetYearContainingAsync(string schoolId, DateOnly date, CancellationToken ct);
        Task<SectionRef?> GetSectionAsync(string schoolId, long sectionId, CancellationToken ct);
        Task<TbPeriod?> GetPeriodAsync(string schoolId, long periodId, CancellationToken ct);
        Task<List<TbPeriod>> GetActivePeriodsAsync(string schoolId, int activeStatusId, CancellationToken ct);
        /// <summary>Every Active slot (periods AND breaks) of every schedule of the school, in clock-time order.</summary>
        Task<List<TbPeriod>> GetActiveSlotsAsync(string schoolId, int activeStatusId, CancellationToken ct);
        /// <summary>The school's default schedule id (null when it has none yet).</summary>
        Task<long?> GetDefaultScheduleIdAsync(string schoolId, CancellationToken ct);
        Task<Dictionary<long, string>> GetScheduleNamesAsync(string schoolId, CancellationToken ct);
        /// <summary>The year's (class, day, schedule) assignments; a null class / day means every.</summary>
        Task<List<(long? ClassId, short? Day, long ScheduleId)>> GetYearScheduleRowsAsync(string schoolId, long academicYearId, CancellationToken ct);

        /// <summary>The subject (null when it isn't one of this school's), and whether the SECTION is taught it in that academic year (the effective subjects: class mapping - REMOVE + ADD).</summary>
        Task<TtSubjectRow?> GetSubjectAsync(string schoolId, long subjectId, long academicYearId, long sectionId, int activeStatusId, CancellationToken ct);
        /// <summary>The Active subjects the section is taught in that academic year.</summary>
        Task<List<TtSubjectRow>> GetSubjectsOfSectionAsync(string schoolId, long academicYearId, long sectionId, int activeStatusId, CancellationToken ct);
        Task<TtTeacherRow?> GetTeacherAsync(string schoolId, string employeeId, CancellationToken ct);
        /// <summary>Employees who can teach: Active record, Teaching, employment status that still works here.</summary>
        Task<List<TtTeacherRow>> GetTeachersAsync(string schoolId, int activeStatusId, CancellationToken ct);

        /// <summary>The Active entry of a slot of ONE timetable, TRACKED (to replace it).</summary>
        Task<TbTimetableEntry?> GetActiveSlotForUpdateAsync(string schoolId, long timetableId, short dayOfWeek, long periodId, int activeStatusId, CancellationToken ct);
        /// <summary>This teacher's Active entry in the same year / weekday / period in ANOTHER ACTIVE timetable, or null.</summary>
        Task<TtConflictRow?> FindTeacherConflictAsync(string schoolId, long academicYearId, string employeeId, short dayOfWeek, long periodId, long exceptTimetableId, int activeStatusId, CancellationToken ct);

        // ---- S6: timetable versions
        /// <summary>The per-school academic-setup lock (transaction scoped): activation, archive, apply and every entry edit queue up on it.</summary>
        Task LockSchoolAsync(string schoolId, CancellationToken ct);
        Task<TbTimetable?> GetTimetableAsync(string schoolId, long timetableId, CancellationToken ct);
        /// <summary>TRACKED.</summary>
        Task<TbTimetable?> GetTimetableForUpdateAsync(string schoolId, long timetableId, CancellationToken ct);
        Task<TbTimetable?> GetActiveTimetableAsync(string schoolId, long academicYearId, long sectionId, CancellationToken ct);
        /// <summary>TRACKED.</summary>
        Task<TbTimetable?> GetActiveTimetableForUpdateAsync(string schoolId, long academicYearId, long sectionId, CancellationToken ct);
        Task<List<TimetableListRow>> ListTimetablesAsync(string schoolId, long academicYearId, long sectionId, int activeStatusId, CancellationToken ct);
        Task<bool> TimetableNameTakenAsync(string schoolId, long academicYearId, long sectionId, string name, long? exceptTimetableId, CancellationToken ct);
        Task AddTimetableAsync(TbTimetable timetable, CancellationToken ct);
        void RemoveTimetable(TbTimetable timetable);
        /// <summary>Every entry (any status) of a timetable, TRACKED.</summary>
        Task<List<TbTimetableEntry>> GetTimetableEntriesForUpdateAsync(string schoolId, long timetableId, CancellationToken ct);
        void RemoveEntries(IEnumerable<TbTimetableEntry> entries);
        /// <summary>The Active-status entries of one timetable (any state) with names, for the editor grid and validation.</summary>
        Task<List<TtEntryRow>> GetTimetableRowsAsync(string schoolId, long timetableId, int activeStatusId, CancellationToken ct);
        /// <summary>Active bookings of these teachers in ACTIVE timetables of the year, other than the given timetable.</summary>
        Task<List<TeacherBookingRow>> GetActiveBookingsAsync(string schoolId, long academicYearId, IReadOnlyCollection<string> employeeIds, IReadOnlyCollection<long> exceptTimetableIds, int activeStatusId, CancellationToken ct);
        /// <summary>The Active sections of the class that the year offers.</summary>
        Task<List<OfferedSectionRow>> GetOfferedSectionsOfClassAsync(string schoolId, long academicYearId, long classId, CancellationToken ct);
        Task<HashSet<long>> GetEffectiveSubjectIdsAsync(string schoolId, long academicYearId, long sectionId, CancellationToken ct);
        Task<Dictionary<long, TbPeriod>> GetSlotsByIdsAsync(string schoolId, IReadOnlyCollection<long> periodIds, CancellationToken ct);
        Task<Dictionary<string, TtTeacherRow>> GetTeachersByIdsAsync(string schoolId, IReadOnlyCollection<string> employeeIds, CancellationToken ct);
        Task<Dictionary<long, string>> GetSubjectNamesAsync(string schoolId, CancellationToken ct);
        Task<string?> GetUserDisplayNameAsync(string schoolId, long userId, CancellationToken ct);
        Task AddAsync(TbTimetableEntry entry, CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);

        Task<TtEntryRow?> GetEntryRowAsync(string schoolId, long entryId, CancellationToken ct);
        Task<List<TtEntryRow>> GetSectionEntriesAsync(string schoolId, long academicYearId, long sectionId, int activeStatusId, CancellationToken ct);
        Task<List<TtEntryRow>> GetTeacherEntriesAsync(string schoolId, long academicYearId, string employeeId, int activeStatusId, CancellationToken ct);

        /// <summary>The section a student is in for the year: an Active enrollment of an Active student (same rule as Homework / Assessments), or null.</summary>
        Task<long?> GetStudentSectionAsync(string schoolId, string studentId, long academicYearId, int activeStatusId, CancellationToken ct);

        /// <summary>The login id (employee id) and display name of a user in this school.</summary>
        Task<string?> GetUserLoginIdAsync(string schoolId, long userId, CancellationToken ct);
    }
}
