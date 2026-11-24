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

        // ---- the year every dashboard shows (see DashboardYears): the school's CURRENT year, any year by id, and the year whose dates contain a day
        /// <summary>The school's current (is_current and Active) academic year, or null.</summary>
        Task<DashboardYearRow?> CurrentYearAsync(string schoolId, CancellationToken ct);
        /// <summary>One of the school's own years by id (closed or not); null for another school's id.</summary>
        Task<DashboardYearRow?> YearAsync(string schoolId, long academicYearId, CancellationToken ct);
        /// <summary>The year whose dates contain <paramref name="date"/> (the current one first when years overlap), or null.</summary>
        Task<DashboardYearRow?> YearContainingAsync(string schoolId, DateOnly date, CancellationToken ct);
        /// <summary>Active students with an Active enrolment in the year.</summary>
        Task<int> EnrolledStudentsAsync(string schoolId, long academicYearId, CancellationToken ct);
        /// <summary>Does the year have any fee item at all (false = no fee structure was ever assigned in it)?</summary>
        Task<bool> HasFeeItemsAsync(string schoolId, long academicYearId, CancellationToken ct);
    }
}
