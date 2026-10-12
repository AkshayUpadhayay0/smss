using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class StudentRepository : IStudentRepository
    {
        private const string GeneralStatusType = "general status";

        private readonly dbContext _db;
        public StudentRepository(dbContext db) => _db = db;

        public Task<bool> AcademicYearBelongsToSchoolAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            _db.AcademicYears.AnyAsync(y => y.SchoolId == schoolId && y.AcademicYearId == academicYearId, ct);

        public Task<bool> SectionBelongsToSchoolAsync(string schoolId, long sectionId, CancellationToken ct) =>
            _db.Sections.AnyAsync(s => s.SchoolId == schoolId && s.SectionId == sectionId, ct);

        // Filtered by school on BOTH tables, so an enrollment row can never pull in another school's student
        public async Task<List<StudentListRow>> GetEnrolledStudentsAsync(string schoolId, long academicYearId, long sectionId, CancellationToken ct)
        {
            var rows = await (
                from e in _db.StudentEnrollments.AsNoTracking()
                join s in _db.Students.AsNoTracking() on e.StudentId equals s.StudentId
                where e.SchoolId == schoolId && s.SchoolId == schoolId
                      && e.AcademicYearId == academicYearId && e.ClassSectionId == sectionId
                select new
                {
                    s.StudentId, s.AdmissionNumber, s.FirstName, s.MiddleName, s.LastName,
                    e.RollNumber, s.GenderId, s.StatusId, e.EnrollmentId, s.ProfilePhotoUrl
                }).ToListAsync(ct);

            return rows.Select(r => new StudentListRow(r.StudentId, r.AdmissionNumber, r.FirstName, r.MiddleName, r.LastName,
                r.RollNumber, r.GenderId, r.StatusId, r.EnrollmentId, r.ProfilePhotoUrl)).ToList();
        }

        public Task<TbStudent?> GetStudentAsync(string schoolId, long studentId, CancellationToken ct) =>
            _db.Students.AsNoTracking().Where(s => s.SchoolId == schoolId && s.StudentId == studentId).FirstOrDefaultAsync(ct);

        public Task<TbStudent?> GetStudentForUpdateAsync(string schoolId, long studentId, CancellationToken ct) =>
            _db.Students.Where(s => s.SchoolId == schoolId && s.StudentId == studentId).FirstOrDefaultAsync(ct);

        public Task<string?> GetSchoolCodeAsync(string schoolId, CancellationToken ct) =>
            _db.Schools.AsNoTracking().Where(s => s.SchoolId == schoolId).Select(s => s.SchoolCode).FirstOrDefaultAsync(ct);

        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

        public async Task<List<StudentEnrollmentRow>> GetEnrollmentsAsync(string schoolId, long studentId, CancellationToken ct)
        {
            var rows = await (
                from e in _db.StudentEnrollments.AsNoTracking()
                join y in _db.AcademicYears.AsNoTracking() on e.AcademicYearId equals y.AcademicYearId
                join sec in _db.Sections.AsNoTracking() on e.ClassSectionId equals sec.SectionId
                join c in _db.Classes.AsNoTracking() on sec.ClassId equals c.ClassId
                where e.SchoolId == schoolId && e.StudentId == studentId
                      && y.SchoolId == schoolId && sec.SchoolId == schoolId && c.SchoolId == schoolId
                select new
                {
                    e.EnrollmentId, e.RollNumber, e.StatusId, e.AcademicYearId, y.YearName, y.StartDate, y.IsCurrent,
                    c.ClassName, sec.SectionName
                }).ToListAsync(ct);

            return rows.Select(r => new StudentEnrollmentRow(r.EnrollmentId, r.RollNumber, r.StatusId, r.AcademicYearId, r.YearName,
                r.StartDate, r.IsCurrent, r.ClassName, r.SectionName)).ToList();
        }

        public async Task<List<StudentGuardianRow>> GetGuardiansAsync(string schoolId, long studentId, CancellationToken ct)
        {
            var rows = await (
                from sg in _db.StudentGuardians.AsNoTracking()
                join g in _db.Guardians.AsNoTracking() on sg.GuardianId equals g.GuardianId
                where sg.StudentId == studentId && g.SchoolId == schoolId
                select new
                {
                    g.GuardianId, g.FirstName, g.LastName, sg.Relationship, g.MobileNumber, g.Email,
                    sg.IsPrimary, sg.IsEmergencyContact, sg.CanPickupStudent
                }).ToListAsync(ct);

            return rows.Select(r => new StudentGuardianRow(r.GuardianId, r.FirstName, r.LastName, r.Relationship, r.MobileNumber,
                r.Email, r.IsPrimary, r.IsEmergencyContact, r.CanPickupStudent)).ToList();
        }

        public Task<Dictionary<int, string>> GetGeneralStatusNamesAsync(CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Stype == GeneralStatusType).ToDictionaryAsync(s => s.Sid, s => s.Sname, ct);

        public Task<Dictionary<long, string>> GetGenderNamesAsync(CancellationToken ct) =>
            _db.LutGenders.AsNoTracking().ToDictionaryAsync(g => g.GenderId, g => g.GenderName, ct);

        public Task<string?> GetBloodGroupNameAsync(long id, CancellationToken ct) =>
            _db.LutBloodGroups.AsNoTracking().Where(g => g.BloodGroupId == id).Select(g => g.BloodGroupName).FirstOrDefaultAsync(ct);

        public Task<string?> GetStudentCategoryNameAsync(long id, CancellationToken ct) =>
            _db.LutStudentCategories.AsNoTracking().Where(c => c.StudentCategoryId == id).Select(c => c.CategoryName).FirstOrDefaultAsync(ct);

        public Task<string?> GetAdmissionTypeNameAsync(string schoolId, long id, CancellationToken ct) =>
            _db.AdmissionTypes.AsNoTracking().Where(t => t.SchoolId == schoolId && t.AdmissionTypeId == id)
                .Select(t => t.TypeName).FirstOrDefaultAsync(ct);
    }
}
