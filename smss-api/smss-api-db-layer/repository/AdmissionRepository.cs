using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class AdmissionRepository : IAdmissionRepository
    {
        private const string AdmissionStatusType = "ADMISSION_STATUS";
        private const string GeneralStatusType = "general status";

        private readonly dbContext _db;
        public AdmissionRepository(dbContext db) => _db = db;

        public Task<List<TbAdmission>> GetAllAsync(string schoolId, CancellationToken ct) =>
            _db.Admissions.AsNoTracking()
                .Where(a => a.SchoolId == schoolId)
                .OrderByDescending(a => a.ApplicationDate).ThenByDescending(a => a.AdmissionId)
                .ToListAsync(ct);

        public Task<TbAdmission?> GetByIdAsync(string schoolId, long admissionId, bool track, CancellationToken ct)
        {
            IQueryable<TbAdmission> q = _db.Admissions.Where(a => a.AdmissionId == admissionId && a.SchoolId == schoolId);
            if (!track) q = q.AsNoTracking();
            return q.FirstOrDefaultAsync(ct);
        }

        public async Task AddAdmissionAsync(TbAdmission entity, CancellationToken ct) => await _db.Admissions.AddAsync(entity, ct);

        // ---- display names ----
        public Task<Dictionary<long, string>> GetClassNamesAsync(string schoolId, IReadOnlyCollection<long> ids, CancellationToken ct) =>
            _db.Classes.AsNoTracking().Where(c => c.SchoolId == schoolId && ids.Contains(c.ClassId))
                .ToDictionaryAsync(c => c.ClassId, c => c.ClassName, ct);

        public Task<Dictionary<long, string>> GetAdmissionTypeNamesAsync(string schoolId, IReadOnlyCollection<long> ids, CancellationToken ct) =>
            _db.AdmissionTypes.AsNoTracking().Where(t => t.SchoolId == schoolId && ids.Contains(t.AdmissionTypeId))
                .ToDictionaryAsync(t => t.AdmissionTypeId, t => t.TypeName, ct);

        public Task<Dictionary<long, string>> GetAcademicYearNamesAsync(string schoolId, IReadOnlyCollection<long> ids, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && ids.Contains(y.AcademicYearId))
                .ToDictionaryAsync(y => y.AcademicYearId, y => y.YearName, ct);

        public Task<string?> GetGenderNameAsync(long id, CancellationToken ct) =>
            _db.LutGenders.AsNoTracking().Where(g => g.GenderId == id).Select(g => g.GenderName).FirstOrDefaultAsync(ct);

        public Task<string?> GetBloodGroupNameAsync(long id, CancellationToken ct) =>
            _db.LutBloodGroups.AsNoTracking().Where(g => g.BloodGroupId == id).Select(g => g.BloodGroupName).FirstOrDefaultAsync(ct);

        public Task<string?> GetStudentCategoryNameAsync(long id, CancellationToken ct) =>
            _db.LutStudentCategories.AsNoTracking().Where(c => c.StudentCategoryId == id).Select(c => c.CategoryName).FirstOrDefaultAsync(ct);

        // ---- ownership checks ----
        public Task<bool> AcademicYearBelongsToSchoolAsync(string schoolId, long id, CancellationToken ct) =>
            _db.AcademicYears.AnyAsync(y => y.SchoolId == schoolId && y.AcademicYearId == id, ct);

        public Task<bool> ClassBelongsToSchoolAsync(string schoolId, long id, CancellationToken ct) =>
            _db.Classes.AnyAsync(c => c.SchoolId == schoolId && c.ClassId == id, ct);

        public Task<bool> AdmissionTypeBelongsToSchoolAsync(string schoolId, long id, CancellationToken ct) =>
            _db.AdmissionTypes.AnyAsync(t => t.SchoolId == schoolId && t.AdmissionTypeId == id, ct);

        public Task<TbAdmissionTypes?> GetAdmissionTypeByNameAsync(string schoolId, string name, CancellationToken ct)
        {
            var wanted = name.Trim().ToLower();
            return _db.AdmissionTypes.AsNoTracking()
                .Where(t => t.SchoolId == schoolId && t.TypeName.ToLower() == wanted)
                .FirstOrDefaultAsync(ct);
        }

        public Task<bool> SectionBelongsToSchoolAsync(string schoolId, long sectionId, CancellationToken ct) =>
            _db.Sections.AnyAsync(s => s.SchoolId == schoolId && s.SectionId == sectionId, ct);

        public Task<bool> GenderExistsAsync(long id, CancellationToken ct) => _db.LutGenders.AnyAsync(g => g.GenderId == id, ct);
        public Task<bool> BloodGroupExistsAsync(long id, CancellationToken ct) => _db.LutBloodGroups.AnyAsync(g => g.BloodGroupId == id, ct);
        public Task<bool> StudentCategoryExistsAsync(long id, CancellationToken ct) => _db.LutStudentCategories.AnyAsync(c => c.StudentCategoryId == id, ct);

        // ---- statuses ----
        public Task<Dictionary<int, string>> GetAdmissionStatusNamesAsync(CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Stype == AdmissionStatusType).ToDictionaryAsync(s => s.Sid, s => s.Sname, ct);

        public Task<int?> GetAdmissionStatusIdAsync(string name, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == name && s.Stype == AdmissionStatusType).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public Task<int?> GetGeneralStatusIdAsync(string name, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == name && s.Stype == GeneralStatusType).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        // ---- running numbers: the largest existing suffix + 1 (a count would reuse a number after any gap) ----
        public async Task<int> GetMaxApplicationSequenceAsync(string schoolId, string prefix, CancellationToken ct)
        {
            var existing = await _db.Admissions.AsNoTracking()
                .Where(a => a.SchoolId == schoolId && a.ApplicationNumber.StartsWith(prefix))
                .Select(a => a.ApplicationNumber).ToListAsync(ct);
            return existing.Select(n => int.TryParse(n[prefix.Length..], out var v) ? v : 0).DefaultIfEmpty(0).Max();
        }

        public async Task<int> GetMaxAdmissionNumberSequenceAsync(string schoolId, string prefix, CancellationToken ct)
        {
            var existing = await _db.Students.AsNoTracking()
                .Where(s => s.SchoolId == schoolId && s.AdmissionNumber.StartsWith(prefix))
                .Select(s => s.AdmissionNumber).ToListAsync(ct);
            return existing.Select(n => int.TryParse(n[prefix.Length..], out var v) ? v : 0).DefaultIfEmpty(0).Max();
        }

        // ---- confirm-admission writes ----
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);
        public async Task AddStudentAsync(TbStudent entity, CancellationToken ct) => await _db.Students.AddAsync(entity, ct);
        public async Task AddEnrollmentAsync(TbStudentEnrollment entity, CancellationToken ct) => await _db.StudentEnrollments.AddAsync(entity, ct);
        public async Task AddGuardianAsync(TbGuardian entity, CancellationToken ct) => await _db.Guardians.AddAsync(entity, ct);
        public async Task AddStudentGuardianAsync(TbStudentGuardian entity, CancellationToken ct) => await _db.StudentGuardians.AddAsync(entity, ct);

        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

        public void ClearTracking() => _db.ChangeTracker.Clear();

        public Task<SchoolIdentity?> GetSchoolIdentityAsync(string schoolId, CancellationToken ct) =>
            _db.Schools.AsNoTracking()
                .Where(s => s.SchoolId == schoolId)
                .Select(s => new SchoolIdentity(s.SchoolName, s.SchoolShortCode))
                .FirstOrDefaultAsync(ct);

        public async Task<SectionClassInfo?> GetSectionClassAsync(string schoolId, long sectionId, CancellationToken ct)
        {
            var row = await (
                from s in _db.Sections.AsNoTracking()
                join c in _db.Classes.AsNoTracking() on s.ClassId equals c.ClassId
                where s.SectionId == sectionId && s.SchoolId == schoolId && c.SchoolId == schoolId
                select new { c.ClassId, c.ClassName }).FirstOrDefaultAsync(ct);
            return row == null ? null : new SectionClassInfo(row.ClassId, row.ClassName);
        }

        public async Task<FeeStructureRef?> FindActiveFeeStructureAsync(string schoolId, long classId, long academicYearId, CancellationToken ct)
        {
            var row = await _db.FeeStructures.AsNoTracking()
                .Where(f => f.SchoolId == schoolId && f.ClassId == classId && f.AcademicYearId == academicYearId && f.IsActive)
                .Select(f => new { f.FeeStructureId, f.StructureName })
                .FirstOrDefaultAsync(ct);
            return row == null ? null : new FeeStructureRef(row.FeeStructureId, row.StructureName);
        }

        public async Task AddStudentFeeStructureAsync(TbStudentFeeStructure entity, CancellationToken ct) =>
            await _db.StudentFeeStructures.AddAsync(entity, ct);

        public Task<int> CountStudentsAsync(string schoolId, CancellationToken ct) =>
            _db.Students.CountAsync(s => s.SchoolId == schoolId, ct);
    }
}
