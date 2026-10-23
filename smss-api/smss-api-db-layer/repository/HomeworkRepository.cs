using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class HomeworkRepository : IHomeworkRepository
    {
        private readonly dbContext _db;
        public HomeworkRepository(dbContext db) => _db = db;

        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == statusName && s.Stype == statusType).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public async Task<(string? LoginId, string? Name)> GetUserAsync(string schoolId, long userId, CancellationToken ct)
        {
            var u = await _db.Users.AsNoTracking().Where(x => x.UserId == userId && x.SchoolId == schoolId).Select(x => new { x.OrgUserId, Name = x.DisplayName ?? x.OrgUserId }).FirstOrDefaultAsync(ct);
            return (u?.OrgUserId, u?.Name);
        }

        public Task<HomeworkSchoolRow?> GetSchoolAsync(string schoolId, CancellationToken ct) =>
            _db.Schools.AsNoTracking().Where(s => s.SchoolId == schoolId).Select(s => new HomeworkSchoolRow(s.SchoolCode)).FirstOrDefaultAsync(ct);

        public Task<YearRef?> GetYearContainingAsync(string schoolId, DateOnly date, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.StartDate <= date && y.EndDate >= date)
                .OrderByDescending(y => y.IsCurrent).ThenByDescending(y => y.StartDate).Select(y => new YearRef(y.AcademicYearId, y.YearName)).FirstOrDefaultAsync(ct);

        public Task<YearRef?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.AcademicYearId == academicYearId).Select(y => new YearRef(y.AcademicYearId, y.YearName)).FirstOrDefaultAsync(ct);

        public Task<SectionRef?> GetSectionAsync(string schoolId, long sectionId, CancellationToken ct) =>
            (from s in _db.Sections.AsNoTracking()
             join c in _db.Classes.AsNoTracking() on s.ClassId equals c.ClassId
             where s.SchoolId == schoolId && c.SchoolId == schoolId && s.SectionId == sectionId
             select new SectionRef(s.SectionId, s.ClassId, s.SectionName, c.ClassName, c.SequenceOrder, s.StatusId, c.StatusId)).FirstOrDefaultAsync(ct);

        public Task<HomeworkSubjectRow?> GetSubjectAsync(string schoolId, long subjectId, long classId, int activeStatusId, CancellationToken ct) =>
            _db.Subjects.AsNoTracking().Where(s => s.SchoolId == schoolId && s.SubjectId == subjectId)
                .Select(s => new HomeworkSubjectRow(s.SubjectId, s.SubjectName, s.StatusId,
                    _db.ClassSubjects.Any(m => m.SchoolId == schoolId && m.ClassId == classId && m.SubjectId == s.SubjectId && m.StatusId == activeStatusId))).FirstOrDefaultAsync(ct);

        public Task<HomeworkTeacherRow?> GetEmployeeAsync(string schoolId, string employeeId, CancellationToken ct) =>
            _db.Employees.AsNoTracking().Where(e => e.SchoolId == schoolId && e.EmployeeId == employeeId)
                .Select(e => new HomeworkTeacherRow(e.EmployeeId, e.FirstName, e.MiddleName, e.LastName, e.StatusId, e.EmployeeType,
                    _db.LutEmploymentStatuses.Any(es => es.EmploymentStatusId == e.EmploymentStatusId && es.BlocksLogin))).FirstOrDefaultAsync(ct);

        public Task<long?> GetStudentSectionAsync(string schoolId, string studentId, long academicYearId, int activeStatusId, CancellationToken ct) =>
            (from e in _db.StudentEnrollments.AsNoTracking()
             join s in _db.Students.AsNoTracking() on e.StudentId equals s.StudentId
             where e.SchoolId == schoolId && s.SchoolId == schoolId && e.StudentId == studentId && e.AcademicYearId == academicYearId && e.StatusId == activeStatusId && s.StatusId == activeStatusId
             select (long?)e.ClassSectionId).FirstOrDefaultAsync(ct);

        public async Task AddAsync(TbHomework homework, CancellationToken ct) => await _db.Homework.AddAsync(homework, ct);

        public Task<TbHomework?> GetActiveForUpdateAsync(string schoolId, long homeworkId, int activeStatusId, CancellationToken ct) =>
            _db.Homework.AsTracking().Where(h => h.SchoolId == schoolId && h.HomeworkId == homeworkId && h.StatusId == activeStatusId).FirstOrDefaultAsync(ct);

        // The filter is applied to the entity BEFORE the projection into HomeworkRow: EF can't translate a Where over the projected record.
        private IQueryable<HomeworkRow> Rows(IQueryable<TbHomework> source) =>
            from h in source
            join y in _db.AcademicYears on h.AcademicYearId equals y.AcademicYearId
            join s in _db.Sections on h.SectionId equals s.SectionId
            join c in _db.Classes on h.ClassId equals c.ClassId
            join sub in _db.Subjects on h.SubjectId equals sub.SubjectId
            join e in _db.Employees on h.EmployeeId equals e.EmployeeId
            select new HomeworkRow(h.HomeworkId, h.AcademicYearId, y.YearName, h.ClassId, c.ClassName, h.SectionId, s.SectionName, h.SubjectId, sub.SubjectName,
                h.EmployeeId, e.FirstName, e.MiddleName, e.LastName, h.Title, h.Description, h.AssignedDate, h.DueDate, h.AttachmentPath, h.StatusId);

        public Task<HomeworkRow?> GetRowAsync(string schoolId, long homeworkId, CancellationToken ct) =>
            Rows(_db.Homework.AsNoTracking().Where(h => h.SchoolId == schoolId && h.HomeworkId == homeworkId)).FirstOrDefaultAsync(ct);

        public async Task<List<HomeworkRow>> ListAsync(string schoolId, HomeworkFilter f, int activeStatusId, CancellationToken ct)
        {
            var q = _db.Homework.AsNoTracking().Where(h => h.SchoolId == schoolId && h.StatusId == activeStatusId);
            if (f.AcademicYearId != null) q = q.Where(h => h.AcademicYearId == f.AcademicYearId);
            if (f.SectionId != null) q = q.Where(h => h.SectionId == f.SectionId);
            if (f.SubjectId != null) q = q.Where(h => h.SubjectId == f.SubjectId);
            if (f.EmployeeId != null) q = q.Where(h => h.EmployeeId == f.EmployeeId);
            if (f.From != null) q = q.Where(h => h.DueDate >= f.From);
            if (f.To != null) q = q.Where(h => h.DueDate <= f.To);
            return await Rows(q).ToListAsync(ct);
        }
    }
}
