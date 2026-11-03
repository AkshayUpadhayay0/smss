namespace smss_api_db_layer.@interface
{
    /// <summary>One calendar day's attendance across the whole school: how many marks exist and how many of them are Present.</summary>
    public record SchoolAttendanceDayRow(DateOnly Date, int Marked, int Present);
    public record SchoolYearSpanRow(long AcademicYearId, DateOnly StartDate, DateOnly EndDate);

    // Used by SchoolDashboardService only, and read-only. It holds the two figures no existing service can give the School Admin dashboard: attendance across
    // the WHOLE school per day (Attendance only reads one section or one student at a time) and which academic years a date range touches. No new table.
    public interface ISchoolDashboardRepository
    {
        /// <summary>Marks per day for fromDate..toDate (both included), school-wide, with how many are <paramref name="presentCode"/>. Days with no marks are absent from the list.</summary>
        Task<List<SchoolAttendanceDayRow>> GetAttendanceByDayAsync(string schoolId, DateOnly fromDate, DateOnly toDate, string presentCode, CancellationToken ct);

        /// <summary>The school's academic years whose dates overlap fromDate..toDate, oldest first.</summary>
        Task<List<SchoolYearSpanRow>> GetYearsOverlappingAsync(string schoolId, DateOnly fromDate, DateOnly toDate, CancellationToken ct);
    }
}
