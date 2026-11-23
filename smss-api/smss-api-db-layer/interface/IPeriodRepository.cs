using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record PeriodListRow(TbPeriod Period, string StatusName, int ActiveEntryCount);

    /// <summary>Usage of one schedule: slots, year assignments, and timetable entries (any status) sitting on its slots.</summary>
    public record ScheduleUsageRow(long ScheduleId, int Slots, int Assignments, int Entries);
    /// <summary>Active timetable entries of a year, counted per class, day and the schedule their period belongs to.</summary>
    public record EntryScheduleUse(long ClassId, short DayOfWeek, long ScheduleId, int Entries);

    // Every method takes the school id: another school's periods can never be reached through this repository.
    public interface IPeriodRepository
    {
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct);
        Task<List<PeriodListRow>> ListAsync(string schoolId, bool includeInactive, int activeStatusId, CancellationToken ct);
        /// <summary>TRACKED.</summary>
        Task<TbPeriod?> GetForUpdateAsync(string schoolId, long periodId, CancellationToken ct);
        Task<PeriodListRow?> GetRowAsync(string schoolId, long periodId, int activeStatusId, CancellationToken ct);
        /// <summary>Any period of the school (Active or not) already has this number? (the unique rule covers inactive ones too)</summary>
        Task<bool> NumberTakenAsync(string schoolId, short number, long? exceptPeriodId, CancellationToken ct);
        /// <summary>The number of an ACTIVE period whose times overlap [start, end), or null.</summary>
        Task<short?> FindOverlapAsync(string schoolId, TimeOnly start, TimeOnly end, long? exceptPeriodId, int activeStatusId, CancellationToken ct);
        Task<int> ActiveEntryCountAsync(string schoolId, long periodId, int activeStatusId, CancellationToken ct);
        Task AddAsync(TbPeriod period, CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);

        // ---- S4: schedules, slots and the year assignments
        /// <summary>The school's default schedule, creating "Regular" when it has none yet.</summary>
        Task<TbPeriodSchedule> EnsureDefaultScheduleAsync(string schoolId, int activeStatusId, CancellationToken ct);
        Task<TbPeriodSchedule?> GetDefaultScheduleAsync(string schoolId, CancellationToken ct);
        Task<List<TbPeriodSchedule>> ListSchedulesAsync(string schoolId, CancellationToken ct);
        /// <summary>TRACKED.</summary>
        Task<TbPeriodSchedule?> GetScheduleForUpdateAsync(string schoolId, long scheduleId, CancellationToken ct);
        Task<bool> ScheduleNameTakenAsync(string schoolId, string name, long? exceptScheduleId, CancellationToken ct);
        Task<List<ScheduleUsageRow>> ScheduleUsageAsync(string schoolId, CancellationToken ct);
        /// <summary>Every slot (period or break, Active or not) of the school, by schedule then order. Untracked.</summary>
        Task<List<TbPeriod>> AllSlotsAsync(string schoolId, CancellationToken ct);
        /// <summary>The slots of one schedule, TRACKED.</summary>
        Task<List<TbPeriod>> ScheduleSlotsForUpdateAsync(string schoolId, long scheduleId, CancellationToken ct);
        Task<int> EntryCountAnyStatusAsync(string schoolId, long periodId, CancellationToken ct);
        /// <summary>Active timetable entries per slot (period id), for the whole school.</summary>
        Task<Dictionary<long, int>> ActiveEntryCountsAsync(string schoolId, int activeStatusId, CancellationToken ct);
        /// <summary>The academic years that have Active timetable entries.</summary>
        Task<List<long>> EntryYearIdsAsync(string schoolId, CancellationToken ct);
        /// <summary>Puts the slots of a schedule in clock-time order (slot_order 1..n), in two steps so the unique order never collides.</summary>
        Task ResequenceAsync(string schoolId, long scheduleId, CancellationToken ct);
        void RemoveSlots(IEnumerable<TbPeriod> slots);
        void RemoveSchedule(TbPeriodSchedule schedule);
        Task AddScheduleAsync(TbPeriodSchedule schedule, CancellationToken ct);

        Task<TbAcademicYears?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct);
        /// <summary>The classes the year offers (Active), as (id, name) in class order.</summary>
        Task<List<(long ClassId, string ClassName)>> OfferedClassesAsync(string schoolId, long academicYearId, int activeStatusId, CancellationToken ct);
        Task<List<TbAcademicYearSchedule>> AssignmentsForUpdateAsync(string schoolId, long academicYearId, CancellationToken ct);
        Task<List<EntryScheduleUse>> EntryScheduleUseAsync(string schoolId, long academicYearId, int activeStatusId, CancellationToken ct);
        Task AddAssignmentsAsync(IEnumerable<TbAcademicYearSchedule> rows, CancellationToken ct);
        void RemoveAssignments(IEnumerable<TbAcademicYearSchedule> rows);
        Task LockSchoolAsync(string schoolId, CancellationToken ct);
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
    }
}
