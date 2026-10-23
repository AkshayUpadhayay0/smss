using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record PeriodListRow(TbPeriod Period, string StatusName, int ActiveEntryCount);

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
    }
}
