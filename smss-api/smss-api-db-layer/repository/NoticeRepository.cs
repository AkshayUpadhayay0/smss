using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class NoticeRepository : INoticeRepository
    {
        private readonly dbContext _db;
        public NoticeRepository(dbContext db) => _db = db;

        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == statusName && s.Stype == statusType).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public async Task<NoticeAudienceFacts?> GetAudienceFactsAsync(string schoolId, long userId, int activeStatusId, CancellationToken ct)
        {
            var user = await _db.Users.AsNoTracking().Where(u => u.UserId == userId && u.SchoolId == schoolId && u.StatusId == activeStatusId)
                .Select(u => new { u.UserType, u.OrgUserId }).FirstOrDefaultAsync(ct);
            if (user == null) return null;

            var roleCodes = await (from ur in _db.UserRoles.AsNoTracking()
                                   join r in _db.LutRoles.AsNoTracking() on ur.RoleId equals r.RoleId
                                   where ur.UserId == userId && r.StatusId == activeStatusId
                                   select r.RoleCode).ToListAsync(ct);
            var employee = await _db.Employees.AsNoTracking().AnyAsync(e => e.SchoolId == schoolId && e.EmployeeId == user.OrgUserId && e.StatusId == activeStatusId, ct);
            var student = await _db.Students.AsNoTracking().AnyAsync(s => s.SchoolId == schoolId && s.StudentId == user.OrgUserId && s.StatusId == activeStatusId, ct);
            return new NoticeAudienceFacts(user.UserType, employee, roleCodes.Count > 0, student, roleCodes);
        }

        public async Task AddAsync(TbNotice notice, CancellationToken ct) => await _db.Notices.AddAsync(notice, ct);

        public Task<TbNotice?> GetActiveForUpdateAsync(string schoolId, long noticeId, int activeStatusId, CancellationToken ct) =>
            _db.Notices.AsTracking().Where(n => n.SchoolId == schoolId && n.NoticeId == noticeId && n.StatusId == activeStatusId).FirstOrDefaultAsync(ct);

        // The filter is applied to the entity BEFORE the projection into NoticeRow: EF can't translate a Where over the projected record.
        private IQueryable<NoticeRow> Rows(IQueryable<TbNotice> source, long readerUserId) =>
            from n in source
            select new NoticeRow(n.NoticeId, n.Title, n.Body, n.Audience, n.CreatedBy,
                _db.Users.Where(u => u.UserId == n.CreatedBy).Select(u => u.DisplayName ?? u.OrgUserId).FirstOrDefault(), n.PublishedAt, n.UpdatedAt, n.StatusId,
                _db.NoticeReads.Any(r => r.NoticeId == n.NoticeId && r.UserId == readerUserId));

        public Task<NoticeRow?> GetRowAsync(string schoolId, long noticeId, long readerUserId, CancellationToken ct) =>
            Rows(_db.Notices.AsNoTracking().Where(n => n.SchoolId == schoolId && n.NoticeId == noticeId), readerUserId).FirstOrDefaultAsync(ct);

        public async Task<List<NoticeRow>> ListAsync(string schoolId, long readerUserId, NoticeFilter f, int activeStatusId, CancellationToken ct)
        {
            var q = _db.Notices.AsNoTracking().Where(n => n.SchoolId == schoolId && n.StatusId == activeStatusId);
            if (f.Audiences != null) { var audiences = f.Audiences.ToList(); q = q.Where(n => audiences.Contains(n.Audience)); }
            if (f.PublishedFromUtc != null) q = q.Where(n => n.PublishedAt >= f.PublishedFromUtc);
            if (f.PublishedToExclusiveUtc != null) q = q.Where(n => n.PublishedAt < f.PublishedToExclusiveUtc);
            if (f.UnreadOnly) q = q.Where(n => !_db.NoticeReads.Any(r => r.NoticeId == n.NoticeId && r.UserId == readerUserId));
            var rows = await Rows(q, readerUserId).ToListAsync(ct);
            return rows.OrderByDescending(r => r.PublishedAt).ThenByDescending(r => r.NoticeId).ToList();   // most recently published first
        }

        public Task<int> CountUnreadAsync(string schoolId, long readerUserId, IReadOnlyCollection<string> audiences, int activeStatusId, CancellationToken ct)
        {
            var list = audiences.ToList();
            return _db.Notices.AsNoTracking().CountAsync(n => n.SchoolId == schoolId && n.StatusId == activeStatusId && list.Contains(n.Audience)
                                                              && !_db.NoticeReads.Any(r => r.NoticeId == n.NoticeId && r.UserId == readerUserId), ct);
        }

        public async Task MarkReadAsync(long noticeId, long userId, CancellationToken ct) =>
            await _db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO public.tb_notice_reads (notice_id, user_id) VALUES ({noticeId}, {userId}) ON CONFLICT (notice_id, user_id) DO NOTHING", ct);
    }
}
