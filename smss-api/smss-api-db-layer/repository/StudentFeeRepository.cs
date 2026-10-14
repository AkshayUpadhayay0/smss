using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class StudentFeeRepository : IStudentFeeRepository
    {
        private readonly dbContext _db;
        public StudentFeeRepository(dbContext db) => _db = db;

        public Task<TbStudent?> GetStudentAsync(string schoolId, string studentId, CancellationToken ct) =>
            _db.Students.AsNoTracking().Where(s => s.SchoolId == schoolId && s.StudentId == studentId).FirstOrDefaultAsync(ct);

        public async Task<List<StudentFeeEnrollmentRow>> GetEnrollmentsAsync(string schoolId, string studentId, CancellationToken ct)
        {
            var rows = await (
                from e in _db.StudentEnrollments.AsNoTracking()
                join y in _db.AcademicYears.AsNoTracking() on e.AcademicYearId equals y.AcademicYearId
                join sec in _db.Sections.AsNoTracking() on e.ClassSectionId equals sec.SectionId
                join c in _db.Classes.AsNoTracking() on sec.ClassId equals c.ClassId
                where e.SchoolId == schoolId && e.StudentId == studentId
                      && y.SchoolId == schoolId && sec.SchoolId == schoolId && c.SchoolId == schoolId
                select new { e.AcademicYearId, y.YearName, y.StartDate, y.IsCurrent, c.ClassId, c.ClassName }).ToListAsync(ct);

            return rows.Select(r => new StudentFeeEnrollmentRow
            {
                AcademicYearId = r.AcademicYearId, AcademicYearName = r.YearName, AcademicYearStart = r.StartDate,
                AcademicYearIsCurrent = r.IsCurrent, ClassId = r.ClassId, ClassName = r.ClassName
            }).ToList();
        }

        public Task<TbStudentFeeStructure?> GetAssignmentAsync(string schoolId, string studentId, long academicYearId, bool track, CancellationToken ct)
        {
            IQueryable<TbStudentFeeStructure> q = _db.StudentFeeStructures
                .Where(a => a.SchoolId == schoolId && a.StudentId == studentId && a.AcademicYearId == academicYearId);
            if (!track) q = q.AsNoTracking();
            return q.FirstOrDefaultAsync(ct);
        }

        public Task<TbFeeStructures?> GetStructureAsync(string schoolId, long feeStructureId, CancellationToken ct) =>
            _db.FeeStructures.AsNoTracking().Where(f => f.SchoolId == schoolId && f.FeeStructureId == feeStructureId).FirstOrDefaultAsync(ct);

        public async Task<List<StudentFeeLineRow>> GetStructureLinesAsync(long feeStructureId, CancellationToken ct)
        {
            var rows = await (
                from d in _db.FeeStructureDetails.AsNoTracking()
                join t in _db.FeeTypes.AsNoTracking() on d.FeeTypeId equals t.FeeTypeId
                join f in _db.FeeFrequencies.AsNoTracking() on d.FrequencyId equals f.FrequencyId
                where d.FeeStructureId == feeStructureId
                orderby t.FeeTypeName
                select new { d.FeeTypeId, t.FeeTypeName, t.FeeTypeCode, d.FrequencyId, f.FrequencyName, d.Amount }).ToListAsync(ct);

            return rows.Select(r => new StudentFeeLineRow
            {
                FeeTypeId = r.FeeTypeId, FeeTypeName = r.FeeTypeName, FeeTypeCode = r.FeeTypeCode,
                FrequencyId = r.FrequencyId, FrequencyName = r.FrequencyName, Amount = r.Amount
            }).ToList();
        }

        public async Task<List<FeeStructureOptionRow>> GetActiveStructuresAsync(string schoolId, long academicYearId, CancellationToken ct)
        {
            var rows = await (
                from f in _db.FeeStructures.AsNoTracking()
                join c in _db.Classes.AsNoTracking() on f.ClassId equals c.ClassId
                where f.SchoolId == schoolId && f.AcademicYearId == academicYearId && f.IsActive && c.SchoolId == schoolId
                orderby c.SequenceOrder, f.StructureName
                select new { f.FeeStructureId, f.StructureName, c.ClassId, c.ClassName }).ToListAsync(ct);

            return rows.Select(r => new FeeStructureOptionRow
            {
                FeeStructureId = r.FeeStructureId, StructureName = r.StructureName, ClassId = r.ClassId, ClassName = r.ClassName
            }).ToList();
        }

        public async Task<List<AppliedConcessionRow>> GetAppliedConcessionsAsync(string schoolId, string studentId, CancellationToken ct)
        {
            var rows = await (
                from sc in _db.StudentFeeConcessions.AsNoTracking()
                join c in _db.FeeConcessions.AsNoTracking() on sc.ConcessionId equals c.ConcessionId
                where sc.StudentId == studentId && c.SchoolId == schoolId
                orderby sc.CreatedAt
                select new
                {
                    sc.StudentFeeConcessionId, sc.ConcessionId, c.ConcessionName, c.ConcessionCode, c.ConcessionType, c.Value,
                    c.ApplicableFeeTypeId, ConcessionIsActive = c.IsActive, sc.Remarks, sc.ApprovedBy, AppliedAt = sc.CreatedAt
                }).ToListAsync(ct);

            var feeTypeNames = await _db.FeeTypes.AsNoTracking().Where(t => t.SchoolId == schoolId)
                .ToDictionaryAsync(t => t.FeeTypeId, t => t.FeeTypeName, ct);
            var userIds = rows.Where(r => r.ApprovedBy != null).Select(r => r.ApprovedBy!.Value).Distinct().ToList();
            var userNames = userIds.Count == 0
                ? new Dictionary<long, string>()
                : await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.UserId))
                    .ToDictionaryAsync(u => u.UserId, u => u.DisplayName ?? u.OrgUserId ?? string.Empty, ct);

            return rows.Select(r => new AppliedConcessionRow
            {
                StudentFeeConcessionId = r.StudentFeeConcessionId, ConcessionId = r.ConcessionId, ConcessionName = r.ConcessionName,
                ConcessionCode = r.ConcessionCode, ConcessionType = r.ConcessionType, Value = r.Value,
                ApplicableFeeTypeId = r.ApplicableFeeTypeId,
                ApplicableFeeTypeName = r.ApplicableFeeTypeId is long ft && feeTypeNames.TryGetValue(ft, out var n) ? n : null,
                ConcessionIsActive = r.ConcessionIsActive, Remarks = r.Remarks,
                ApprovedByName = r.ApprovedBy is long u && userNames.TryGetValue(u, out var un) ? un : null,
                AppliedAt = r.AppliedAt
            }).ToList();
        }

        public Task<TbStudentFeeConcession?> GetAppliedConcessionAsync(string studentId, long concessionId, bool track, CancellationToken ct)
        {
            IQueryable<TbStudentFeeConcession> q = _db.StudentFeeConcessions.Where(x => x.StudentId == studentId && x.ConcessionId == concessionId);
            if (!track) q = q.AsNoTracking();
            return q.FirstOrDefaultAsync(ct);
        }

        public Task<TbFeeConcessions?> GetConcessionAsync(string schoolId, long concessionId, CancellationToken ct) =>
            _db.FeeConcessions.AsNoTracking().Where(c => c.SchoolId == schoolId && c.ConcessionId == concessionId).FirstOrDefaultAsync(ct);

        public async Task<List<ConcessionOptionRow>> GetActiveConcessionsAsync(string schoolId, CancellationToken ct)
        {
            var rows = await _db.FeeConcessions.AsNoTracking().Where(c => c.SchoolId == schoolId && c.IsActive).OrderBy(c => c.ConcessionName).ToListAsync(ct);
            var feeTypeNames = await _db.FeeTypes.AsNoTracking().Where(t => t.SchoolId == schoolId)
                .ToDictionaryAsync(t => t.FeeTypeId, t => t.FeeTypeName, ct);

            return rows.Select(c => new ConcessionOptionRow
            {
                ConcessionId = c.ConcessionId, ConcessionName = c.ConcessionName, ConcessionCode = c.ConcessionCode,
                ConcessionType = c.ConcessionType, Value = c.Value, ApplicableFeeTypeId = c.ApplicableFeeTypeId,
                ApplicableFeeTypeName = c.ApplicableFeeTypeId is long ft && feeTypeNames.TryGetValue(ft, out var n) ? n : null
            }).ToList();
        }

        public async Task AddAssignmentAsync(TbStudentFeeStructure entity, CancellationToken ct) =>
            await _db.StudentFeeStructures.AddAsync(entity, ct);

        public async Task AddAppliedConcessionAsync(TbStudentFeeConcession entity, CancellationToken ct) =>
            await _db.StudentFeeConcessions.AddAsync(entity, ct);

        public void RemoveAppliedConcession(TbStudentFeeConcession entity) => _db.StudentFeeConcessions.Remove(entity);

        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
    }
}
