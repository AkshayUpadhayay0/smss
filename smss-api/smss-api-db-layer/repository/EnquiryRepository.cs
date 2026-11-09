using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class EnquiryRepository : IEnquiryRepository
    {
        private readonly dbContext _db;
        public EnquiryRepository(dbContext db) => _db = db;

        public async Task AddAsync(TbEnquiry enquiry, CancellationToken ct) => await _db.Enquiries.AddAsync(enquiry, ct);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

        public async Task<(List<EnquiryListRow> Rows, int Total)> ListAsync(EnquiryFilter f, int page, int pageSize, CancellationToken ct)
        {
            var q = _db.Enquiries.AsNoTracking().AsQueryable();
            if (!string.IsNullOrEmpty(f.Type)) q = q.Where(e => e.EnquiryType == f.Type);
            if (!string.IsNullOrEmpty(f.Status)) q = q.Where(e => e.Status == f.Status);
            if (f.FromUtc != null) q = q.Where(e => e.CreatedAt >= f.FromUtc);
            if (f.ToExclusiveUtc != null) q = q.Where(e => e.CreatedAt < f.ToExclusiveUtc);
            if (!string.IsNullOrEmpty(f.Search))
            {
                // Case-insensitive contains; LIKE wildcards typed by the user are escaped so they match literally
                var like = "%" + f.Search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                q = q.Where(e => EF.Functions.ILike(e.FullName, like, "\\") || EF.Functions.ILike(e.Email, like, "\\") || (e.SchoolName != null && EF.Functions.ILike(e.SchoolName, like, "\\")));
            }

            var total = await q.CountAsync(ct);
            var ordered = f.OldestFirst ? q.OrderBy(e => e.CreatedAt).ThenBy(e => e.EnquiryId) : q.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.EnquiryId);
            var rows = await ordered.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(e => new EnquiryListRow(e.EnquiryId, e.EnquiryType, e.FullName, e.SchoolName, e.Email, e.Phone, e.City, e.Subject, e.Status, e.CreatedAt))
                .ToListAsync(ct);
            return (rows, total);
        }

        public Task<EnquiryDetailRow?> GetDetailAsync(long id, CancellationToken ct) =>
            _db.Enquiries.AsNoTracking().Where(e => e.EnquiryId == id)
                .Select(e => new EnquiryDetailRow(e, _db.Users.Where(u => u.UserId == e.HandledByUserId).Select(u => u.DisplayName ?? u.OrgUserId).FirstOrDefault()))
                .FirstOrDefaultAsync(ct);

        public Task<TbEnquiry?> GetForUpdateAsync(long id, CancellationToken ct) =>
            _db.Enquiries.AsTracking().FirstOrDefaultAsync(e => e.EnquiryId == id, ct);

        // One round trip: conditional counts over the table (SPAM is never counted as a lead this month)
        public async Task<EnquiryCounts> GetCountsAsync(DateTime monthStartUtc, CancellationToken ct)
        {
            var r = await _db.Enquiries.AsNoTracking().GroupBy(_ => 1).Select(g => new
            {
                New = g.Count(e => e.Status == "NEW"),
                Contacted = g.Count(e => e.Status == "CONTACTED"),
                Scheduled = g.Count(e => e.Status == "DEMO_SCHEDULED"),
                NewDemo = g.Count(e => e.Status == "NEW" && e.EnquiryType == "DEMO"),
                NewContact = g.Count(e => e.Status == "NEW" && e.EnquiryType == "CONTACT"),
                Month = g.Count(e => e.Status != "SPAM" && e.CreatedAt >= monthStartUtc),
            }).FirstOrDefaultAsync(ct);
            return r == null ? new EnquiryCounts(0, 0, 0, 0, 0, 0) : new EnquiryCounts(r.New, r.Contacted, r.Scheduled, r.NewDemo, r.NewContact, r.Month);
        }
    }
}
