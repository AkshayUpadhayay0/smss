using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class ClassSubjectRepository : IClassSubjectRepository
    {
        private readonly dbContext _db;
        public ClassSubjectRepository(dbContext db) => _db = db;

        private sealed class JoinedRow
        {
            public TbClassSubjects Mapping { get; set; } = null!;
            public string ClassName { get; set; } = null!;
            public string SubjectName { get; set; } = null!;
            public string? SubjectCode { get; set; }
        }

        // Joined on the ids AND the school, so a mapping can never surface another school's class or subject name
        private IQueryable<JoinedRow> Joined(string schoolId) =>
            from m in _db.ClassSubjects.AsNoTracking()
            join c in _db.Classes.AsNoTracking() on m.ClassId equals c.ClassId
            join s in _db.Subjects.AsNoTracking() on m.SubjectId equals s.SubjectId
            where m.SchoolId == schoolId && c.SchoolId == schoolId && s.SchoolId == schoolId
            select new JoinedRow { Mapping = m, ClassName = c.ClassName, SubjectName = s.SubjectName, SubjectCode = s.SubjectCode };

        private static ClassSubjectWithNames ToModel(JoinedRow r) => new(r.Mapping, r.ClassName, r.SubjectName, r.SubjectCode);

        public async Task<List<ClassSubjectWithNames>> GetByClassAsync(string schoolId, long classId)
        {
            var rows = await Joined(schoolId)
                .Where(x => x.Mapping.ClassId == classId)
                .OrderBy(x => x.SubjectName)
                .ToListAsync();
            return rows.Select(ToModel).ToList();
        }

        public async Task<ClassSubjectWithNames?> GetWithNamesByIdAsync(string schoolId, long id)
        {
            var row = await Joined(schoolId).Where(x => x.Mapping.ClassSubjectId == id).FirstOrDefaultAsync();
            return row == null ? null : ToModel(row);
        }

        public Task<TbClassSubjects?> GetByIdAsync(string schoolId, long id) =>
            _db.ClassSubjects.Where(m => m.SchoolId == schoolId && m.ClassSubjectId == id).FirstOrDefaultAsync();

        public Task<List<TbClassSubjects>> GetTrackedByClassAsync(string schoolId, long classId) =>
            _db.ClassSubjects.Where(m => m.SchoolId == schoolId && m.ClassId == classId).ToListAsync();

        public Task<TbClasses?> GetClassAsync(string schoolId, long classId) =>
            _db.Classes.AsNoTracking().Where(c => c.SchoolId == schoolId && c.ClassId == classId).FirstOrDefaultAsync();

        public Task<int> CountSubjectsAsync(string schoolId, IReadOnlyCollection<long> subjectIds) =>
            _db.Subjects.CountAsync(s => s.SchoolId == schoolId && subjectIds.Contains(s.SubjectId));

        public Task<bool> ExistsAsync(long classId, long subjectId) =>
            _db.ClassSubjects.AnyAsync(m => m.ClassId == classId && m.SubjectId == subjectId);

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType) =>
            _db.LutStatus.AsNoTracking()
                .Where(s => s.Sname == statusName && s.Stype == statusType)
                .Select(s => (int?)s.Sid)
                .FirstOrDefaultAsync();

        public async Task AddAsync(TbClassSubjects entity) => await _db.ClassSubjects.AddAsync(entity);
        public void Remove(TbClassSubjects entity) => _db.ClassSubjects.Remove(entity);
        public void RemoveRange(IEnumerable<TbClassSubjects> entities) => _db.ClassSubjects.RemoveRange(entities);

        public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
    }
}
