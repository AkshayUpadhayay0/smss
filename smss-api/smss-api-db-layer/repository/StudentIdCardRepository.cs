using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class StudentIdCardRepository : IStudentIdCardRepository
    {
        private readonly dbContext _db;
        public StudentIdCardRepository(dbContext db) => _db = db;

        public async Task<IdCardSchoolRow?> GetSchoolAsync(string schoolId, CancellationToken ct)
        {
            var s = await _db.Schools.AsNoTracking().Where(x => x.SchoolId == schoolId).FirstOrDefaultAsync(ct);
            if (s == null) return null;
            string? city = null, state = null;
            if (s.CityId.HasValue && s.CountryId.HasValue && s.StateId.HasValue && s.DistrictId.HasValue)
                city = await _db.LutCities.AsNoTracking()
                    .Where(c => c.Cid == s.CountryId && c.Sid == s.StateId && c.Did == s.DistrictId && c.CityId == s.CityId)
                    .Select(c => c.CityName).FirstOrDefaultAsync(ct);
            if (s.CountryId.HasValue && s.StateId.HasValue)
                state = await _db.LutStates.AsNoTracking()
                    .Where(c => c.Cid == s.CountryId && c.Sid == s.StateId)
                    .Select(c => c.Sname).FirstOrDefaultAsync(ct);
            return new IdCardSchoolRow(s.SchoolId, s.SchoolCode, s.SchoolName, s.LogoUrl, s.AddressLine1, s.AddressLine2, city, state, s.Pincode, s.MobileNumber);
        }

        public Task<IdCardYearRow?> GetCurrentYearAsync(string schoolId, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.IsCurrent)
                .Select(y => new IdCardYearRow(y.AcademicYearId, y.YearName)).FirstOrDefaultAsync(ct);

        public Task<bool> SectionBelongsToSchoolAsync(string schoolId, long sectionId, CancellationToken ct) =>
            _db.Sections.AsNoTracking().AnyAsync(s => s.SchoolId == schoolId && s.SectionId == sectionId, ct);

        // School id on every table in the join, so a row of another school can never be pulled in
        private IQueryable<IdCardStudentRow> Rows(string schoolId, long academicYearId, long? sectionId, string? studentId) =>
            from e in _db.StudentEnrollments.AsNoTracking()
            join s in _db.Students.AsNoTracking() on e.StudentId equals s.StudentId
            join sec in _db.Sections.AsNoTracking() on e.ClassSectionId equals sec.SectionId
            join c in _db.Classes.AsNoTracking() on sec.ClassId equals c.ClassId
            where e.SchoolId == schoolId && s.SchoolId == schoolId && sec.SchoolId == schoolId && c.SchoolId == schoolId
                  && e.AcademicYearId == academicYearId
                  && (sectionId == null || e.ClassSectionId == sectionId)
                  && (studentId == null || e.StudentId == studentId)
            select new IdCardStudentRow(s.StudentId, s.AdmissionNumber, s.FirstName, s.MiddleName, s.LastName, e.RollNumber,
                s.ProfilePhotoUrl, s.StatusId, e.StatusId, c.ClassName, sec.SectionName,
                s.DateOfBirth, s.BloodGroupId, s.MobileNumber, s.AddressLine1, s.AddressLine2, s.CountryId, s.StateId, s.DistrictId, s.CityId, s.Pincode);

        public Task<List<IdCardStudentRow>> GetSectionStudentsAsync(string schoolId, long academicYearId, long sectionId, CancellationToken ct) =>
            Rows(schoolId, academicYearId, sectionId, null).ToListAsync(ct);

        public Task<IdCardStudentRow?> GetStudentAsync(string schoolId, long academicYearId, string studentId, CancellationToken ct) =>
            Rows(schoolId, academicYearId, null, studentId).FirstOrDefaultAsync(ct);

        public Task<bool> StudentExistsAsync(string schoolId, string studentId, CancellationToken ct) =>
            _db.Students.AsNoTracking().AnyAsync(s => s.SchoolId == schoolId && s.StudentId == studentId, ct);

        public async Task<List<IdCardGuardianRow>> GetGuardiansAsync(string schoolId, IReadOnlyCollection<string> studentIds, CancellationToken ct)
        {
            if (studentIds.Count == 0) return new();
            var ids = studentIds.ToArray();
            var rows = await (
                from sg in _db.StudentGuardians.AsNoTracking()
                join g in _db.Guardians.AsNoTracking() on sg.GuardianId equals g.GuardianId
                where ids.Contains(sg.StudentId) && g.SchoolId == schoolId
                orderby sg.IsPrimary descending, g.GuardianId
                select new { sg.StudentId, g.FirstName, g.LastName, sg.Relationship, g.MobileNumber, sg.IsPrimary }).ToListAsync(ct);
            return rows.Select(r => new IdCardGuardianRow(r.StudentId,
                string.Join(' ', new[] { r.FirstName, r.LastName }.Where(p => !string.IsNullOrWhiteSpace(p))), r.Relationship, r.MobileNumber, r.IsPrimary)).ToList();
        }

        public async Task<Dictionary<int, string>> GetBloodGroupNamesAsync(CancellationToken ct) =>
            (await _db.LutBloodGroups.AsNoTracking().ToListAsync(ct)).ToDictionary(b => (int)b.BloodGroupId, b => b.BloodGroupName);

        public async Task<Dictionary<(int, int, int, int), string>> GetCityNamesAsync(IReadOnlyCollection<int> cityIds, CancellationToken ct)
        {
            if (cityIds.Count == 0) return new();
            var ids = cityIds.ToArray();
            var rows = await _db.LutCities.AsNoTracking().Where(c => ids.Contains(c.CityId)).ToListAsync(ct);
            return rows.ToDictionary(c => (c.Cid, c.Sid, c.Did, c.CityId), c => c.CityName);
        }

        public Task<int?> GetActiveStatusIdAsync(CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Stype == "general status" && s.Sname == "Active").Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);
    }
}
