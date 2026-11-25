using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class SchoolWhatsAppSenderRepository : ISchoolWhatsAppSenderRepository
    {
        private readonly dbContext _db;
        public SchoolWhatsAppSenderRepository(dbContext db) => _db = db;

        public Task<SchoolSenderInfo?> GetSchoolAsync(string schoolId, CancellationToken ct) =>
            _db.Schools.AsNoTracking().Where(s => s.SchoolId == schoolId).Select(s => new SchoolSenderInfo(s.SchoolId, s.SchoolName, s.WhatsAppNumber)).FirstOrDefaultAsync(ct);

        public Task<TbSchoolWhatsAppSender?> GetSenderAsync(string schoolId, bool tracked, CancellationToken ct)
        {
            var q = tracked ? _db.SchoolWhatsAppSenders.AsTracking() : _db.SchoolWhatsAppSenders.AsNoTracking();
            return q.Where(s => s.SchoolId == schoolId).FirstOrDefaultAsync(ct);
        }

        public async Task AddSenderAsync(TbSchoolWhatsAppSender sender, CancellationToken ct) => await _db.SchoolWhatsAppSenders.AddAsync(sender, ct);

        public Task<string?> GetUserNameAsync(long userId, CancellationToken ct) =>
            _db.Users.AsNoTracking().Where(u => u.UserId == userId).Select(u => u.DisplayName ?? u.OrgUserId).FirstOrDefaultAsync(ct);

        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
        public void ClearTracking() => _db.ChangeTracker.Clear();
    }
}
