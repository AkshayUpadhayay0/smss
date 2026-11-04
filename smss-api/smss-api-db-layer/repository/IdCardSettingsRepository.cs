using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class IdCardSettingsRepository : IIdCardSettingsRepository
    {
        private readonly dbContext _db;
        public IdCardSettingsRepository(dbContext db) => _db = db;

        public Task<TbSchoolIdCardSettings?> GetAsync(string schoolId, CancellationToken ct) =>
            _db.SchoolIdCardSettings.AsNoTracking().FirstOrDefaultAsync(s => s.SchoolId == schoolId, ct);

        public Task<TbSchoolIdCardSettings?> GetForUpdateAsync(string schoolId, CancellationToken ct) =>
            _db.SchoolIdCardSettings.FirstOrDefaultAsync(s => s.SchoolId == schoolId, ct);

        public void Add(TbSchoolIdCardSettings row) => _db.SchoolIdCardSettings.Add(row);

        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
    }
}
