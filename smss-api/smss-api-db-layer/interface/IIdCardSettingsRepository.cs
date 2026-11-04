using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    /// <summary>One settings row per school. Every method takes the school id: another school's row can never come back.</summary>
    public interface IIdCardSettingsRepository
    {
        Task<TbSchoolIdCardSettings?> GetAsync(string schoolId, CancellationToken ct);
        /// <summary>The row, tracked, for changing it.</summary>
        Task<TbSchoolIdCardSettings?> GetForUpdateAsync(string schoolId, CancellationToken ct);
        void Add(TbSchoolIdCardSettings row);
        Task<int> SaveChangesAsync(CancellationToken ct);
    }
}
