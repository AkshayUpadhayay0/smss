using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class StudentFeeBillingRepository : IStudentFeeBillingRepository
    {
        private readonly dbContext _db;
        public StudentFeeBillingRepository(dbContext db) => _db = db;

        public Task<List<LutFeeFrequency>> GetFrequenciesAsync(CancellationToken ct) =>
            _db.FeeFrequencies.AsNoTracking().OrderBy(f => f.FrequencyId).ToListAsync(ct);

        public Task<TbAcademicYears?> GetAcademicYearAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.AcademicYearId == academicYearId).FirstOrDefaultAsync(ct);

        public async Task<List<SourceLineRow>> GetSourceLinesAsync(long feeStructureId, CancellationToken ct)
        {
            var rows = await (
                from d in _db.FeeStructureDetails.AsNoTracking()
                join t in _db.FeeTypes.AsNoTracking() on d.FeeTypeId equals t.FeeTypeId
                join f in _db.FeeFrequencies.AsNoTracking() on d.FrequencyId equals f.FrequencyId
                where d.FeeStructureId == feeStructureId
                orderby t.FeeTypeName
                select new { d.FeeTypeId, t.FeeTypeName, t.FeeTypeCode, t.IsOptional, d.Amount, d.FrequencyId, f.FrequencyName, f.FrequencyCode, d.DueDay }).ToListAsync(ct);

            return rows.Select(r => new SourceLineRow
            {
                FeeTypeId = r.FeeTypeId, FeeTypeName = r.FeeTypeName, FeeTypeCode = r.FeeTypeCode, IsOptional = r.IsOptional, Amount = r.Amount,
                FrequencyId = r.FrequencyId, FrequencyName = r.FrequencyName, FrequencyCode = r.FrequencyCode, DueDay = r.DueDay
            }).ToList();
        }

        public Task<TbFeeStructures?> FindActiveStructureAsync(string schoolId, long classId, long academicYearId, CancellationToken ct) =>
            _db.FeeStructures.AsNoTracking()
                .Where(f => f.SchoolId == schoolId && f.ClassId == classId && f.AcademicYearId == academicYearId && f.IsActive)
                .FirstOrDefaultAsync(ct);

        public Task<List<TbStudentFeeItem>> GetItemsAsync(string schoolId, string studentId, long academicYearId, CancellationToken ct) =>
            _db.StudentFeeItems.Include(i => i.Dues)
                .Where(i => i.SchoolId == schoolId && i.StudentId == studentId && i.AcademicYearId == academicYearId)
                .OrderBy(i => i.StudentFeeItemId)
                .ToListAsync(ct);

        public Task<TbStudentFeeItem?> GetItemAsync(string schoolId, string studentId, long itemId, CancellationToken ct) =>
            _db.StudentFeeItems.Include(i => i.Dues)
                .Where(i => i.SchoolId == schoolId && i.StudentId == studentId && i.StudentFeeItemId == itemId)
                .FirstOrDefaultAsync(ct);

        public Task<List<TbStudentFeeItem>> GetItemsForStructureLineAsync(string schoolId, long feeStructureId, long feeTypeId, CancellationToken ct) =>
            (from i in _db.StudentFeeItems.Include(x => x.Dues)
             join a in _db.StudentFeeStructures on new { i.StudentId, i.AcademicYearId } equals new { a.StudentId, a.AcademicYearId }
             where i.SchoolId == schoolId && a.SchoolId == schoolId && a.FeeStructureId == feeStructureId && i.FeeTypeId == feeTypeId
             select i).ToListAsync(ct);

        public async Task<List<(string StudentId, long AcademicYearId)>> GetAssignedStudentsAsync(string schoolId, long feeStructureId, CancellationToken ct)
        {
            var rows = await _db.StudentFeeStructures.AsNoTracking()
                .Where(a => a.SchoolId == schoolId && a.FeeStructureId == feeStructureId)
                .Select(a => new { a.StudentId, a.AcademicYearId })
                .ToListAsync(ct);
            return rows.Select(r => (r.StudentId, r.AcademicYearId)).ToList();
        }

        public Task<Dictionary<long, string>> GetFeeTypeNamesAsync(string schoolId, CancellationToken ct) =>
            _db.FeeTypes.AsNoTracking().Where(t => t.SchoolId == schoolId).ToDictionaryAsync(t => t.FeeTypeId, t => t.FeeTypeName, ct);

        public async Task<DateOnly?> GetEnrollmentStartAsync(string schoolId, string studentId, long academicYearId, CancellationToken ct)
        {
            var date = await _db.StudentEnrollments.AsNoTracking()
                .Where(e => e.SchoolId == schoolId && e.StudentId == studentId && e.AcademicYearId == academicYearId)
                .Select(e => (DateTime?)e.EnrollmentDate)
                .FirstOrDefaultAsync(ct);
            return date == null ? null : DateOnly.FromDateTime(date.Value);
        }

        public async Task AddItemAsync(TbStudentFeeItem item, CancellationToken ct) => await _db.StudentFeeItems.AddAsync(item, ct);
        public void RemoveItem(TbStudentFeeItem item) => _db.StudentFeeItems.Remove(item);
        public void RemoveDues(IEnumerable<TbStudentFeeDue> dues) => _db.StudentFeeDues.RemoveRange(dues);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
    }
}
