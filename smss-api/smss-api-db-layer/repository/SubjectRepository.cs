using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class SubjectRepository : ISubjectRepository
    {
        private readonly dbContext _db;
        public SubjectRepository(dbContext db) => _db = db;

        public Task<List<TbSubjects>> GetBySchoolAsync(string schoolId) =>
            _db.Subjects.AsNoTracking()
                .Where(x => x.SchoolId == schoolId)
                .OrderBy(x => x.SubjectName)
                .ToListAsync();

        public Task<TbSubjects?> GetByIdAsync(string schoolId, long id, bool track = false)
        {
            IQueryable<TbSubjects> q = _db.Subjects.Where(x => x.SchoolId == schoolId && x.SubjectId == id);
            if (!track) q = q.AsNoTracking();
            return q.FirstOrDefaultAsync();
        }

        public Task<bool> ExistsAsync(string schoolId, string name, long? excludeId = null) =>
            _db.Subjects.AnyAsync(x => x.SchoolId == schoolId && x.SubjectName == name
                                         && (excludeId == null || x.SubjectId != excludeId));

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType) =>
            _db.LutStatus.AsNoTracking()
                .Where(s => s.Sname == statusName && s.Stype == statusType)
                .Select(s => (int?)s.Sid)
                .FirstOrDefaultAsync();

        public async Task<List<(int Sid, string Name)>> GetGeneralStatusesAsync(string statusType)
        {
            var rows = await _db.LutStatus.AsNoTracking()
                .Where(s => s.Stype == statusType)
                .Select(s => new { s.Sid, s.Sname })
                .ToListAsync();
            return rows.Select(r => (r.Sid, r.Sname)).ToList();
        }

        public async Task AddAsync(TbSubjects entity) => await _db.Subjects.AddAsync(entity);

        public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
    }
}
