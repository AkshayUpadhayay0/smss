using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class TimetableRepository : ITimetableRepository
    {
        private readonly dbContext _db;
        public TimetableRepository(dbContext db) => _db = db;

        public Task<bool> IsTeachingSectionSubjectAsync(string schoolId, string employeeId, long sectionId, long subjectId, long academicYearId, int activeStatusId, CancellationToken ct) =>
            _db.TimetableEntries.AsNoTracking().AnyAsync(e => e.SchoolId == schoolId && e.EmployeeId == employeeId && e.SectionId == sectionId && e.SubjectId == subjectId
                                                              && e.AcademicYearId == academicYearId && e.StatusId == activeStatusId, ct);

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);

        public async Task LockSlotAsync(string schoolId, long academicYearId, short dayOfWeek, long periodId, CancellationToken ct)
        {
            var key = $"timetable:{schoolId}:{academicYearId}:{dayOfWeek}:{periodId}";
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == statusName && s.Stype == statusType).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public Task<YearRef?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.AcademicYearId == academicYearId).Select(y => new YearRef(y.AcademicYearId, y.YearName)).FirstOrDefaultAsync(ct);

        public Task<YearRef?> GetYearContainingAsync(string schoolId, DateOnly date, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.StartDate <= date && y.EndDate >= date)
                .OrderByDescending(y => y.IsCurrent).ThenByDescending(y => y.StartDate).Select(y => new YearRef(y.AcademicYearId, y.YearName)).FirstOrDefaultAsync(ct);

        public Task<SectionRef?> GetSectionAsync(string schoolId, long sectionId, CancellationToken ct) =>
            (from s in _db.Sections.AsNoTracking()
             join c in _db.Classes.AsNoTracking() on s.ClassId equals c.ClassId
             where s.SchoolId == schoolId && c.SchoolId == schoolId && s.SectionId == sectionId
             select new SectionRef(s.SectionId, s.ClassId, s.SectionName, c.ClassName, c.SequenceOrder, s.StatusId, c.StatusId)).FirstOrDefaultAsync(ct);

        public Task<TbPeriod?> GetPeriodAsync(string schoolId, long periodId, CancellationToken ct) =>
            _db.Periods.AsNoTracking().Where(p => p.SchoolId == schoolId && p.PeriodId == periodId).FirstOrDefaultAsync(ct);

        public async Task<List<TbPeriod>> GetActivePeriodsAsync(string schoolId, int activeStatusId, CancellationToken ct) =>
            (await _db.Periods.AsNoTracking().Where(p => p.SchoolId == schoolId && p.StatusId == activeStatusId).ToListAsync(ct)).OrderBy(p => p.PeriodNumber).ToList();

        public Task<TtSubjectRow?> GetSubjectAsync(string schoolId, long subjectId, long classId, int activeStatusId, CancellationToken ct) =>
            _db.Subjects.AsNoTracking().Where(s => s.SchoolId == schoolId && s.SubjectId == subjectId)
                .Select(s => new TtSubjectRow(s.SubjectId, s.SubjectName, s.SubjectCode, s.StatusId,
                    _db.ClassSubjects.Any(m => m.SchoolId == schoolId && m.ClassId == classId && m.SubjectId == s.SubjectId && m.StatusId == activeStatusId))).FirstOrDefaultAsync(ct);

        public async Task<List<TtSubjectRow>> GetSubjectsOfClassAsync(string schoolId, long classId, int activeStatusId, CancellationToken ct)
        {
            var rows = await (from m in _db.ClassSubjects.AsNoTracking()
                              join s in _db.Subjects.AsNoTracking() on m.SubjectId equals s.SubjectId
                              where m.SchoolId == schoolId && s.SchoolId == schoolId && m.ClassId == classId && m.StatusId == activeStatusId && s.StatusId == activeStatusId
                              select new TtSubjectRow(s.SubjectId, s.SubjectName, s.SubjectCode, s.StatusId, true)).ToListAsync(ct);
            return rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private IQueryable<TtTeacherRow> TeacherRows(IQueryable<TbEmployee> source) =>
            from e in source
            join d in _db.EmployeeDesignations.AsNoTracking() on e.DesignationId equals d.DesignationId
            select new TtTeacherRow(e.EmployeeId, e.FirstName, e.MiddleName, e.LastName, e.EmployeeType, e.StatusId,
                _db.LutEmploymentStatuses.Any(es => es.EmploymentStatusId == e.EmploymentStatusId && es.BlocksLogin), d.DesignationName);

        public Task<TtTeacherRow?> GetTeacherAsync(string schoolId, string employeeId, CancellationToken ct) =>
            TeacherRows(_db.Employees.AsNoTracking().Where(e => e.SchoolId == schoolId && e.EmployeeId == employeeId)).FirstOrDefaultAsync(ct);

        public async Task<List<TtTeacherRow>> GetTeachersAsync(string schoolId, int activeStatusId, CancellationToken ct)
        {
            var rows = await TeacherRows(_db.Employees.AsNoTracking().Where(e => e.SchoolId == schoolId && e.StatusId == activeStatusId && e.EmployeeType == "Teaching")).ToListAsync(ct);
            return rows.Where(r => !r.EmploymentBlocksLogin).OrderBy(r => r.FirstName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.LastName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public Task<TbTimetableEntry?> GetActiveSlotForUpdateAsync(string schoolId, long academicYearId, long sectionId, short dayOfWeek, long periodId, int activeStatusId, CancellationToken ct) =>
            _db.TimetableEntries.Where(e => e.SchoolId == schoolId && e.AcademicYearId == academicYearId && e.SectionId == sectionId && e.DayOfWeek == dayOfWeek
                && e.PeriodId == periodId && e.StatusId == activeStatusId).FirstOrDefaultAsync(ct);

        public Task<TtConflictRow?> FindTeacherConflictAsync(string schoolId, long academicYearId, string employeeId, short dayOfWeek, long periodId, long exceptSectionId, int activeStatusId, CancellationToken ct) =>
            (from e in _db.TimetableEntries.AsNoTracking()
             where e.SchoolId == schoolId && e.AcademicYearId == academicYearId && e.EmployeeId == employeeId && e.DayOfWeek == dayOfWeek && e.PeriodId == periodId
                   && e.StatusId == activeStatusId && e.SectionId != exceptSectionId
             join c in _db.Classes.AsNoTracking() on e.ClassId equals c.ClassId
             join s in _db.Sections.AsNoTracking() on e.SectionId equals s.SectionId
             join sub in _db.Subjects.AsNoTracking() on e.SubjectId equals sub.SubjectId
             join p in _db.Periods.AsNoTracking() on e.PeriodId equals p.PeriodId
             select new TtConflictRow(c.ClassName, s.SectionName, sub.SubjectName, p.PeriodNumber)).FirstOrDefaultAsync(ct);

        public async Task AddAsync(TbTimetableEntry entry, CancellationToken ct) => await _db.TimetableEntries.AddAsync(entry, ct);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

        private IQueryable<TtEntryRow> Rows(IQueryable<TbTimetableEntry> source) =>
            from e in source
            join c in _db.Classes.AsNoTracking() on e.ClassId equals c.ClassId
            join s in _db.Sections.AsNoTracking() on e.SectionId equals s.SectionId
            join sub in _db.Subjects.AsNoTracking() on e.SubjectId equals sub.SubjectId
            join emp in _db.Employees.AsNoTracking() on e.EmployeeId equals emp.EmployeeId
            join p in _db.Periods.AsNoTracking() on e.PeriodId equals p.PeriodId
            select new TtEntryRow(e, c.ClassName, s.SectionName, sub.SubjectName, sub.SubjectCode,
                emp.FirstName + " " + (emp.MiddleName == null ? "" : emp.MiddleName + " ") + emp.LastName, p.PeriodNumber);

        public Task<TtEntryRow?> GetEntryRowAsync(string schoolId, long entryId, CancellationToken ct) =>
            Rows(_db.TimetableEntries.AsNoTracking().Where(e => e.SchoolId == schoolId && e.TimetableEntryId == entryId)).FirstOrDefaultAsync(ct);

        public Task<List<TtEntryRow>> GetSectionEntriesAsync(string schoolId, long academicYearId, long sectionId, int activeStatusId, CancellationToken ct) =>
            Rows(_db.TimetableEntries.AsNoTracking().Where(e => e.SchoolId == schoolId && e.AcademicYearId == academicYearId && e.SectionId == sectionId && e.StatusId == activeStatusId)).ToListAsync(ct);

        public Task<List<TtEntryRow>> GetTeacherEntriesAsync(string schoolId, long academicYearId, string employeeId, int activeStatusId, CancellationToken ct) =>
            Rows(_db.TimetableEntries.AsNoTracking().Where(e => e.SchoolId == schoolId && e.AcademicYearId == academicYearId && e.EmployeeId == employeeId && e.StatusId == activeStatusId)).ToListAsync(ct);

        public Task<long?> GetStudentSectionAsync(string schoolId, string studentId, long academicYearId, int activeStatusId, CancellationToken ct) =>
            (from e in _db.StudentEnrollments.AsNoTracking()
             join s in _db.Students.AsNoTracking() on e.StudentId equals s.StudentId
             where e.SchoolId == schoolId && s.SchoolId == schoolId && e.StudentId == studentId && e.AcademicYearId == academicYearId && e.StatusId == activeStatusId && s.StatusId == activeStatusId
             select (long?)e.ClassSectionId).FirstOrDefaultAsync(ct);

        public Task<string?> GetUserLoginIdAsync(string schoolId, long userId, CancellationToken ct) =>
            _db.Users.AsNoTracking().Where(u => u.UserId == userId && u.SchoolId == schoolId).Select(u => u.OrgUserId).FirstOrDefaultAsync(ct);
    }
}
