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
        public Task<bool> IsYearClosedAsync(string schoolId, long academicYearId, CancellationToken ct) => _db.IsYearClosedAsync(schoolId, academicYearId, ct);

        public Task<bool> IsTeachingSectionSubjectAsync(string schoolId, string employeeId, long sectionId, long subjectId, long academicYearId, int activeStatusId, CancellationToken ct) =>
            _db.TimetableEntries.AsNoTracking().AnyAsync(e => e.SchoolId == schoolId && e.EmployeeId == employeeId && e.SectionId == sectionId && e.SubjectId == subjectId
                                                              && e.AcademicYearId == academicYearId && e.StatusId == activeStatusId && e.TtIsActive, ct);   // the ACTIVE timetable only

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);

        public async Task LockSlotAsync(string schoolId, long academicYearId, short dayOfWeek, long periodId, CancellationToken ct) => await LockSchoolAsync(schoolId, ct);

        public async Task LockSchoolAsync(string schoolId, CancellationToken ct)
        {
            var key = "acad-setup:" + schoolId;
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
            (await _db.Periods.AsNoTracking().Where(p => p.SchoolId == schoolId && p.StatusId == activeStatusId && p.SlotType == SlotTypes.Period).ToListAsync(ct)).OrderBy(p => p.PeriodNumber).ToList();

        public async Task<List<TbPeriod>> GetActiveSlotsAsync(string schoolId, int activeStatusId, CancellationToken ct) =>
            (await _db.Periods.AsNoTracking().Where(p => p.SchoolId == schoolId && p.StatusId == activeStatusId).ToListAsync(ct)).OrderBy(p => p.StartTime).ThenBy(p => p.SlotOrder).ToList();

        public async Task<long?> GetDefaultScheduleIdAsync(string schoolId, CancellationToken ct) =>
            await _db.PeriodSchedules.AsNoTracking().Where(d => d.SchoolId == schoolId && d.IsDefault).Select(d => (long?)d.ScheduleId).FirstOrDefaultAsync(ct);

        public Task<Dictionary<long, string>> GetScheduleNamesAsync(string schoolId, CancellationToken ct) =>
            _db.PeriodSchedules.AsNoTracking().Where(d => d.SchoolId == schoolId).ToDictionaryAsync(d => d.ScheduleId, d => d.ScheduleName, ct);

        public async Task<List<(long? ClassId, short? Day, long ScheduleId)>> GetYearScheduleRowsAsync(string schoolId, long academicYearId, CancellationToken ct) =>
            (await _db.AcademicYearSchedules.AsNoTracking().Where(a => a.SchoolId == schoolId && a.AcademicYearId == academicYearId).ToListAsync(ct))
                .Select(a => (a.ClassId, a.DayOfWeek, a.ScheduleId)).ToList();

        public Task<TtSubjectRow?> GetSubjectAsync(string schoolId, long subjectId, long academicYearId, long sectionId, int activeStatusId, CancellationToken ct) =>
            _db.Subjects.AsNoTracking().Where(s => s.SchoolId == schoolId && s.SubjectId == subjectId)
                .Select(s => new TtSubjectRow(s.SubjectId, s.SubjectName, s.SubjectCode, s.StatusId,
                    _db.SectionEffectiveSubjects.Any(v => v.SchoolId == schoolId && v.AcademicYearId == academicYearId && v.SectionId == sectionId && v.SubjectId == s.SubjectId))).FirstOrDefaultAsync(ct);

        public async Task<List<TtSubjectRow>> GetSubjectsOfSectionAsync(string schoolId, long academicYearId, long sectionId, int activeStatusId, CancellationToken ct)
        {
            var rows = await (from v in _db.SectionEffectiveSubjects.AsNoTracking()
                              join s in _db.Subjects.AsNoTracking() on v.SubjectId equals s.SubjectId
                              where v.SchoolId == schoolId && s.SchoolId == schoolId && v.AcademicYearId == academicYearId && v.SectionId == sectionId && s.StatusId == activeStatusId
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

        public Task<TbTimetableEntry?> GetActiveSlotForUpdateAsync(string schoolId, long timetableId, short dayOfWeek, long periodId, int activeStatusId, CancellationToken ct) =>
            _db.TimetableEntries.Where(e => e.SchoolId == schoolId && e.TimetableId == timetableId && e.DayOfWeek == dayOfWeek && e.PeriodId == periodId && e.StatusId == activeStatusId).FirstOrDefaultAsync(ct);

        public Task<TtConflictRow?> FindTeacherConflictAsync(string schoolId, long academicYearId, string employeeId, short dayOfWeek, long periodId, long exceptTimetableId, int activeStatusId, CancellationToken ct) =>
            (from e in _db.TimetableEntries.AsNoTracking()
             where e.SchoolId == schoolId && e.AcademicYearId == academicYearId && e.EmployeeId == employeeId && e.DayOfWeek == dayOfWeek && e.PeriodId == periodId
                   && e.StatusId == activeStatusId && e.TtIsActive && e.TimetableId != exceptTimetableId
             join c in _db.Classes.AsNoTracking() on e.ClassId equals c.ClassId
             join s in _db.Sections.AsNoTracking() on e.SectionId equals s.SectionId
             join sub in _db.Subjects.AsNoTracking() on e.SubjectId equals sub.SubjectId
             join p in _db.Periods.AsNoTracking() on e.PeriodId equals p.PeriodId
             join t in _db.Timetables.AsNoTracking() on e.TimetableId equals t.TimetableId
             select new TtConflictRow(c.ClassName, s.SectionName, sub.SubjectName, p.PeriodNumber ?? 0, t.TimetableName)).FirstOrDefaultAsync(ct);

        public async Task AddAsync(TbTimetableEntry entry, CancellationToken ct) => await _db.TimetableEntries.AddAsync(entry, ct);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

        private IQueryable<TtEntryRow> Rows(IQueryable<TbTimetableEntry> source) =>
            from e in source
            join c in _db.Classes.AsNoTracking() on e.ClassId equals c.ClassId
            join s in _db.Sections.AsNoTracking() on e.SectionId equals s.SectionId
            join sub in _db.Subjects.AsNoTracking() on e.SubjectId equals sub.SubjectId
            join emp0 in _db.Employees.AsNoTracking() on e.EmployeeId equals emp0.EmployeeId into empGroup
            from emp in empGroup.DefaultIfEmpty()   // a slot copied without a teacher has none
            join p in _db.Periods.AsNoTracking() on e.PeriodId equals p.PeriodId
            select new TtEntryRow(e, c.ClassName, s.SectionName, sub.SubjectName, sub.SubjectCode,
                emp == null ? "" : emp.FirstName + " " + (emp.MiddleName == null ? "" : emp.MiddleName + " ") + emp.LastName, p.PeriodNumber ?? 0);

        public Task<TtEntryRow?> GetEntryRowAsync(string schoolId, long entryId, CancellationToken ct) =>
            Rows(_db.TimetableEntries.AsNoTracking().Where(e => e.SchoolId == schoolId && e.TimetableEntryId == entryId)).FirstOrDefaultAsync(ct);

        public Task<List<TtEntryRow>> GetSectionEntriesAsync(string schoolId, long academicYearId, long sectionId, int activeStatusId, CancellationToken ct) =>
            Rows(_db.TimetableEntries.AsNoTracking().Where(e => e.SchoolId == schoolId && e.AcademicYearId == academicYearId && e.SectionId == sectionId && e.StatusId == activeStatusId && e.TtIsActive)).ToListAsync(ct);   // the ACTIVE timetable only

        public Task<List<TtEntryRow>> GetTeacherEntriesAsync(string schoolId, long academicYearId, string employeeId, int activeStatusId, CancellationToken ct) =>
            Rows(_db.TimetableEntries.AsNoTracking().Where(e => e.SchoolId == schoolId && e.AcademicYearId == academicYearId && e.EmployeeId == employeeId && e.StatusId == activeStatusId && e.TtIsActive)).ToListAsync(ct);   // the ACTIVE timetable only

        public Task<long?> GetStudentSectionAsync(string schoolId, string studentId, long academicYearId, int activeStatusId, CancellationToken ct) =>
            (from e in _db.StudentEnrollments.AsNoTracking()
             join s in _db.Students.AsNoTracking() on e.StudentId equals s.StudentId
             where e.SchoolId == schoolId && s.SchoolId == schoolId && e.StudentId == studentId && e.AcademicYearId == academicYearId && e.StatusId == activeStatusId && s.StatusId == activeStatusId
             select (long?)e.ClassSectionId).FirstOrDefaultAsync(ct);

        // ================================================================== S6: timetable versions
        public Task<TbTimetable?> GetTimetableAsync(string schoolId, long timetableId, CancellationToken ct) =>
            _db.Timetables.AsNoTracking().Where(t => t.SchoolId == schoolId && t.TimetableId == timetableId).FirstOrDefaultAsync(ct);

        public Task<TbTimetable?> GetTimetableForUpdateAsync(string schoolId, long timetableId, CancellationToken ct) =>
            _db.Timetables.Where(t => t.SchoolId == schoolId && t.TimetableId == timetableId).FirstOrDefaultAsync(ct);

        public Task<TbTimetable?> GetActiveTimetableAsync(string schoolId, long academicYearId, long sectionId, CancellationToken ct) =>
            _db.Timetables.AsNoTracking().Where(t => t.SchoolId == schoolId && t.AcademicYearId == academicYearId && t.SectionId == sectionId && t.IsActive).FirstOrDefaultAsync(ct);

        public Task<TbTimetable?> GetActiveTimetableForUpdateAsync(string schoolId, long academicYearId, long sectionId, CancellationToken ct) =>
            _db.Timetables.Where(t => t.SchoolId == schoolId && t.AcademicYearId == academicYearId && t.SectionId == sectionId && t.IsActive).FirstOrDefaultAsync(ct);

        public async Task<List<TimetableListRow>> ListTimetablesAsync(string schoolId, long academicYearId, long sectionId, int activeStatusId, CancellationToken ct)
        {
            var rows = await (from t in _db.Timetables.AsNoTracking()
                              join d in _db.PeriodSchedules.AsNoTracking() on t.ScheduleId equals d.ScheduleId
                              where t.SchoolId == schoolId && t.AcademicYearId == academicYearId && t.SectionId == sectionId
                              select new
                              {
                                  t, ScheduleName = d.ScheduleName,
                                  Slots = _db.TimetableEntries.Count(e => e.TimetableId == t.TimetableId && e.StatusId == activeStatusId),
                                  Needs = _db.TimetableEntries.Count(e => e.TimetableId == t.TimetableId && e.StatusId == activeStatusId && e.NeedsTeacher),
                                  ActivatedBy = _db.Users.Where(u => u.UserId == t.ActivatedBy).Select(u => u.DisplayName).FirstOrDefault(),
                                  CreatedBy = _db.Users.Where(u => u.UserId == t.CreatedBy).Select(u => u.DisplayName).FirstOrDefault()
                              }).ToListAsync(ct);
            return rows.OrderByDescending(r => r.t.IsActive).ThenByDescending(r => r.t.UpdatedAt).ThenByDescending(r => r.t.TimetableId)
                .Select(r => new TimetableListRow(r.t, r.ScheduleName, r.Slots, r.Needs, r.ActivatedBy, r.CreatedBy)).ToList();
        }

        public Task<bool> TimetableNameTakenAsync(string schoolId, long academicYearId, long sectionId, string name, long? exceptTimetableId, CancellationToken ct) =>
            _db.Timetables.AnyAsync(t => t.SchoolId == schoolId && t.AcademicYearId == academicYearId && t.SectionId == sectionId && t.TimetableName.ToLower() == name.ToLower() && t.TimetableId != exceptTimetableId, ct);

        public async Task AddTimetableAsync(TbTimetable timetable, CancellationToken ct) => await _db.Timetables.AddAsync(timetable, ct);
        public void RemoveTimetable(TbTimetable timetable) => _db.Timetables.Remove(timetable);

        public Task<List<TbTimetableEntry>> GetTimetableEntriesForUpdateAsync(string schoolId, long timetableId, CancellationToken ct) =>
            _db.TimetableEntries.Where(e => e.SchoolId == schoolId && e.TimetableId == timetableId).ToListAsync(ct);

        public void RemoveEntries(IEnumerable<TbTimetableEntry> entries) => _db.TimetableEntries.RemoveRange(entries);

        public Task<List<TtEntryRow>> GetTimetableRowsAsync(string schoolId, long timetableId, int activeStatusId, CancellationToken ct) =>
            Rows(_db.TimetableEntries.AsNoTracking().Where(e => e.SchoolId == schoolId && e.TimetableId == timetableId && e.StatusId == activeStatusId)).ToListAsync(ct);

        public Task<List<TeacherBookingRow>> GetActiveBookingsAsync(string schoolId, long academicYearId, IReadOnlyCollection<string> employeeIds, IReadOnlyCollection<long> exceptTimetableIds, int activeStatusId, CancellationToken ct)
        {
            var ids = employeeIds.ToArray();
            var except = exceptTimetableIds.ToArray();
            return (from e in _db.TimetableEntries.AsNoTracking()
                    where e.SchoolId == schoolId && e.AcademicYearId == academicYearId && e.StatusId == activeStatusId && e.TtIsActive && !except.Contains(e.TimetableId)
                          && e.EmployeeId != null && ids.Contains(e.EmployeeId)
                    join c in _db.Classes.AsNoTracking() on e.ClassId equals c.ClassId
                    join s in _db.Sections.AsNoTracking() on e.SectionId equals s.SectionId
                    join sub in _db.Subjects.AsNoTracking() on e.SubjectId equals sub.SubjectId
                    join p in _db.Periods.AsNoTracking() on e.PeriodId equals p.PeriodId
                    join t in _db.Timetables.AsNoTracking() on e.TimetableId equals t.TimetableId
                    select new TeacherBookingRow(e.EmployeeId!, e.DayOfWeek, e.PeriodId, p.PeriodNumber ?? 0, c.ClassName, s.SectionName, sub.SubjectName, t.TimetableName, t.TimetableId)).ToListAsync(ct);
        }

        public async Task<List<OfferedSectionRow>> GetOfferedSectionsOfClassAsync(string schoolId, long academicYearId, long classId, CancellationToken ct) =>
            await (from o in _db.AcademicYearSections.AsNoTracking()
                   join s in _db.Sections.AsNoTracking() on o.SectionId equals s.SectionId
                   join st in _db.LutStatus.AsNoTracking() on o.StatusId equals st.Sid
                   where o.SchoolId == schoolId && o.AcademicYearId == academicYearId && o.ClassId == classId && st.Sname == "Active" && st.Stype == "general status"
                   orderby s.SectionName
                   select new OfferedSectionRow(s.SectionId, s.SectionName, s.StatusId)).ToListAsync(ct);

        public async Task<HashSet<long>> GetEffectiveSubjectIdsAsync(string schoolId, long academicYearId, long sectionId, CancellationToken ct) =>
            (await _db.EffectiveSubjects(schoolId, academicYearId).Where(v => v.SectionId == sectionId).Select(v => v.SubjectId).ToListAsync(ct)).ToHashSet();

        public Task<Dictionary<long, TbPeriod>> GetSlotsByIdsAsync(string schoolId, IReadOnlyCollection<long> periodIds, CancellationToken ct)
        {
            var ids = periodIds.ToArray();
            return _db.Periods.AsNoTracking().Where(p => p.SchoolId == schoolId && ids.Contains(p.PeriodId)).ToDictionaryAsync(p => p.PeriodId, ct);
        }

        public async Task<Dictionary<string, TtTeacherRow>> GetTeachersByIdsAsync(string schoolId, IReadOnlyCollection<string> employeeIds, CancellationToken ct)
        {
            var ids = employeeIds.ToArray();
            var rows = await TeacherRows(_db.Employees.AsNoTracking().Where(e => e.SchoolId == schoolId && ids.Contains(e.EmployeeId))).ToListAsync(ct);
            return rows.ToDictionary(r => r.EmployeeId, r => r);
        }

        public Task<Dictionary<long, string>> GetSubjectNamesAsync(string schoolId, CancellationToken ct) =>
            _db.Subjects.AsNoTracking().Where(s => s.SchoolId == schoolId).ToDictionaryAsync(s => s.SubjectId, s => s.SubjectName, ct);

        public Task<string?> GetUserDisplayNameAsync(string schoolId, long userId, CancellationToken ct) =>
            _db.Users.AsNoTracking().Where(u => u.UserId == userId && u.SchoolId == schoolId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);

        public Task<string?> GetUserLoginIdAsync(string schoolId, long userId, CancellationToken ct) =>
            _db.Users.AsNoTracking().Where(u => u.UserId == userId && u.SchoolId == schoolId).Select(u => u.OrgUserId).FirstOrDefaultAsync(ct);
    }
}
