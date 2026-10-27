using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class TimetableService : ITimetableService
    {
        private const string SlotIndex = "uq_tb_tte_one_active_per_slot";
        private const string TeacherIndex = "uq_tb_tte_teacher_one_active_per_period";
        private static readonly string[] DayNames = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

        private readonly ITimetableRepository _repo;
        private readonly ILogger<TimetableService> _logger;

        public TimetableService(ITimetableRepository repo, ILogger<TimetableService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        // ---------------------------------------------------------------- reuse: is this teacher teaching this subject to this section?
        public async Task<bool> IsTeachingSectionSubjectAsync(string? schoolId, string employeeId, long sectionId, long subjectId, long academicYearId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId) || string.IsNullOrWhiteSpace(employeeId)) return false;
            var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
            if (activeId == null) return false;
            return await _repo.IsTeachingSectionSubjectAsync(schoolId, employeeId.Trim(), sectionId, subjectId, academicYearId, activeId.Value, ct);
        }

        // ---------------------------------------------------------------- assign / reassign / unassign
        public async Task<ApiResponse<TimetableEntryDto>> AssignAsync(string? schoolId, long assignedByUserId, AssignTimetableRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TimetableEntryDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var ctx = await ResolveAsync(schoolId, req.AcademicYearId!.Value, req.SectionId!.Value, req.ClassId, req.DayOfWeek!.Value, req.PeriodId!.Value, ct, requireActiveSection: true);
                if (ctx.Error != null) return Result<TimetableEntryDto>(ctx.Error.Value.Code, ctx.Error.Value.Message, null);
                var (activeId, inactiveId, section, day, period) = (ctx.ActiveId, ctx.InactiveId, ctx.Section!, ctx.Day, ctx.Period!);

                var subject = await _repo.GetSubjectAsync(schoolId, req.SubjectId!.Value, section.ClassId, activeId, ct);
                if (subject == null || subject.StatusId != activeId) return Result<TimetableEntryDto>(400, "The selected subject was not found for your school.", null);
                if (!subject.MappedToClass)
                    return Result<TimetableEntryDto>(400, $"{subject.Name} is not taught in {section.ClassName}. Add it to the class under Class-Subject Mapping first.", null);

                var employeeId = req.EmployeeId!.Trim();
                var teacher = await _repo.GetTeacherAsync(schoolId, employeeId, ct);
                if (teacher == null) return Result<TimetableEntryDto>(400, "The selected teacher was not found for your school.", null);
                if (teacher.StatusId != activeId || teacher.EmployeeType != EmployeeTypes.Teaching || teacher.EmploymentBlocksLogin)
                    return Result<TimetableEntryDto>(400, "This employee can't be given a timetable slot. They need an active record, employee type Teaching, and an employment status that still works at the school.", null);

                var room = string.IsNullOrWhiteSpace(req.RoomNo) ? null : req.RoomNo.Trim();
                long entryId;
                await using (var tx = await _repo.BeginTransactionAsync(ct))
                {
                    await _repo.LockSlotAsync(schoolId, req.AcademicYearId.Value, day, period.PeriodId, ct);   // everything that could clash on this day + period queues up

                    // a teacher can't be in two places at once: another section at the same day + period in the same year
                    var clash = await _repo.FindTeacherConflictAsync(schoolId, req.AcademicYearId.Value, employeeId, day, period.PeriodId, section.SectionId, activeId, ct);
                    if (clash != null)
                        return Result<TimetableEntryDto>(409, $"{TeacherName(teacher)} is already teaching {clash.ClassName} - {clash.SectionName} ({clash.SubjectName}) on {DayNames[day - 1]}, period {clash.PeriodNumber}.", null);

                    var current = await _repo.GetActiveSlotForUpdateAsync(schoolId, req.AcademicYearId.Value, section.SectionId, day, period.PeriodId, activeId, ct);
                    if (current != null && current.SubjectId == subject.SubjectId && current.EmployeeId == employeeId && string.Equals(current.RoomNo ?? "", room ?? "", StringComparison.Ordinal))
                        return await RowAsync(schoolId, current.TimetableEntryId, 200, "This slot already has exactly these details.", ct);

                    var now = DateTime.UtcNow;
                    if (current != null)
                    {
                        // history, not deletion: the old entry stays, marked Inactive. Saved first, so the one-active-slot / one-booking-per-teacher indexes are never violated.
                        current.StatusId = inactiveId;
                        current.UpdatedAt = now;
                        await _repo.SaveChangesAsync(ct);
                    }
                    var next = new TbTimetableEntry
                    {
                        SchoolId = schoolId, AcademicYearId = req.AcademicYearId.Value, ClassId = section.ClassId, SectionId = section.SectionId, DayOfWeek = day, PeriodId = period.PeriodId,
                        SubjectId = subject.SubjectId, EmployeeId = employeeId, RoomNo = room, StatusId = activeId, AssignedBy = assignedByUserId, CreatedAt = now, UpdatedAt = now
                    };
                    await _repo.AddAsync(next, ct);
                    await _repo.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    entryId = next.TimetableEntryId;
                }
                return await RowAsync(schoolId, entryId, 200, "Timetable slot saved.", ct);
            }
            catch (DbUpdateException ex) when ((ex.InnerException as PostgresException)?.ConstraintName is SlotIndex or TeacherIndex)
            {
                // the database's one-active-slot / one-booking-per-teacher rule caught a race the lock did not (should not happen)
                return Result<TimetableEntryDto>(409, "This slot or this teacher's period was just changed by someone else. Please refresh and try again.", null);
            }
            catch (Exception ex) { return Error<TimetableEntryDto>(ex, "assigning a timetable slot"); }
        }

        public async Task<ApiResponse<TimetableEntryDto>> UnassignAsync(string? schoolId, UnassignTimetableRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TimetableEntryDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var ctx = await ResolveAsync(schoolId, req.AcademicYearId!.Value, req.SectionId!.Value, null, req.DayOfWeek!.Value, req.PeriodId!.Value, ct, requireActiveSection: false);
                if (ctx.Error != null) return Result<TimetableEntryDto>(ctx.Error.Value.Code, ctx.Error.Value.Message, null);

                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockSlotAsync(schoolId, req.AcademicYearId.Value, ctx.Day, ctx.Period!.PeriodId, ct);
                var current = await _repo.GetActiveSlotForUpdateAsync(schoolId, req.AcademicYearId.Value, req.SectionId.Value, ctx.Day, ctx.Period.PeriodId, ctx.ActiveId, ct);
                if (current == null) return Result<TimetableEntryDto>(404, "There is nothing scheduled in this slot.", null);
                current.StatusId = ctx.InactiveId;
                current.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return await RowAsync(schoolId, current.TimetableEntryId, 200, "Timetable slot cleared.", ct);
            }
            catch (Exception ex) { return Error<TimetableEntryDto>(ex, "clearing a timetable slot"); }
        }

        // ---------------------------------------------------------------- reads
        public async Task<ApiResponse<TimetableLookupsDto>> GetLookupsAsync(string? schoolId, long sectionId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TimetableLookupsDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<TimetableLookupsDto>();
                var section = await _repo.GetSectionAsync(schoolId, sectionId, ct);
                if (section == null) return Result<TimetableLookupsDto>(404, "Section not found.", null);

                var subjects = await _repo.GetSubjectsOfClassAsync(schoolId, section.ClassId, activeId.Value, ct);
                var teachers = await _repo.GetTeachersAsync(schoolId, activeId.Value, ct);
                return Result(200, "Successfully fetched", new TimetableLookupsDto
                {
                    Subjects = subjects.Select(s => new TimetableSubjectOptionDto { SubjectId = s.SubjectId, SubjectName = s.Name, SubjectCode = s.Code }).ToList(),
                    Teachers = teachers.Select(t => new TimetableTeacherOptionDto { EmployeeId = t.EmployeeId, FullName = TeacherName(t), DesignationName = t.DesignationName }).ToList()
                });
            }
            catch (Exception ex) { return Error<TimetableLookupsDto>(ex, "fetching timetable options"); }
        }

        public async Task<ApiResponse<TimetableGridDto>> GetClassGridAsync(string? schoolId, long sectionId, long? academicYearId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TimetableGridDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<TimetableGridDto>();
                var section = await _repo.GetSectionAsync(schoolId, sectionId, ct);
                if (section == null) return Result<TimetableGridDto>(404, "Section not found.", null);
                var (year, error) = await YearAsync(schoolId, academicYearId, ct);
                if (year == null) return Result<TimetableGridDto>(error!.Value.Code, error.Value.Message, null);

                var periods = await _repo.GetActivePeriodsAsync(schoolId, activeId.Value, ct);
                var entries = await _repo.GetSectionEntriesAsync(schoolId, year.AcademicYearId, sectionId, activeId.Value, ct);
                var grid = BuildGrid(year, "section", periods, entries);
                grid.SectionId = section.SectionId; grid.ClassId = section.ClassId; grid.ClassName = section.ClassName; grid.SectionName = section.SectionName;
                return Result(200, "Successfully fetched", grid);
            }
            catch (Exception ex) { return Error<TimetableGridDto>(ex, "fetching a section timetable"); }
        }

        public async Task<ApiResponse<TimetableGridDto>> GetTeacherGridAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, string employeeId, long? academicYearId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TimetableGridDto>(400, TenantMessages.NoSchool, null);
            try
            {
                employeeId = (employeeId ?? string.Empty).Trim();
                if (!callerIsSchoolAdmin)
                {
                    // a teacher may ask about themselves only: the employee id is their own login id
                    var own = await _repo.GetUserLoginIdAsync(schoolId, callerUserId, ct);
                    if (own == null || !string.Equals(own, employeeId, StringComparison.Ordinal))
                        return Result<TimetableGridDto>(403, "You can only view your own timetable.", null);
                }
                return await TeacherGridAsync(schoolId, employeeId, academicYearId, ct);
            }
            catch (Exception ex) { return Error<TimetableGridDto>(ex, "fetching a teacher timetable"); }
        }

        public async Task<ApiResponse<TimetableGridDto>> GetMyGridAsync(string? schoolId, long callerUserId, long? academicYearId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TimetableGridDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var own = await _repo.GetUserLoginIdAsync(schoolId, callerUserId, ct);
                if (own == null) return Result<TimetableGridDto>(403, "Your account could not be found.", null);
                return await TeacherGridAsync(schoolId, own, academicYearId, ct);
            }
            catch (Exception ex) { return Error<TimetableGridDto>(ex, "fetching my timetable"); }
        }

        public async Task<ApiResponse<TimetableGridDto>> GetStudentMyGridAsync(string? schoolId, long callerUserId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TimetableGridDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<TimetableGridDto>();
                var loginId = await _repo.GetUserLoginIdAsync(schoolId, callerUserId, ct);
                if (string.IsNullOrEmpty(loginId)) return Result<TimetableGridDto>(403, "Your account could not be found.", null);
                var (year, error) = await YearAsync(schoolId, null, ct);
                if (year == null) return Result<TimetableGridDto>(error!.Value.Code, error.Value.Message, null);
                var sectionId = await _repo.GetStudentSectionAsync(schoolId, loginId, year.AcademicYearId, activeId.Value, ct);
                if (sectionId == null) return Result<TimetableGridDto>(404, "You are not enrolled in a class for the current academic year.", null);
                return await GetClassGridAsync(schoolId, sectionId.Value, year.AcademicYearId, ct);   // the one place a section's grid is built
            }
            catch (Exception ex) { return Error<TimetableGridDto>(ex, "fetching a student's timetable"); }
        }

        // ---------------------------------------------------------------- helpers
        private async Task<ApiResponse<TimetableGridDto>> TeacherGridAsync(string schoolId, string employeeId, long? academicYearId, CancellationToken ct)
        {
            var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
            if (activeId == null) return ConfigError<TimetableGridDto>();
            // 404 for another school's employee, and for a login that is not an employee (e.g. a student)
            var teacher = await _repo.GetTeacherAsync(schoolId, employeeId, ct);
            if (teacher == null) return Result<TimetableGridDto>(404, "Employee not found.", null);
            var (year, error) = await YearAsync(schoolId, academicYearId, ct);
            if (year == null) return Result<TimetableGridDto>(error!.Value.Code, error.Value.Message, null);

            var periods = await _repo.GetActivePeriodsAsync(schoolId, activeId.Value, ct);
            var entries = await _repo.GetTeacherEntriesAsync(schoolId, year.AcademicYearId, employeeId, activeId.Value, ct);
            var grid = BuildGrid(year, "teacher", periods, entries);
            grid.EmployeeId = teacher.EmployeeId; grid.EmployeeName = TeacherName(teacher);
            return Result(200, "Successfully fetched", grid);
        }

        /// <summary>The year asked for, or the school's current one (the year containing today).</summary>
        private async Task<(YearRef? Year, (int Code, string Message)? Error)> YearAsync(string schoolId, long? academicYearId, CancellationToken ct)
        {
            var year = academicYearId != null ? await _repo.GetYearAsync(schoolId, academicYearId.Value, ct) : await _repo.GetYearContainingAsync(schoolId, SchoolClock.Today(), ct);
            if (year != null) return (year, null);
            return (null, academicYearId != null ? (400, "The selected academic year was not found for your school.") : (409, "No academic year covers today's date, so choose an academic year."));
        }

        /// <summary>Every active period x Monday..Saturday, with the entry in each cell or none.</summary>
        private static TimetableGridDto BuildGrid(YearRef year, string scope, List<TbPeriod> periods, List<TtEntryRow> entries)
        {
            var byCell = entries.GroupBy(e => (e.Entry.PeriodId, e.Entry.DayOfWeek)).ToDictionary(g => g.Key, g => g.OrderBy(e => e.Entry.TimetableEntryId).First());
            return new TimetableGridDto
            {
                AcademicYearId = year.AcademicYearId, AcademicYearName = year.YearName, Scope = scope, EntryCount = entries.Count,
                Days = DayNames.Select((n, i) => new TimetableDayDto { DayOfWeek = i + 1, Name = n }).ToList(),
                Rows = periods.Select(p => new TimetableRowDto
                {
                    Period = new TimetablePeriodDto { PeriodId = p.PeriodId, PeriodNumber = p.PeriodNumber, StartTime = p.StartTime, EndTime = p.EndTime },
                    Cells = Enumerable.Range(1, 6).Select(d => new TimetableCellDto { DayOfWeek = d, Entry = byCell.TryGetValue((p.PeriodId, (short)d), out var e) ? ToDto(e) : null }).ToList()
                }).ToList()
            };
        }

        private record Resolved(int ActiveId, int InactiveId, SectionRef? Section, short Day, TbPeriod? Period, (int Code, string Message)? Error);

        /// <summary>Statuses, the year, the section, the weekday (Monday to Saturday) and the period, all checked against the caller's school.</summary>
        private async Task<Resolved> ResolveAsync(string schoolId, long academicYearId, long sectionId, long? classId, int day, long periodId, CancellationToken ct, bool requireActiveSection)
        {
            Resolved Bad(int code, string message) => new(0, 0, null, 0, null, (code, message));
            var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
            var inactiveId = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType, ct);
            if (activeId == null || inactiveId == null)
            {
                _logger.LogError("Configuration problem: Active / Inactive statuses are missing in lut_status");
                return Bad(500, "The system is not configured correctly. Please contact support.");
            }
            if (day < 1 || day > 6) return Bad(400, "Day of week must be 1 (Monday) to 6 (Saturday). Sunday is not a school day.");
            if (await _repo.GetYearAsync(schoolId, academicYearId, ct) == null) return Bad(400, "The selected academic year was not found for your school.");
            var section = await _repo.GetSectionAsync(schoolId, sectionId, ct);
            if (section == null) return Bad(400, "The selected section was not found for your school.");
            if (classId != null && classId != section.ClassId) return Bad(400, "The selected section does not belong to the selected class.");
            if (requireActiveSection && (section.SectionStatusId != activeId || section.ClassStatusId != activeId)) return Bad(400, "The selected section (or its class) is inactive.");
            var period = await _repo.GetPeriodAsync(schoolId, periodId, ct);
            if (period == null) return Bad(400, "The selected period was not found for your school.");
            if (requireActiveSection && period.StatusId != activeId) return Bad(400, "The selected period is inactive.");
            return new Resolved(activeId.Value, inactiveId.Value, section, (short)day, period, null);
        }

        private async Task<ApiResponse<TimetableEntryDto>> RowAsync(string schoolId, long entryId, int code, string message, CancellationToken ct)
        {
            var row = await _repo.GetEntryRowAsync(schoolId, entryId, ct);
            return row == null ? Result<TimetableEntryDto>(500, "Something went wrong. Please try again later.", null) : Result(code, message, ToDto(row));
        }

        private static TimetableEntryDto ToDto(TtEntryRow r) => new()
        {
            TimetableEntryId = r.Entry.TimetableEntryId, AcademicYearId = r.Entry.AcademicYearId, ClassId = r.Entry.ClassId, ClassName = r.ClassName, SectionId = r.Entry.SectionId, SectionName = r.SectionName,
            DayOfWeek = r.Entry.DayOfWeek, PeriodId = r.Entry.PeriodId, PeriodNumber = r.PeriodNumber, SubjectId = r.Entry.SubjectId, SubjectName = r.SubjectName, SubjectCode = r.SubjectCode,
            EmployeeId = r.Entry.EmployeeId, EmployeeName = r.EmployeeName, RoomNo = r.Entry.RoomNo
        };

        private static string TeacherName(TtTeacherRow t) => string.Join(' ', new[] { t.FirstName, t.MiddleName, t.LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));

        private ApiResponse<T> ConfigError<T>()
        {
            _logger.LogError("Configuration problem: Active status is missing in lut_status");
            return Result<T>(500, "The system is not configured correctly. Please contact support.", default);
        }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) => new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        private ApiResponse<T> Error<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", default);
        }
    }
}
