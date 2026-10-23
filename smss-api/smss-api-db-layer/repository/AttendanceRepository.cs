using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class AttendanceRepository : IAttendanceRepository
    {
        private readonly dbContext _db;
        public AttendanceRepository(dbContext db) => _db = db;

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);

        public async Task LockSectionDayAsync(string schoolId, long sectionId, DateOnly date, CancellationToken ct)
        {
            var key = $"attendance:{schoolId}:{sectionId}:{date:yyyy-MM-dd}";
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == statusName && s.Stype == statusType).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

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

        public Task<List<AttendanceStatusRow>> GetAttendanceStatusesAsync(CancellationToken ct) =>
            _db.LutAttendanceStatuses.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.AttendanceStatusId)
                .Select(s => new AttendanceStatusRow(s.AttendanceStatusId, s.StatusCode, s.StatusName)).ToListAsync(ct);

        public async Task<List<RosterStudentRow>> GetRosterAsync(string schoolId, long academicYearId, long sectionId, int activeStatusId, CancellationToken ct)
        {
            // Filtered by school on BOTH tables, so an enrollment row can never pull in another school's student
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

        public async Task<List<MarkRow>> GetMarksAsync(string schoolId, long sectionId, DateOnly date, CancellationToken ct)
        {
            var rows = await (
                from a in _db.StudentAttendance.AsNoTracking()
                where a.SchoolId == schoolId && a.SectionId == sectionId && a.AttendanceDate == date
                join st in _db.LutAttendanceStatuses.AsNoTracking() on a.AttendanceStatusId equals st.AttendanceStatusId
                select new
                {
                    a, st.StatusCode, st.StatusName,
                    MarkedByName = _db.Users.Where(u => u.UserId == a.MarkedBy).Select(u => u.DisplayName ?? u.OrgUserId).FirstOrDefault(),
                    CorrectedByName = _db.Users.Where(u => u.UserId == a.CorrectedBy).Select(u => u.DisplayName ?? u.OrgUserId).FirstOrDefault()
                }).ToListAsync(ct);
            return rows.Select(r => new MarkRow(r.a, r.StatusCode, r.StatusName, r.MarkedByName, r.CorrectedByName)).ToList();
        }

        public Task<List<TbStudentAttendance>> GetExistingForUpdateAsync(string schoolId, DateOnly date, IReadOnlyCollection<string> studentIds, CancellationToken ct)
        {
            var ids = studentIds.ToArray();
            return _db.StudentAttendance.Where(a => a.SchoolId == schoolId && a.AttendanceDate == date && ids.Contains(a.StudentId)).ToListAsync(ct);
        }

        public Task<TbStudentAttendance?> GetMarkForUpdateAsync(string schoolId, string studentId, DateOnly date, CancellationToken ct) =>
            _db.StudentAttendance.Where(a => a.SchoolId == schoolId && a.StudentId == studentId && a.AttendanceDate == date).FirstOrDefaultAsync(ct);

        public Task AddRangeAsync(IEnumerable<TbStudentAttendance> marks, CancellationToken ct) => _db.StudentAttendance.AddRangeAsync(marks, ct);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

        public Task<StudentRef?> GetStudentAsync(string schoolId, string studentId, CancellationToken ct) =>
            _db.Students.AsNoTracking().Where(s => s.SchoolId == schoolId && s.StudentId == studentId)
                .Select(s => new StudentRef(s.StudentId, s.FirstName, s.MiddleName, s.LastName)).FirstOrDefaultAsync(ct);

        public Task<long?> GetStudentSectionAsync(string schoolId, string studentId, long academicYearId, CancellationToken ct) =>
            _db.StudentEnrollments.AsNoTracking().Where(e => e.SchoolId == schoolId && e.StudentId == studentId && e.AcademicYearId == academicYearId)
                .Select(e => (long?)e.ClassSectionId).FirstOrDefaultAsync(ct);

        public async Task<EnrollmentRef?> GetStudentEnrollmentAsync(string schoolId, string studentId, long academicYearId, CancellationToken ct)
        {
            var e = await _db.StudentEnrollments.AsNoTracking().Where(x => x.SchoolId == schoolId && x.StudentId == studentId && x.AcademicYearId == academicYearId)
                .Select(x => new { x.ClassSectionId, x.EnrollmentDate }).FirstOrDefaultAsync(ct);
            return e == null ? null : new EnrollmentRef(e.ClassSectionId, DateOnly.FromDateTime(e.EnrollmentDate));
        }

        public async Task<List<StudentDayRow>> GetStudentDaysAsync(string schoolId, string studentId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
        {
            var rows = await (
                from a in _db.StudentAttendance.AsNoTracking()
                where a.SchoolId == schoolId && a.StudentId == studentId && a.AttendanceDate >= fromDate && a.AttendanceDate <= toDate
                join st in _db.LutAttendanceStatuses.AsNoTracking() on a.AttendanceStatusId equals st.AttendanceStatusId
                join c in _db.Classes.AsNoTracking() on a.ClassId equals c.ClassId
                join s in _db.Sections.AsNoTracking() on a.SectionId equals s.SectionId
                orderby a.AttendanceDate
                select new { a.AttendanceDate, a.SectionId, c.ClassName, s.SectionName, st.StatusCode, st.StatusName, a.CorrectedAt, a.CorrectionReason }).ToListAsync(ct);
            return rows.Select(r => new StudentDayRow(r.AttendanceDate, r.SectionId, r.ClassName, r.SectionName, r.StatusCode, r.StatusName, r.CorrectedAt, r.CorrectionReason)).ToList();
        }

        public async Task<List<UnmarkedSectionRow>> GetUnmarkedSectionsAsync(string schoolId, long academicYearId, DateOnly date, int activeStatusId, CancellationToken ct)
        {
            var rows = await (
                from s in _db.Sections.AsNoTracking()
                join c in _db.Classes.AsNoTracking() on s.ClassId equals c.ClassId
                where s.SchoolId == schoolId && c.SchoolId == schoolId && s.StatusId == activeStatusId && c.StatusId == activeStatusId
                let roster = (from e in _db.StudentEnrollments
                              join st in _db.Students on e.StudentId equals st.StudentId
                              where e.SchoolId == schoolId && e.AcademicYearId == academicYearId && e.ClassSectionId == s.SectionId && e.StatusId == activeStatusId && st.StatusId == activeStatusId
                              select e.EnrollmentId).Count()
                where roster > 0 && !_db.StudentAttendance.Any(x => x.SchoolId == schoolId && x.SectionId == s.SectionId && x.AttendanceDate == date)
                join a in _db.ClassTeacherAssignments.AsNoTracking().Where(x => x.SchoolId == schoolId && x.AcademicYearId == academicYearId && x.StatusId == activeStatusId)
                    on s.SectionId equals a.SectionId into ag
                from a in ag.DefaultIfEmpty()
                join e in _db.Employees.AsNoTracking() on (a == null ? null : a.EmployeeId) equals e.EmployeeId into eg
                from e in eg.DefaultIfEmpty()
                orderby c.SequenceOrder, c.ClassName, s.SectionName
                select new
                {
                    s.SectionId, s.ClassId, c.ClassName, s.SectionName, roster,
                    TeacherId = e == null ? null : e.EmployeeId,
                    TeacherName = e == null ? null : e.FirstName + " " + (e.MiddleName == null ? "" : e.MiddleName + " ") + e.LastName
                }).ToListAsync(ct);
            return rows.Select(r => new UnmarkedSectionRow(r.SectionId, r.ClassId, r.ClassName, r.SectionName, r.roster, r.TeacherId, r.TeacherName)).ToList();
        }

        public async Task<List<TeacherSectionRow>> GetMarkableSectionsAsync(string schoolId, long academicYearId, DateOnly today, string? employeeId, int activeStatusId, CancellationToken ct)
        {
            var rows = await (
                from s in _db.Sections.AsNoTracking()
                join c in _db.Classes.AsNoTracking() on s.ClassId equals c.ClassId
                where s.SchoolId == schoolId && c.SchoolId == schoolId && s.StatusId == activeStatusId && c.StatusId == activeStatusId
                      && (employeeId == null || _db.ClassTeacherAssignments.Any(a => a.SchoolId == schoolId && a.AcademicYearId == academicYearId && a.SectionId == s.SectionId
                                                                                  && a.EmployeeId == employeeId && a.StatusId == activeStatusId))
                orderby c.SequenceOrder, c.ClassName, s.SectionName
                select new
                {
                    s.SectionId, s.ClassId, c.ClassName, s.SectionName,
                    Roster = (from e in _db.StudentEnrollments
                              join st in _db.Students on e.StudentId equals st.StudentId
                              where e.SchoolId == schoolId && e.AcademicYearId == academicYearId && e.ClassSectionId == s.SectionId && e.StatusId == activeStatusId && st.StatusId == activeStatusId
                              select e.EnrollmentId).Count(),
                    Marked = _db.StudentAttendance.Count(x => x.SchoolId == schoolId && x.SectionId == s.SectionId && x.AttendanceDate == today)
                }).ToListAsync(ct);
            return rows.Select(r => new TeacherSectionRow(r.SectionId, r.ClassId, r.ClassName, r.SectionName, r.Roster, r.Marked)).ToList();
        }

        public async Task<(string? LoginId, string? Name)> GetUserAsync(string schoolId, long userId, CancellationToken ct)
        {
            var u = await _db.Users.AsNoTracking().Where(x => x.UserId == userId && x.SchoolId == schoolId).Select(x => new { x.OrgUserId, Name = x.DisplayName ?? x.OrgUserId }).FirstOrDefaultAsync(ct);
            return (u?.OrgUserId, u?.Name);
        }
    }
}
