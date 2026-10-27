namespace smss_api_db_layer.@interface
{
    // Used by TeacherDashboardService only. It exists so the Teacher dashboard can count students with fees due WITHOUT the Teacher role ever being
    // given access to the Fee services or endpoints. It returns a head count, never an amount.
    public interface ITeacherDashboardRepository
    {
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct);

        /// <summary>
        /// Distinct students on the Active roster (Active enrollment of an Active student, in the given academic year) of the given sections who have at
        /// least one installment with fromDate &lt;= due_date &lt; toExclusive that is not fully paid (amount_paid &lt; amount_due). Scoped to the school.
        /// </summary>
        Task<int> CountStudentsWithUnpaidDuesAsync(string schoolId, long academicYearId, IReadOnlyCollection<long> sectionIds, int activeStatusId, DateOnly fromDate, DateOnly toExclusive, CancellationToken ct);
    }
}
