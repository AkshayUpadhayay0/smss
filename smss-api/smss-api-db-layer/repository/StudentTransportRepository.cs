using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class StudentTransportRepository : IStudentTransportRepository
    {
        private readonly dbContext _db;
        public StudentTransportRepository(dbContext db) => _db = db;

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == statusName && s.Stype == statusType).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public async Task<TransportStudentRef?> GetStudentAsync(string schoolId, string studentId, CancellationToken ct)
        {
            var s = await _db.Students.AsNoTracking().Where(x => x.SchoolId == schoolId && x.StudentId == studentId)
                .Select(x => new { x.StudentId, x.FirstName, x.MiddleName, x.LastName }).FirstOrDefaultAsync(ct);
            return s == null ? null : new TransportStudentRef(s.StudentId, string.Join(' ', new[] { s.FirstName, s.MiddleName, s.LastName }.Where(p => !string.IsNullOrWhiteSpace(p))));
        }

        public async Task<string?> GetStudentIdForLoginAsync(string schoolId, long userId, CancellationToken ct)
        {
            var login = await _db.Users.AsNoTracking().Where(u => u.UserId == userId && u.SchoolId == schoolId && u.UserType == "Student").Select(u => u.OrgUserId).FirstOrDefaultAsync(ct);
            if (string.IsNullOrEmpty(login)) return null;
            return await _db.Students.AsNoTracking().Where(s => s.SchoolId == schoolId && s.StudentId == login).Select(s => s.StudentId).FirstOrDefaultAsync(ct);
        }

        public Task<TransportYearRef?> GetCurrentYearAsync(string schoolId, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.IsCurrent).OrderByDescending(y => y.StartDate)
                .Select(y => new TransportYearRef(y.AcademicYearId, y.YearName, y.IsCurrent)).FirstOrDefaultAsync(ct);

        public Task<TransportYearRef?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.AcademicYearId == academicYearId)
                .Select(y => new TransportYearRef(y.AcademicYearId, y.YearName, y.IsCurrent)).FirstOrDefaultAsync(ct);

        public Task<AreaRef?> GetAreaAsync(string schoolId, long areaId, CancellationToken ct) =>
            _db.TransportAreas.AsNoTracking().Where(a => a.SchoolId == schoolId && a.AreaId == areaId).Select(a => new AreaRef(a.AreaId, a.AreaName, a.StatusId)).FirstOrDefaultAsync(ct);

        public async Task<StudentAssignmentRow?> GetAssignmentAsync(string schoolId, string studentId, long academicYearId, CancellationToken ct)
        {
            var r = await (from t in _db.StudentTransports.AsNoTracking()
                           where t.SchoolId == schoolId && t.StudentId == studentId && t.AcademicYearId == academicYearId
                           join a in _db.TransportAreas.AsNoTracking() on new { t.SchoolId, t.AreaId } equals new { a.SchoolId, a.AreaId }
                           select new { t, a.AreaName, AreaStatusId = a.StatusId }).FirstOrDefaultAsync(ct);
            return r == null ? null : new StudentAssignmentRow(r.t, r.AreaName, r.AreaStatusId);
        }

        public Task<TbStudentTransport?> GetForUpdateAsync(string schoolId, string studentId, long academicYearId, CancellationToken ct) =>
            _db.StudentTransports.Where(t => t.SchoolId == schoolId && t.StudentId == studentId && t.AcademicYearId == academicYearId).FirstOrDefaultAsync(ct);

        public async Task AddAsync(TbStudentTransport row, CancellationToken ct) => await _db.StudentTransports.AddAsync(row, ct);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
    }
}
