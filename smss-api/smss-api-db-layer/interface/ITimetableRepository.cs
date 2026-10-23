using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record TtSubjectRow(long SubjectId, string Name, string? Code, int? StatusId, bool MappedToClass);
    public record TtTeacherRow(string EmployeeId, string FirstName, string? MiddleName, string LastName, string EmployeeType, int StatusId, bool EmploymentBlocksLogin, string DesignationName);
    public record TtEntryRow(TbTimetableEntry Entry, string ClassName, string SectionName, string SubjectName, string? SubjectCode, string EmployeeName, short PeriodNumber);
    public record TtConflictRow(string ClassName, string SectionName, string SubjectName, short PeriodNumber);

    // Every method takes the school id: another school's timetable can never be reached through this repository.
    public interface ITimetableRepository
    {
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

        /// <summary>The subject (null when it isn't one of this school's), and whether the class studies it (an Active class-subject mapping).</summary>
        Task<TtSubjectRow?> GetSubjectAsync(string schoolId, long subjectId, long classId, int activeStatusId, CancellationToken ct);
        Task<List<TtSubjectRow>> GetSubjectsOfClassAsync(string schoolId, long classId, int activeStatusId, CancellationToken ct);
        Task<TtTeacherRow?> GetTeacherAsync(string schoolId, string employeeId, CancellationToken ct);
        /// <summary>Employees who can teach: Active record, Teaching, employment status that still works here.</summary>
        Task<List<TtTeacherRow>> GetTeachersAsync(string schoolId, int activeStatusId, CancellationToken ct);

        /// <summary>The Active entry of a slot, TRACKED (to replace it).</summary>
        Task<TbTimetableEntry?> GetActiveSlotForUpdateAsync(string schoolId, long academicYearId, long sectionId, short dayOfWeek, long periodId, int activeStatusId, CancellationToken ct);
        /// <summary>This teacher's Active entry in the same year / weekday / period in ANOTHER section, or null.</summary>
        Task<TtConflictRow?> FindTeacherConflictAsync(string schoolId, long academicYearId, string employeeId, short dayOfWeek, long periodId, long exceptSectionId, int activeStatusId, CancellationToken ct);
        Task AddAsync(TbTimetableEntry entry, CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);

        Task<TtEntryRow?> GetEntryRowAsync(string schoolId, long entryId, CancellationToken ct);
        Task<List<TtEntryRow>> GetSectionEntriesAsync(string schoolId, long academicYearId, long sectionId, int activeStatusId, CancellationToken ct);
        Task<List<TtEntryRow>> GetTeacherEntriesAsync(string schoolId, long academicYearId, string employeeId, int activeStatusId, CancellationToken ct);

        /// <summary>The login id (employee id) and display name of a user in this school.</summary>
        Task<string?> GetUserLoginIdAsync(string schoolId, long userId, CancellationToken ct);
    }
}
