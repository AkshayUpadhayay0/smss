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

        public Task<string?> GetStudentLoginIdAsync(string schoolId, string studentId, CancellationToken ct) =>
            _db.Users.AsNoTracking()
                .Where(u => u.SchoolId == schoolId && u.OrgUserId == studentId)
                .Select(u => u.OrgUserId)
                .FirstOrDefaultAsync(ct);

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

        public async Task<Dictionary<string, StudentFeeAggregate>> GetFeeAggregatesAsync(string schoolId, long academicYearId, IReadOnlyCollection<string> studentIds, DateOnly today, CancellationToken ct)
        {
            if (studentIds.Count == 0) return new();
            var ids = studentIds.ToArray();
            // one round trip however many students are on the page; school id on both tables
            // starts from the ITEMS (left join to their installments) so a student with items but no installments still shows up, with zero totals
            var rows = await (
                from i in _db.StudentFeeItems.AsNoTracking()
                where i.SchoolId == schoolId && i.AcademicYearId == academicYearId && ids.Contains(i.StudentId)
                join d in _db.StudentFeeDues.AsNoTracking() on i.StudentFeeItemId equals d.StudentFeeItemId into dg
                from d in dg.DefaultIfEmpty()
                group d by i.StudentId into g
                select new
                {
                    StudentId = g.Key,
                    TotalDue = g.Sum(x => x == null ? 0m : x.AmountDue),
                    TotalPaid = g.Sum(x => x == null ? 0m : x.AmountPaid),
                    Overdue = g.Count(x => x != null && x.AmountPaid < x.AmountDue && x.DueDate < today)
                }).ToListAsync(ct);
            return rows.ToDictionary(r => r.StudentId, r => new StudentFeeAggregate(r.StudentId, r.TotalDue, r.TotalPaid, r.Overdue));
        }

        public Task<TbStudent?> GetStudentAsync(string schoolId, string studentId, CancellationToken ct) =>
            _db.Students.AsNoTracking().Where(s => s.SchoolId == schoolId && s.StudentId == studentId).FirstOrDefaultAsync(ct);

        public Task<TbStudent?> GetStudentForUpdateAsync(string schoolId, string studentId, CancellationToken ct) =>
            _db.Students.Where(s => s.SchoolId == schoolId && s.StudentId == studentId).FirstOrDefaultAsync(ct);

        public Task<string?> GetSchoolCodeAsync(string schoolId, CancellationToken ct) =>
            _db.Schools.AsNoTracking().Where(s => s.SchoolId == schoolId).Select(s => s.SchoolCode).FirstOrDefaultAsync(ct);

        public Task<string?> GetUserLoginIdAsync(string schoolId, long userId, CancellationToken ct) =>
            _db.Users.AsNoTracking().Where(u => u.UserId == userId && u.SchoolId == schoolId).Select(u => u.OrgUserId).FirstOrDefaultAsync(ct);

        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

        public async Task<List<StudentEnrollmentRow>> GetEnrollmentsAsync(string schoolId, string studentId, CancellationToken ct)
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

        public async Task<List<StudentGuardianRow>> GetGuardiansAsync(string schoolId, string studentId, CancellationToken ct)
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
