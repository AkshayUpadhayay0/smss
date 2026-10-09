using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class SectionRepository : ISectionRepository
    {
        private readonly dbContext _db;
        public SectionRepository(dbContext db) => _db = db;

        // Joined on class_id AND school_id, so a section can never surface another school's class name
        private sealed class JoinedRow
        {
            public TbSections Section { get; set; } = null!;
            public string ClassName { get; set; } = null!;
            public short ClassSequenceOrder { get; set; }
        }

        private IQueryable<JoinedRow> Joined(string schoolId) =>
            from s in _db.Sections.AsNoTracking()
            join c in _db.Classes.AsNoTracking() on s.ClassId equals c.ClassId
            where s.SchoolId == schoolId && c.SchoolId == schoolId
            select new JoinedRow { Section = s, ClassName = c.ClassName, ClassSequenceOrder = c.SequenceOrder };

        private static SectionWithClass ToModel(JoinedRow r) => new(r.Section, r.ClassName, r.ClassSequenceOrder);

        public async Task<List<SectionWithClass>> GetBySchoolAsync(string schoolId, long? classId = null)
        {
            var rows = await Joined(schoolId)
                .Where(x => classId == null || x.Section.ClassId == classId)
                .OrderBy(x => x.ClassSequenceOrder).ThenBy(x => x.ClassName).ThenBy(x => x.Section.SectionName)
                .ToListAsync();
            return rows.Select(ToModel).ToList();
        }

        public async Task<SectionWithClass?> GetWithClassByIdAsync(string schoolId, long id)
        {
            var row = await Joined(schoolId).Where(x => x.Section.SectionId == id).FirstOrDefaultAsync();
            return row == null ? null : ToModel(row);
        }

        public Task<TbSections?> GetByIdAsync(string schoolId, long id, bool track = false)
        {
            IQueryable<TbSections> q = _db.Sections.Where(s => s.SchoolId == schoolId && s.SectionId == id);
            if (!track) q = q.AsNoTracking();
            return q.FirstOrDefaultAsync();
        }

        public Task<TbClasses?> GetClassAsync(string schoolId, long classId) =>
            _db.Classes.AsNoTracking().Where(c => c.SchoolId == schoolId && c.ClassId == classId).FirstOrDefaultAsync();

        public Task<bool> ExistsAsync(long classId, string sectionName, long? excludeId = null) =>
            _db.Sections.AnyAsync(s => s.ClassId == classId && s.SectionName == sectionName
                                       && (excludeId == null || s.SectionId != excludeId));

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

        public async Task AddAsync(TbSections entity) => await _db.Sections.AddAsync(entity);

        public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
    }
}
