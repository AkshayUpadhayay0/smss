using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class TeacherDashboardRepository : ITeacherDashboardRepository
    {
        private readonly dbContext _db;
        public TeacherDashboardRepository(dbContext db) => _db = db;

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == statusName && s.Stype == statusType).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public async Task<int> CountStudentsWithUnpaidDuesAsync(string schoolId, long academicYearId, IReadOnlyCollection<long> sectionIds, int activeStatusId, DateOnly fromDate, DateOnly toExclusive, CancellationToken ct)
        {
            if (sectionIds.Count == 0) return 0;
            var sections = sectionIds.ToArray();
            // the roster is the same one Attendance / Assessments use; school id on every table so another school's rows can never be counted
            return await (
                from e in _db.StudentEnrollments.AsNoTracking()
                join st in _db.Students.AsNoTracking() on e.StudentId equals st.StudentId
                where e.SchoolId == schoolId && e.AcademicYearId == academicYearId && sections.Contains(e.ClassSectionId)
                      && e.StatusId == activeStatusId && st.StatusId == activeStatusId && st.SchoolId == schoolId
                      && _db.StudentFeeDues.Any(d => d.SchoolId == schoolId && d.StudentId == e.StudentId
                                                     && d.DueDate >= fromDate &&d.DueDate < toExclusive && d.AmountPaid < d.AmountDue)
                select e.StudentId).Distinct().CountAsync(ct);
        }
    }
}
