using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class ClassTeacherRepository : IClassTeacherRepository
    {
        private readonly dbContext _db;
        public ClassTeacherRepository(dbContext db) => _db = db;

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);

        public async Task LockSectionYearAsync(string schoolId, long academicYearId, long sectionId, CancellationToken ct)
        {
            var key = $"class-teacher:{schoolId}:{academicYearId}:{sectionId}";
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == statusName && s.Stype == statusType).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public Task<YearRef?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.AcademicYearId == academicYearId)
                .Select(y => new YearRef(y.AcademicYearId, y.YearName)).FirstOrDefaultAsync(ct);

        public Task<YearRef?> GetCurrentYearAsync(string schoolId, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.IsCurrent)
                .Select(y => new YearRef(y.AcademicYearId, y.YearName)).FirstOrDefaultAsync(ct);

        public Task<SectionRef?> GetSectionAsync(string schoolId, long sectionId, CancellationToken ct) =>
            (from s in _db.Sections.AsNoTracking()
             join c in _db.Classes.AsNoTracking() on s.ClassId equals c.ClassId
             where s.SchoolId == schoolId && c.SchoolId == schoolId && s.SectionId == sectionId
             select new SectionRef(s.SectionId, s.ClassId, s.SectionName, c.ClassName, c.SequenceOrder, s.StatusId, c.StatusId)).FirstOrDefaultAsync(ct);

        private IQueryable<TbEmployee> EligibleEmployees(string schoolId, int activeStatusId, string teacherRoleCode) =>
            _db.Employees.AsNoTracking().Where(e => e.SchoolId == schoolId && e.StatusId == activeStatusId && e.EmployeeType == "Teaching"
                && !_db.LutEmploymentStatuses.Any(es => es.EmploymentStatusId == e.EmploymentStatusId && es.BlocksLogin)
                && _db.Users.Any(u => u.SchoolId == schoolId && u.OrgUserId == e.EmployeeId && u.StatusId == activeStatusId
                    && _db.UserRoles.Any(ur => ur.UserId == u.UserId
                        && _db.LutRoles.Any(r => r.RoleId == ur.RoleId && r.RoleCode == teacherRoleCode && r.StatusId == activeStatusId))));

        public async Task<List<EligibleTeacherRow>> GetEligibleTeachersAsync(string schoolId, int activeStatusId, string teacherRoleCode, CancellationToken ct)
        {
            var rows = await (
                from e in EligibleEmployees(schoolId, activeStatusId, teacherRoleCode)
                join d in _db.EmployeeDesignations.AsNoTracking() on e.DesignationId equals d.DesignationId
                orderby e.FirstName, e.LastName, e.EmployeeId
                select new { e.EmployeeId, e.FirstName, e.MiddleName, e.LastName, d.DesignationName }).ToListAsync(ct);
            return rows.Select(r => new EligibleTeacherRow(r.EmployeeId, r.FirstName, r.MiddleName, r.LastName, r.DesignationName)).ToList();
        }

        public Task<bool> IsEligibleTeacherAsync(string schoolId, string employeeId, int activeStatusId, string teacherRoleCode, CancellationToken ct) =>
            EligibleEmployees(schoolId, activeStatusId, teacherRoleCode).AnyAsync(e => e.EmployeeId == employeeId, ct);

        public Task<TbClassTeacherAssignment?> GetActiveForUpdateAsync(string schoolId, long academicYearId, long sectionId, int activeStatusId, CancellationToken ct) =>
            _db.ClassTeacherAssignments.Where(a => a.SchoolId == schoolId && a.AcademicYearId == academicYearId && a.SectionId == sectionId && a.StatusId == activeStatusId).FirstOrDefaultAsync(ct);

        public async Task AddAsync(TbClassTeacherAssignment assignment, CancellationToken ct) => await _db.ClassTeacherAssignments.AddAsync(assignment, ct);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

        private IQueryable<AssignmentRow> Rows(IQueryable<TbClassTeacherAssignment> source) =>
            from a in source
            join c in _db.Classes.AsNoTracking() on a.ClassId equals c.ClassId
            join s in _db.Sections.AsNoTracking() on a.SectionId equals s.SectionId
            join e in _db.Employees.AsNoTracking() on a.EmployeeId equals e.EmployeeId
            join st in _db.LutStatus.AsNoTracking() on a.StatusId equals st.Sid
            select new AssignmentRow(a, c.ClassName, s.SectionName, e.FirstName + " " + (e.MiddleName == null ? "" : e.MiddleName + " ") + e.LastName, st.Sname);

        public Task<AssignmentRow?> GetAssignmentRowAsync(string schoolId, long assignmentId, CancellationToken ct) =>
            Rows(_db.ClassTeacherAssignments.AsNoTracking().Where(a => a.SchoolId == schoolId && a.AssignmentId == assignmentId)).FirstOrDefaultAsync(ct);

        public Task<AssignmentRow?> GetActiveRowAsync(string schoolId, long academicYearId, long sectionId, int activeStatusId, CancellationToken ct) =>
            Rows(_db.ClassTeacherAssignments.AsNoTracking().Where(a => a.SchoolId == schoolId && a.AcademicYearId == academicYearId && a.SectionId == sectionId && a.StatusId == activeStatusId))
                .FirstOrDefaultAsync(ct);

        public async Task<List<SectionAssignmentRow>> GetSectionAssignmentsAsync(string schoolId, long academicYearId, int activeStatusId, CancellationToken ct)
        {
            // ONE query for the whole school: every active section of an active class, left-joined to its active class teacher for the year
            var rows = await (
                from s in _db.Sections.AsNoTracking()
                join c in _db.Classes.AsNoTracking() on s.ClassId equals c.ClassId
                where s.SchoolId == schoolId && c.SchoolId == schoolId && s.StatusId == activeStatusId && c.StatusId == activeStatusId
                join a in _db.ClassTeacherAssignments.AsNoTracking().Where(x => x.SchoolId == schoolId && x.AcademicYearId == academicYearId && x.StatusId == activeStatusId)
                    on s.SectionId equals a.SectionId into ag
                from a in ag.DefaultIfEmpty()
                join e in _db.Employees.AsNoTracking() on (a == null ? null : a.EmployeeId) equals e.EmployeeId into eg
                from e in eg.DefaultIfEmpty()
                orderby c.SequenceOrder, c.ClassName, s.SectionName
                select new
                {
                    s.SectionId, s.ClassId, s.SectionName, c.ClassName, c.SequenceOrder,
                    AssignmentId = a == null ? (long?)null : a.AssignmentId,
                    EmployeeId = e == null ? null : e.EmployeeId,
                    Name = e == null ? null : e.FirstName + " " + (e.MiddleName == null ? "" : e.MiddleName + " ") + e.LastName
                }).ToListAsync(ct);
            return rows.Select(r => new SectionAssignmentRow(r.SectionId, r.ClassId, r.SectionName, r.ClassName, r.SequenceOrder, r.AssignmentId, r.EmployeeId, r.Name)).ToList();
        }

        public async Task<List<AssignmentRow>> GetHistoryAsync(string schoolId, long academicYearId, long sectionId, CancellationToken ct)
        {
            var rows = await Rows(_db.ClassTeacherAssignments.AsNoTracking().Where(a => a.SchoolId == schoolId && a.AcademicYearId == academicYearId && a.SectionId == sectionId)).ToListAsync(ct);
            return rows.OrderByDescending(r => r.Assignment.AssignmentId).ToList();   // newest first
        }

        public Task<string?> GetUserLoginIdAsync(string schoolId, long userId, CancellationToken ct) =>
            _db.Users.AsNoTracking().Where(u => u.UserId == userId && u.SchoolId == schoolId).Select(u => u.OrgUserId).FirstOrDefaultAsync(ct);
    }
}
