using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.context;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class MasterImportRepository : IMasterImportRepository
    {
        private const string GeneralStatusType = "general status";

        private readonly dbContext _db;
        public MasterImportRepository(dbContext db) => _db = db;

        // Case-insensitive on purpose: "class 1" and "Class 1" are the same class to a school, even though the
        // database's unique index would allow both.
        public async Task<Dictionary<string, long>> GetClassIdsByNameAsync(string schoolId, CancellationToken ct)
        {
            var rows = await _db.Classes.AsNoTracking().Where(c => c.SchoolId == schoolId)
                .Select(c => new { c.ClassName, c.ClassId }).ToListAsync(ct);
            var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows) map.TryAdd(r.ClassName, r.ClassId);
            return map;
        }

        public async Task<HashSet<long>> GetClassIdsWithSectionsAsync(string schoolId, CancellationToken ct) =>
            (await _db.Sections.AsNoTracking().Where(s => s.SchoolId == schoolId).Select(s => s.ClassId).Distinct().ToListAsync(ct)).ToHashSet();

        public Task<List<string>> GetSubjectNamesAsync(string schoolId, CancellationToken ct) =>
            _db.Subjects.AsNoTracking().Where(s => s.SchoolId == schoolId).Select(s => s.SubjectName).ToListAsync(ct);

        public Task<List<string>> GetDesignationNamesAsync(string schoolId, CancellationToken ct) =>
            _db.EmployeeDesignations.AsNoTracking().Where(s => s.SchoolId == schoolId).Select(s => s.DesignationName).ToListAsync(ct);

        public Task<List<string>> GetDepartmentNamesAsync(string schoolId, CancellationToken ct) =>
            _db.EmployeeDepartments.AsNoTracking().Where(s => s.SchoolId == schoolId).Select(s => s.DepartmentName).ToListAsync(ct);


        public Task<List<string>> GetDocumentCodesAsync(string schoolId, CancellationToken ct) =>
            _db.DocumentTypes.AsNoTracking().Where(s => s.SchoolId == schoolId).Select(s => s.DocumentCode).ToListAsync(ct);

        public Task<int?> GetActiveStatusIdAsync(CancellationToken ct) =>
            _db.LutStatus.AsNoTracking()
                .Where(s => s.Sname == "Active" && s.Stype == GeneralStatusType)
                .Select(s => (int?)s.Sid)
                .FirstOrDefaultAsync(ct);

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);

        public async Task<int> LinkNextClassesAsync(string schoolId, IReadOnlyList<(string From, string To)> pairs, CancellationToken ct)
        {
            var set = 0;
            foreach (var (from, to) in pairs)
                set += await _db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE public.tb_classes c SET next_class_id = n.class_id, updated_at = now()
FROM public.tb_classes n
WHERE c.school_id = {schoolId} AND n.school_id = {schoolId} AND c.class_name = {from} AND n.class_name = {to} AND c.next_class_id IS NULL
  AND NOT EXISTS (SELECT 1 FROM public.tb_classes o WHERE o.school_id = {schoolId} AND o.next_class_id = n.class_id)", ct);
            return set;
        }

        public async Task AddRangeAsync<T>(IEnumerable<T> entities, CancellationToken ct) where T : class =>
            await _db.Set<T>().AddRangeAsync(entities, ct);

        public Task OfferInCurrentYearAsync(string schoolId, long[] classIds, CancellationToken ct) => _db.OfferClassesWithSectionsInCurrentYearAsync(schoolId, classIds, ct);

        public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
    }
}
