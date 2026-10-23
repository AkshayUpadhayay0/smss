using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class LeaveRepository : ILeaveRepository
    {
        private readonly dbContext _db;
        public LeaveRepository(dbContext db) => _db = db;

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);

        public async Task LockPersonAsync(string kind, string schoolId, string personId, CancellationToken ct)
        {
            var key = $"leave:{kind}:{schoolId}:{personId}";
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }

        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

        // ---------------------------------------------------------------- shared lookups
        public Task<List<LeaveTypeRow>> GetLeaveTypesAsync(CancellationToken ct) =>
            _db.LeaveTypes.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.LeaveTypeId)
                .Select(t => new LeaveTypeRow(t.LeaveTypeId, t.TypeName, t.TypeCode, t.AppliesToEmployee, t.AppliesToStudent, t.IsActive)).ToListAsync(ct);

        public Task<LeaveTypeRow?> GetLeaveTypeAsync(long leaveTypeId, CancellationToken ct) =>
            _db.LeaveTypes.AsNoTracking().Where(t => t.LeaveTypeId == leaveTypeId)
                .Select(t => new LeaveTypeRow(t.LeaveTypeId, t.TypeName, t.TypeCode, t.AppliesToEmployee, t.AppliesToStudent, t.IsActive)).FirstOrDefaultAsync(ct);

        public async Task<(string? LoginId, string? Name)> GetUserAsync(string schoolId, long userId, CancellationToken ct)
        {
            var u = await _db.Users.AsNoTracking().Where(x => x.UserId == userId && x.SchoolId == schoolId).Select(x => new { x.OrgUserId, Name = x.DisplayName ?? x.OrgUserId }).FirstOrDefaultAsync(ct);
            return (u?.OrgUserId, u?.Name);
        }

        public Task<long?> GetYearIdContainingAsync(string schoolId, DateOnly date, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.StartDate <= date && y.EndDate >= date)
                .OrderByDescending(y => y.IsCurrent).ThenByDescending(y => y.StartDate).Select(y => (long?)y.AcademicYearId).FirstOrDefaultAsync(ct);

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == statusName && s.Stype == statusType).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public Task<LeaveEmployeeRow?> GetEmployeeAsync(string schoolId, string employeeId, CancellationToken ct) =>
            _db.Employees.AsNoTracking().Where(e => e.SchoolId == schoolId && e.EmployeeId == employeeId)
                .Select(e => new LeaveEmployeeRow(e.EmployeeId, e.FirstName, e.MiddleName, e.LastName, e.StatusId,
                    _db.LutEmploymentStatuses.Any(es => es.EmploymentStatusId == e.EmploymentStatusId && es.BlocksLogin))).FirstOrDefaultAsync(ct);

        public Task<LeaveStudentRow?> GetStudentAsync(string schoolId, string studentId, CancellationToken ct) =>
            _db.Students.AsNoTracking().Where(s => s.SchoolId == schoolId && s.StudentId == studentId)
                .Select(s => new LeaveStudentRow(s.StudentId, s.FirstName, s.MiddleName, s.LastName, s.StatusId)).FirstOrDefaultAsync(ct);

        public Task<long?> GetStudentSectionAsync(string schoolId, string studentId, long academicYearId, CancellationToken ct) =>
            _db.StudentEnrollments.AsNoTracking().Where(e => e.SchoolId == schoolId && e.StudentId == studentId && e.AcademicYearId == academicYearId)
                .Select(e => (long?)e.ClassSectionId).FirstOrDefaultAsync(ct);

        public Task<List<long>> GetClassTeacherSectionIdsAsync(string schoolId, string employeeId, long academicYearId, int activeStatusId, CancellationToken ct) =>
            _db.ClassTeacherAssignments.AsNoTracking()
                .Where(a => a.SchoolId == schoolId && a.AcademicYearId == academicYearId && a.EmployeeId == employeeId && a.StatusId == activeStatusId)
                .Select(a => a.SectionId).ToListAsync(ct);

        // ---------------------------------------------------------------- employee requests
        public async Task AddEmployeeRequestAsync(TbEmployeeLeaveRequest request, CancellationToken ct) => await _db.EmployeeLeaveRequests.AddAsync(request, ct);

        public Task<TbEmployeeLeaveRequest?> GetEmployeeRequestAsync(string schoolId, long id, bool tracked, CancellationToken ct)
        {
            var q = tracked ? _db.EmployeeLeaveRequests.AsTracking() : _db.EmployeeLeaveRequests.AsNoTracking();
            return q.Where(r => r.SchoolId == schoolId && r.LeaveRequestId == id).FirstOrDefaultAsync(ct);
        }

        public Task<LeaveApprovedRange?> FindApprovedEmployeeOverlapAsync(string schoolId, string employeeId, DateOnly fromDate, DateOnly toDate, long excludeId, CancellationToken ct) =>
            (from r in _db.EmployeeLeaveRequests.AsNoTracking()
             join t in _db.LeaveTypes on r.LeaveTypeId equals t.LeaveTypeId
             where r.SchoolId == schoolId && r.EmployeeId == employeeId && r.Status == "Approved" && r.LeaveRequestId != excludeId
                   && r.FromDate <= toDate && r.ToDate >= fromDate
             orderby r.FromDate
             select new LeaveApprovedRange(r.FromDate, r.ToDate, t.TypeName)).FirstOrDefaultAsync(ct);

        // The filter is applied to the entity BEFORE the projection into LeaveRow: EF can't translate a Where over the projected record.
        private IQueryable<LeaveRow> EmployeeRows(string schoolId, Expression<Func<TbEmployeeLeaveRequest, bool>> filter) =>
            from r in _db.EmployeeLeaveRequests.AsNoTracking().Where(x => x.SchoolId == schoolId).Where(filter)
            join e in _db.Employees on r.EmployeeId equals e.EmployeeId
            join t in _db.LeaveTypes on r.LeaveTypeId equals t.LeaveTypeId
            join ua in _db.Users on r.AppliedBy equals ua.UserId
            join ud in _db.Users on r.ApprovedBy equals ud.UserId into decided
            from d in decided.DefaultIfEmpty()
            select new LeaveRow(r.LeaveRequestId, r.EmployeeId, e.FirstName, e.MiddleName, e.LastName, r.LeaveTypeId, t.TypeName, r.FromDate, r.ToDate, r.Reason, r.Status,
                r.AppliedBy, ua.DisplayName ?? ua.OrgUserId, r.AppliedAt, r.ApprovedBy, d == null ? null : (d.DisplayName ?? d.OrgUserId), r.ApprovedAt, r.DecisionReason, null, null, null);

        public Task<LeaveRow?> GetEmployeeRowAsync(string schoolId, long id, CancellationToken ct) =>
            EmployeeRows(schoolId, r => r.LeaveRequestId == id).FirstOrDefaultAsync(ct);

        public async Task<List<LeaveRow>> ListEmployeeRowsAsync(string schoolId, string? employeeId, string? status, CancellationToken ct)
        {
            var rows = await EmployeeRows(schoolId, r => (employeeId == null || r.EmployeeId == employeeId) && (status == null || r.Status == status)).ToListAsync(ct);
            return rows.OrderByDescending(r => r.AppliedAt).ThenByDescending(r => r.LeaveRequestId).ToList();
        }

        // ---------------------------------------------------------------- student requests
        public async Task AddStudentRequestAsync(TbStudentLeaveRequest request, CancellationToken ct) => await _db.StudentLeaveRequests.AddAsync(request, ct);

        public Task<TbStudentLeaveRequest?> GetStudentRequestAsync(string schoolId, long id, bool tracked, CancellationToken ct)
        {
            var q = tracked ? _db.StudentLeaveRequests.AsTracking() : _db.StudentLeaveRequests.AsNoTracking();
            return q.Where(r => r.SchoolId == schoolId && r.LeaveRequestId == id).FirstOrDefaultAsync(ct);
        }

        public Task<LeaveApprovedRange?> FindApprovedStudentOverlapAsync(string schoolId, string studentId, DateOnly fromDate, DateOnly toDate, long excludeId, CancellationToken ct) =>
            (from r in _db.StudentLeaveRequests.AsNoTracking()
             join t in _db.LeaveTypes on r.LeaveTypeId equals t.LeaveTypeId
             where r.SchoolId == schoolId && r.StudentId == studentId && r.Status == "Approved" && r.LeaveRequestId != excludeId
                   && r.FromDate <= toDate && r.ToDate >= fromDate
             orderby r.FromDate
             select new LeaveApprovedRange(r.FromDate, r.ToDate, t.TypeName)).FirstOrDefaultAsync(ct);

        private IQueryable<LeaveRow> StudentRows(string schoolId, Expression<Func<TbStudentLeaveRequest, bool>> filter) =>
            from r in _db.StudentLeaveRequests.AsNoTracking().Where(x => x.SchoolId == schoolId).Where(filter)
            join s in _db.Students on r.StudentId equals s.StudentId
            join t in _db.LeaveTypes on r.LeaveTypeId equals t.LeaveTypeId
            join ua in _db.Users on r.AppliedBy equals ua.UserId
            join ud in _db.Users on r.ApprovedBy equals ud.UserId into decided
            from d in decided.DefaultIfEmpty()
            select new LeaveRow(r.LeaveRequestId, r.StudentId, s.FirstName, s.MiddleName, s.LastName, r.LeaveTypeId, t.TypeName, r.FromDate, r.ToDate, r.Reason, r.Status,
                r.AppliedBy, ua.DisplayName ?? ua.OrgUserId, r.AppliedAt, r.ApprovedBy, d == null ? null : (d.DisplayName ?? d.OrgUserId), r.ApprovedAt, r.DecisionReason, null, null, null);

        /// <summary>Adds each student's section / class of the given year to the rows (a student without an enrollment that year keeps nulls).</summary>
        private async Task<List<LeaveRow>> WithSectionsAsync(string schoolId, List<LeaveRow> rows, long? academicYearId, CancellationToken ct)
        {
            if (academicYearId == null || rows.Count == 0) return rows;
            var ids = rows.Select(r => r.PersonId).Distinct().ToList();
            var sectionByStudent = await _db.StudentEnrollments.AsNoTracking()
                .Where(e => e.SchoolId == schoolId && e.AcademicYearId == academicYearId && ids.Contains(e.StudentId))
                .Select(e => new { e.StudentId, e.ClassSectionId }).ToDictionaryAsync(e => e.StudentId, e => e.ClassSectionId, ct);
            var sectionIds = sectionByStudent.Values.Distinct().ToList();
            var names = await (from s in _db.Sections.AsNoTracking()
                               join c in _db.Classes on s.ClassId equals c.ClassId
                               where s.SchoolId == schoolId && sectionIds.Contains(s.SectionId)
                               select new { s.SectionId, c.ClassName, s.SectionName }).ToDictionaryAsync(x => x.SectionId, ct);
            return rows.Select(r =>
            {
                if (!sectionByStudent.TryGetValue(r.PersonId, out var sid) || !names.TryGetValue(sid, out var n)) return r;
                return r with { SectionId = sid, ClassName = n.ClassName, SectionName = n.SectionName };
            }).ToList();
        }

        public async Task<LeaveRow?> GetStudentRowAsync(string schoolId, long id, long? academicYearId, CancellationToken ct)
        {
            var row = await StudentRows(schoolId, r => r.LeaveRequestId == id).FirstOrDefaultAsync(ct);
            return row == null ? null : (await WithSectionsAsync(schoolId, new List<LeaveRow> { row }, academicYearId, ct))[0];
        }

        public async Task<List<LeaveRow>> ListStudentRowsAsync(string schoolId, long? academicYearId, IReadOnlyCollection<long>? sectionIds, string? status, CancellationToken ct)
        {
            Expression<Func<TbStudentLeaveRequest, bool>> filter = r => status == null || r.Status == status;
            if (sectionIds != null)
            {
                if (academicYearId == null || sectionIds.Count == 0) return new List<LeaveRow>();
                var sections = sectionIds.ToList();
                var students = _db.StudentEnrollments.AsNoTracking()
                    .Where(e => e.SchoolId == schoolId && e.AcademicYearId == academicYearId && sections.Contains(e.ClassSectionId)).Select(e => e.StudentId);
                filter = r => (status == null || r.Status == status) && students.Contains(r.StudentId);
            }
            var rows = (await StudentRows(schoolId, filter).ToListAsync(ct)).OrderByDescending(r => r.AppliedAt).ThenByDescending(r => r.LeaveRequestId).ToList();
            return await WithSectionsAsync(schoolId, rows, academicYearId, ct);
        }
    }
}
