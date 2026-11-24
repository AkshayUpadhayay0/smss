using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class ClassRepository : IClassRepository
    {
        private readonly dbContext _db;
        public ClassRepository(dbContext db) => _db = db;

        public Task<List<TbClasses>> GetBySchoolAsync(string schoolId, long? academicYearId = null) =>
            _db.Classes.AsNoTracking()
                .Where(c => c.SchoolId == schoolId)
                .Where(c => academicYearId == null || _db.AcademicYearClasses.Any(o => o.SchoolId == schoolId && o.AcademicYearId == academicYearId && o.ClassId == c.ClassId
                    && o.StatusId == _db.LutStatus.Where(s => s.Sname == "Active" && s.Stype == "general status").Select(s => s.Sid).FirstOrDefault()))
                .OrderBy(c => c.SequenceOrder).ThenBy(c => c.ClassName)
                .ToListAsync();

        public Task<TbClasses?> GetByIdAsync(string schoolId, long id, bool track = false)
        {
            IQueryable<TbClasses> q = _db.Classes.Where(c => c.SchoolId == schoolId && c.ClassId == id);
            if (!track) q = q.AsNoTracking();
            return q.FirstOrDefaultAsync();
        }

        public Task<bool> ExistsAsync(string schoolId, string className, long? excludeId = null) =>
            _db.Classes.AnyAsync(c => c.SchoolId == schoolId && c.ClassName == className
                                      && (excludeId == null || c.ClassId != excludeId));

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

        public Task<IDbContextTransaction> BeginTransactionAsync() => _db.Database.BeginTransactionAsync();

        public async Task OfferInCurrentYearAsync(string schoolId, long classId) => await _db.OfferClassInCurrentYearAsync(schoolId, classId, default);

        public async Task AddAsync(TbClasses entity) => await _db.Classes.AddAsync(entity);

        public Task<List<string>> GetUseAsync(string schoolId, long classId) => MasterUseQueries.ClassAsync(_db, schoolId, classId);

        public Task<string?> GetClassPointingToAsync(string schoolId, long targetClassId, long? exceptClassId = null) =>
            _db.Classes.AsNoTracking().Where(c => c.SchoolId == schoolId && c.NextClassId == targetClassId && (exceptClassId == null || c.ClassId != exceptClassId))
                .Select(c => c.ClassName).FirstOrDefaultAsync();

        public async Task<bool> NextWouldLoopAsync(string schoolId, long classId, long nextClassId)
        {
            var next = await _db.Classes.AsNoTracking().Where(c => c.SchoolId == schoolId).ToDictionaryAsync(c => c.ClassId, c => c.NextClassId);
            long? cur = nextClassId;
            for (var hops = 0; cur != null && hops <= next.Count; hops++)
            {
                if (cur == classId) return true;
                cur = next.GetValueOrDefault(cur.Value);
            }
            return false;
        }

        public Task<Dictionary<long, string>> GetClassNamesAsync(string schoolId) =>
            _db.Classes.AsNoTracking().Where(c => c.SchoolId == schoolId).ToDictionaryAsync(c => c.ClassId, c => c.ClassName);

        public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
    }
}
