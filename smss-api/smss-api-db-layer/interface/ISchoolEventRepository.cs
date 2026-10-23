using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record EventYearRow(long AcademicYearId, string YearName, DateOnly StartDate, DateOnly EndDate);
    public record EventCategoryRow(long Id, string Name, string Code, string ColorHex);
    public record EventRow(TbSchoolEvent Event, string CategoryName, string CategoryCode, string ColorHex, string? CreatedByName);

    // Every method takes the school id: another school's events can never be reached through this repository.
    public interface ISchoolEventRepository
    {
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct);
        Task<EventYearRow?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct);
        /// <summary>The academic year whose dates contain the date (the current one first if they overlap), or null.</summary>
        Task<EventYearRow?> GetYearContainingAsync(string schoolId, DateOnly date, CancellationToken ct);
        Task<EventCategoryRow?> GetActiveCategoryAsync(long categoryId, CancellationToken ct);
        Task<List<EventCategoryRow>> GetActiveCategoriesAsync(CancellationToken ct);

        Task AddAsync(TbSchoolEvent ev, CancellationToken ct);
        /// <summary>An ACTIVE event of this school, TRACKED (to change or hide it).</summary>
        Task<TbSchoolEvent?> GetActiveForUpdateAsync(string schoolId, long eventId, int activeStatusId, CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);

        Task<EventRow?> GetRowAsync(string schoolId, long eventId, CancellationToken ct);
        /// <summary>Active events overlapping [from, to] (start_date &lt;= to AND end_date &gt;= from), optionally of one academic year, in calendar order.</summary>
        Task<List<EventRow>> GetOverlappingAsync(string schoolId, long? academicYearId, DateOnly from, DateOnly to, int activeStatusId, CancellationToken ct);
        /// <summary>The next Active events from today (ones still running today included), soonest first.</summary>
        Task<List<EventRow>> GetUpcomingAsync(string schoolId, DateOnly today, int take, int activeStatusId, CancellationToken ct);
    }
}
