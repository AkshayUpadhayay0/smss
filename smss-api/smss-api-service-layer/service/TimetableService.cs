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
        private readonly TimetableVersionService _versions;

        public TimetableService(ITimetableRepository repo, ILogger<TimetableService> logger, TimetableVersionService versions)
        {
            _repo = repo;
            _logger = logger;
            _versions = versions;
        }

        /// <summary>Convenience for callers (and tests) that build the service by hand: the version service is created over the same repository.</summary>
        public TimetableService(ITimetableRepository repo, ILogger<TimetableService> logger)
            : this(repo, logger, new TimetableVersionService(repo, Microsoft.Extensions.Logging.Abstractions.NullLogger<TimetableVersionService>.Instance)) { }

        // ---------------------------------------------------------------- reuse: is this teacher teaching this subject to this section?
        public async Task<bool> IsTeachingSectionSubjectAsync(string? schoolId, string employeeId, long sectionId, long subjectId, long academicYearId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId) || string.IsNullOrWhiteSpace(employeeId)) return false;
            var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
            if (activeId == null) return false;
            return await _repo.IsTeachingSectionSubjectAsync(schoolId, employeeId.Trim(), sectionId, subjectId, academicYearId, activeId.Value, ct);
        }

        // ---------------------------------------------------------------- assign / reassign / unassign
        // The original endpoints act on the section's ACTIVE timetable (created as "Regular timetable" when the section has none yet). Versions have their own endpoints.
        public async Task<ApiResponse<TimetableEntryDto>> AssignAsync(string? schoolId, long assignedByUserId, AssignTimetableRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TimetableEntryDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var ctx = await ResolveAsync(schoolId, req.AcademicYearId!.Value, req.SectionId!.Value, req.ClassId, req.DayOfWeek!.Value, req.PeriodId!.Value, ct, requireActiveSection: true);
                if (ctx.Error != null) return Result<TimetableEntryDto>(ctx.Error.Value.Code, ctx.Error.Value.Message, null);
                var (tt, error) = await _versions.ActiveOrNewAsync(schoolId, assignedByUserId, req.AcademicYearId.Value, ctx.Section!.SectionId, ct);
                if (tt == null) return Result<TimetableEntryDto>(500, error ?? "Something went wrong. Please try again later.", null);
                return await _versions.AssignAsync(schoolId, assignedByUserId, tt.TimetableId,
                    new AssignSlotRequest { DayOfWeek = ctx.Day, PeriodId = req.PeriodId, SubjectId = req.SubjectId, EmployeeId = req.EmployeeId, RoomNo = req.RoomNo }, ct);
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
                var tt = await _repo.GetActiveTimetableAsync(schoolId, req.AcademicYearId.Value, req.SectionId.Value, ct);
                if (tt == null) return Result<TimetableEntryDto>(404, "There is nothing scheduled in this slot.", null);
                return await _versions.UnassignAsync(schoolId, 0, tt.TimetableId, new UnassignSlotRequest { DayOfWeek = ctx.Day, PeriodId = req.PeriodId }, ct);
            }
            catch (Exception ex) { return Error<TimetableEntryDto>(ex, "clearing a timetable slot"); }
        }

        // ---------------------------------------------------------------- reads
        public async Task<ApiResponse<TimetableLookupsDto>> GetLookupsAsync(string? schoolId, long sectionId, long? academicYearId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TimetableLookupsDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<TimetableLookupsDto>();
                var section = await _repo.GetSectionAsync(schoolId, sectionId, ct);
                if (section == null) return Result<TimetableLookupsDto>(404, "Section not found.", null);

                var (year, yearError) = await YearAsync(schoolId, academicYearId, ct);
                if (year == null) return Result<TimetableLookupsDto>(yearError!.Value.Code, yearError.Value.Message, null);
                var subjects = await _repo.GetSubjectsOfSectionAsync(schoolId, year.AcademicYearId, section.SectionId, activeId.Value, ct);
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

                // the ACTIVE timetable of the section: drafts and archived versions never show here
                var entries = await _repo.GetSectionEntriesAsync(schoolId, year.AcademicYearId, sectionId, activeId.Value, ct);
                var grid = await SectionGridAsync(schoolId, activeId.Value, year, section, entries, ct);
                var active = await _repo.GetActiveTimetableAsync(schoolId, year.AcademicYearId, sectionId, ct);
                if (active != null) { grid.TimetableId = active.TimetableId; grid.TimetableName = active.TimetableName; grid.TimetableState = active.State; }
                return Result(200, "Successfully fetched", grid);
            }
            catch (Exception ex) { return Error<TimetableGridDto>(ex, "fetching a section timetable"); }
        }

        /// <summary>The grid of ONE timetable (any state): the editor of a draft or the view of an archived version.</summary>
        public async Task<ApiResponse<TimetableGridDto>> GetTimetableGridAsync(string? schoolId, long timetableId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TimetableGridDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<TimetableGridDto>();
                var tt = await _repo.GetTimetableAsync(schoolId, timetableId, ct);
                if (tt == null) return Result<TimetableGridDto>(404, "Timetable not found.", null);
                var year = await _repo.GetYearAsync(schoolId, tt.AcademicYearId, ct);
                var section = await _repo.GetSectionAsync(schoolId, tt.SectionId, ct);
                if (year == null || section == null) return Result<TimetableGridDto>(404, "Timetable not found.", null);
                var entries = await _repo.GetTimetableRowsAsync(schoolId, tt.TimetableId, activeId.Value, ct);
                var grid = await SectionGridAsync(schoolId, activeId.Value, year, section, entries, ct);
                grid.TimetableId = tt.TimetableId; grid.TimetableName = tt.TimetableName; grid.TimetableState = tt.State;
                return Result(200, "Successfully fetched", grid);
            }
            catch (Exception ex) { return Error<TimetableGridDto>(ex, "fetching a timetable"); }
        }

        private async Task<TimetableGridDto> SectionGridAsync(string schoolId, int activeId, YearRef year, SectionRef section, List<TtEntryRow> entries, CancellationToken ct)
        {
            var slots = await _repo.GetActiveSlotsAsync(schoolId, activeId, ct);
            // S4: the slots of the schedule this class follows on each day (the default schedule everywhere unless the year assigns another)
            var assignments = await _repo.GetYearScheduleRowsAsync(schoolId, year.AcademicYearId, ct);
            var defaultId = await _repo.GetDefaultScheduleIdAsync(schoolId, ct);
            var perDay = Enumerable.Range(1, 6).ToDictionary(d => d, d => ScheduleResolver.Resolve(assignments, section.ClassId, d, defaultId));
            var used = perDay.Values.Where(v => v != null).Select(v => v!.Value).ToHashSet();
            var slotById = slots.ToDictionary(p => p.PeriodId);
            used.UnionWith(entries.Where(e => slotById.ContainsKey(e.Entry.PeriodId)).Select(e => slotById[e.Entry.PeriodId].ScheduleId));   // never hide a lesson that exists
            var names = await _repo.GetScheduleNamesAsync(schoolId, ct);
            var grid = BuildGrid(year, "section", slots.Where(p => used.Contains(p.ScheduleId)).ToList(), entries, names, (p, d) => perDay[d] == p.ScheduleId, used.Count > 1);
            grid.SectionId = section.SectionId; grid.ClassId = section.ClassId; grid.ClassName = section.ClassName; grid.SectionName = section.SectionName;
            return grid;
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
                return await GetStudentGridAsync(schoolId, loginId, ct);
            }
            catch (Exception ex) { return Error<TimetableGridDto>(ex, "fetching a student's timetable"); }
        }

        /// <summary>The weekly grid of ONE student's current section, read-only. Only for a caller the access helper has cleared (the student, or a linked parent).</summary>
        public async Task<ApiResponse<TimetableGridDto>> GetStudentGridAsync(string? schoolId, string loginId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TimetableGridDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<TimetableGridDto>();
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

            var slots = await _repo.GetActiveSlotsAsync(schoolId, activeId.Value, ct);
            var entries = await _repo.GetTeacherEntriesAsync(schoolId, year.AcademicYearId, employeeId, activeId.Value, ct);
            // the default schedule's slots, plus the schedules the teacher's lessons sit on
            var defaultId = await _repo.GetDefaultScheduleIdAsync(schoolId, ct);
            var slotById = slots.ToDictionary(p => p.PeriodId);
            var used = entries.Where(e => slotById.ContainsKey(e.Entry.PeriodId)).Select(e => slotById[e.Entry.PeriodId].ScheduleId).ToHashSet();
            if (defaultId != null) used.Add(defaultId.Value);
            var names = await _repo.GetScheduleNamesAsync(schoolId, ct);
            var grid = BuildGrid(year, "teacher", slots.Where(p => used.Contains(p.ScheduleId)).ToList(), entries, names, (_, _) => true, used.Count > 1);
            grid.EmployeeId = teacher.EmployeeId; grid.EmployeeName = TeacherName(teacher);
            return Result(200, "Successfully fetched", grid);
        }

        /// <summary>The schedule a class follows on a day of a year: the year's assignment, else the school's default (null when the school has no schedule).</summary>
        private async Task<long?> FollowedScheduleAsync(string schoolId, long academicYearId, long classId, int day, CancellationToken ct) =>
            ScheduleResolver.Resolve(await _repo.GetYearScheduleRowsAsync(schoolId, academicYearId, ct), classId, day, await _repo.GetDefaultScheduleIdAsync(schoolId, ct));

        /// <summary>The year asked for, or the school's current one (the year containing today).</summary>
        private async Task<(YearRef? Year, (int Code, string Message)? Error)> YearAsync(string schoolId, long? academicYearId, CancellationToken ct)
        {
            var year = academicYearId != null ? await _repo.GetYearAsync(schoolId, academicYearId.Value, ct) : await _repo.GetYearContainingAsync(schoolId, SchoolClock.Today(), ct);
            if (year != null) return (year, null);
            return (null, academicYearId != null ? (400, "The selected academic year was not found for your school.") : (409, "No academic year covers today's date, so choose an academic year."));
        }

        /// <summary>Every active period x Monday..Saturday, with the entry in each cell or none.</summary>
        private static TimetableGridDto BuildGrid(YearRef year, string scope, List<TbPeriod> periods, List<TtEntryRow> entries, IReadOnlyDictionary<long, string> scheduleNames, Func<TbPeriod, int, bool> applies, bool mixed)
        {
            // one schedule: its own order (the classic grid); several: by clock time
            periods = mixed ? periods.OrderBy(p => p.StartTime).ThenBy(p => p.SlotOrder).ToList() : periods.OrderBy(p => p.SlotOrder).ToList();
            var byCell = entries.GroupBy(e => (e.Entry.PeriodId, e.Entry.DayOfWeek)).ToDictionary(g => g.Key, g => g.OrderBy(e => e.Entry.TimetableEntryId).First());
            return new TimetableGridDto
            {
                AcademicYearId = year.AcademicYearId, AcademicYearName = year.YearName, Scope = scope, EntryCount = entries.Count,
                Days = DayNames.Select((n, i) => new TimetableDayDto { DayOfWeek = i + 1, Name = n }).ToList(),
                Rows = periods.Select(p => new TimetableRowDto
                {
                    Period = new TimetablePeriodDto
                    {
                        PeriodId = p.PeriodId, PeriodNumber = p.PeriodNumber ?? 0, SlotType = p.SlotType, Label = p.SlotLabel, ScheduleId = p.ScheduleId,
                        ScheduleName = mixed ? scheduleNames.GetValueOrDefault(p.ScheduleId) : null, StartTime = p.StartTime, EndTime = p.EndTime,
                        Days = Enumerable.Range(1, 6).Where(d => applies(p, d)).ToList()
                    },
                    Cells = Enumerable.Range(1, 6).Select(d => new TimetableCellDto { DayOfWeek = d, Applies = applies(p, d), Entry = byCell.TryGetValue((p.PeriodId, (short)d), out var e) ? ToDto(e) : null }).ToList()
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
            if (await _repo.IsYearClosedAsync(schoolId, academicYearId, ct)) return Bad(409, ClosedYear.Message);
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
            EmployeeId = r.Entry.EmployeeId ?? "", EmployeeName = r.EmployeeName, RoomNo = r.Entry.RoomNo, NeedsTeacher = r.Entry.NeedsTeacher, TimetableId = r.Entry.TimetableId
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
