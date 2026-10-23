using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class AssessmentRepository : IAssessmentRepository
    {
        private readonly dbContext _db;
        public AssessmentRepository(dbContext db) => _db = db;

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);

        public async Task LockAssessmentAsync(string schoolId, long assessmentId, CancellationToken ct)
        {
            var key = $"assessment:{schoolId}:{assessmentId}";
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }

        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == statusName && s.Stype == statusType).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public async Task<(string? LoginId, string? Name)> GetUserAsync(string schoolId, long userId, CancellationToken ct)
        {
            var u = await _db.Users.AsNoTracking().Where(x => x.UserId == userId && x.SchoolId == schoolId).Select(x => new { x.OrgUserId, Name = x.DisplayName ?? x.OrgUserId }).FirstOrDefaultAsync(ct);
            return (u?.OrgUserId, u?.Name);
        }

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

        public Task<List<AssessmentTypeRow>> GetTypesAsync(CancellationToken ct) =>
            _db.AssessmentTypes.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.AssessmentTypeId).Select(t => new AssessmentTypeRow(t.AssessmentTypeId, t.TypeName, t.TypeCode, t.IsActive)).ToListAsync(ct);

        public Task<AssessmentTypeRow?> GetTypeAsync(long assessmentTypeId, CancellationToken ct) =>
            _db.AssessmentTypes.AsNoTracking().Where(t => t.AssessmentTypeId == assessmentTypeId).Select(t => new AssessmentTypeRow(t.AssessmentTypeId, t.TypeName, t.TypeCode, t.IsActive)).FirstOrDefaultAsync(ct);

        public async Task<List<RosterStudentRow>> GetRosterAsync(string schoolId, long academicYearId, long sectionId, int activeStatusId, CancellationToken ct)
        {
            // Filtered by school on BOTH tables, so an enrollment row can never pull in another school's student (same query as Attendance)
            var rows = await (
                from e in _db.StudentEnrollments.AsNoTracking()
                join s in _db.Students.AsNoTracking() on e.StudentId equals s.StudentId
                where e.SchoolId == schoolId && s.SchoolId == schoolId && e.AcademicYearId == academicYearId && e.ClassSectionId == sectionId
                      && e.StatusId == activeStatusId && s.StatusId == activeStatusId
                select new { s.StudentId, s.FirstName, s.MiddleName, s.LastName, e.RollNumber }).ToListAsync(ct);
            return rows.Select(r => new RosterStudentRow(r.StudentId, r.FirstName, r.MiddleName, r.LastName, r.RollNumber))
                .OrderBy(r => int.TryParse(r.RollNumber, out var n) ? n : int.MaxValue).ThenBy(r => r.RollNumber, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.FirstName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.StudentId, StringComparer.Ordinal).ToList();
        }

        public Task<long?> GetStudentSectionAsync(string schoolId, string studentId, long academicYearId, int activeStatusId, CancellationToken ct) =>
            (from e in _db.StudentEnrollments.AsNoTracking()
             join s in _db.Students.AsNoTracking() on e.StudentId equals s.StudentId
             where e.SchoolId == schoolId && s.SchoolId == schoolId && e.StudentId == studentId && e.AcademicYearId == academicYearId && e.StatusId == activeStatusId && s.StatusId == activeStatusId
             select (long?)e.ClassSectionId).FirstOrDefaultAsync(ct);

        public async Task AddAssessmentAsync(TbClassAssessment assessment, CancellationToken ct) => await _db.ClassAssessments.AddAsync(assessment, ct);

        public Task<TbClassAssessment?> GetActiveForUpdateAsync(string schoolId, long assessmentId, int activeStatusId, CancellationToken ct) =>
            _db.ClassAssessments.AsTracking().Where(a => a.SchoolId == schoolId && a.AssessmentId == assessmentId && a.StatusId == activeStatusId).FirstOrDefaultAsync(ct);

        public Task<List<TbStudentAssessmentMark>> GetMarksForUpdateAsync(long assessmentId, CancellationToken ct) =>
            _db.StudentAssessmentMarks.AsTracking().Where(m => m.AssessmentId == assessmentId).ToListAsync(ct);

        public async Task AddMarksAsync(IEnumerable<TbStudentAssessmentMark> marks, CancellationToken ct) => await _db.StudentAssessmentMarks.AddRangeAsync(marks, ct);

        // The filter is applied to the entity BEFORE the projection into AssessmentRow: EF can't translate a Where over the projected record.
        private IQueryable<AssessmentRow> Rows(IQueryable<TbClassAssessment> source) =>
            from a in source
            join y in _db.AcademicYears on a.AcademicYearId equals y.AcademicYearId
            join s in _db.Sections on a.SectionId equals s.SectionId
            join c in _db.Classes on a.ClassId equals c.ClassId
            join sub in _db.Subjects on a.SubjectId equals sub.SubjectId
            join t in _db.AssessmentTypes on a.AssessmentTypeId equals t.AssessmentTypeId
            join e in _db.Employees on a.EmployeeId equals e.EmployeeId
            select new AssessmentRow(a.AssessmentId, a.AcademicYearId, y.YearName, a.ClassId, c.ClassName, a.SectionId, s.SectionName, a.SubjectId, sub.SubjectName, a.AssessmentTypeId, t.TypeName,
                a.EmployeeId, e.FirstName, e.MiddleName, e.LastName, a.Title, a.TestDate, a.MaxMarks, a.StatusId,
                _db.StudentAssessmentMarks.Count(m => m.AssessmentId == a.AssessmentId), _db.StudentAssessmentMarks.Count(m => m.AssessmentId == a.AssessmentId && m.IsAbsent));

        public Task<AssessmentRow?> GetRowAsync(string schoolId, long assessmentId, CancellationToken ct) =>
            Rows(_db.ClassAssessments.AsNoTracking().Where(a => a.SchoolId == schoolId && a.AssessmentId == assessmentId)).FirstOrDefaultAsync(ct);

        public async Task<List<AssessmentRow>> ListAsync(string schoolId, AssessmentFilter f, int activeStatusId, CancellationToken ct)
        {
            var q = _db.ClassAssessments.AsNoTracking().Where(a => a.SchoolId == schoolId && a.StatusId == activeStatusId);
            if (f.AcademicYearId != null) q = q.Where(a => a.AcademicYearId == f.AcademicYearId);
            if (f.SectionId != null) q = q.Where(a => a.SectionId == f.SectionId);
            if (f.SubjectId != null) q = q.Where(a => a.SubjectId == f.SubjectId);
            if (f.EmployeeId != null) q = q.Where(a => a.EmployeeId == f.EmployeeId);
            if (f.From != null) q = q.Where(a => a.TestDate >= f.From);
            if (f.To != null) q = q.Where(a => a.TestDate <= f.To);
            return await Rows(q).ToListAsync(ct);
        }

        public async Task<List<AssessmentMarkRow>> GetMarkRowsAsync(string schoolId, long assessmentId, long academicYearId, CancellationToken ct)
        {
            var rows = await (
                from m in _db.StudentAssessmentMarks.AsNoTracking()
                where m.AssessmentId == assessmentId
                join s in _db.Students.AsNoTracking() on m.StudentId equals s.StudentId
                where s.SchoolId == schoolId
                select new { m.StudentId, s.FirstName, s.MiddleName, s.LastName, m.ObtainedMarks, m.IsAbsent }).ToListAsync(ct);
            var ids = rows.Select(r => r.StudentId).ToList();
            var rolls = await _db.StudentEnrollments.AsNoTracking().Where(e => e.SchoolId == schoolId && e.AcademicYearId == academicYearId && ids.Contains(e.StudentId))
                .Select(e => new { e.StudentId, e.RollNumber }).ToDictionaryAsync(e => e.StudentId, e => e.RollNumber, ct);
            return rows.Select(r => new AssessmentMarkRow(r.StudentId, r.FirstName, r.MiddleName, r.LastName, rolls.GetValueOrDefault(r.StudentId), r.ObtainedMarks, r.IsAbsent))
                .OrderBy(r => int.TryParse(r.RollNumber, out var n) ? n : int.MaxValue).ThenBy(r => r.RollNumber, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.FirstName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.StudentId, StringComparer.Ordinal).ToList();
        }

        public async Task<List<StudentResultRow>> GetStudentResultsAsync(string schoolId, string studentId, long academicYearId, long sectionId, int activeStatusId, DateOnly? from, DateOnly? to, CancellationToken ct)
        {
            var tests = _db.ClassAssessments.AsNoTracking().Where(a => a.SchoolId == schoolId && a.StatusId == activeStatusId && a.AcademicYearId == academicYearId && a.SectionId == sectionId);
            if (from != null) tests = tests.Where(a => a.TestDate >= from);
            if (to != null) tests = tests.Where(a => a.TestDate <= to);
            // ONLY this student's own mark rows: nothing about anyone else is read
            return await (from m in _db.StudentAssessmentMarks.AsNoTracking().Where(x => x.StudentId == studentId)
                          join a in tests on m.AssessmentId equals a.AssessmentId
                          join t in _db.AssessmentTypes on a.AssessmentTypeId equals t.AssessmentTypeId
                          join sub in _db.Subjects on a.SubjectId equals sub.SubjectId
                          orderby a.TestDate descending, a.AssessmentId descending
                          select new StudentResultRow(a.AssessmentId, a.Title, t.TypeName, sub.SubjectName, a.TestDate, a.MaxMarks, m.ObtainedMarks, m.IsAbsent)).ToListAsync(ct);
        }
    }
}
