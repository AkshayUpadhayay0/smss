using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class TeacherDashboardService : ITeacherDashboardService
    {
        private const string PresentCode = "PRESENT";
        private const string AbsentCode = "ABSENT";

        private readonly ITimetableRepository _timetableRepo;
        private readonly IEmployeeRepository _employees;
        private readonly ITimetableService _timetable;
        private readonly IClassTeacherService _classTeachers;
        private readonly IAttendanceService _attendance;
        private readonly ITeacherDashboardRepository _repo;
        private readonly ILogger<TeacherDashboardService> _logger;

        public TeacherDashboardService(ITimetableRepository timetableRepo, IEmployeeRepository employees, ITimetableService timetable, IClassTeacherService classTeachers,
            IAttendanceService attendance, ITeacherDashboardRepository repo, ILogger<TeacherDashboardService> logger)
        {
            _timetableRepo = timetableRepo;
            _employees = employees;
            _timetable = timetable;
            _classTeachers = classTeachers;
            _attendance = attendance;
            _repo = repo;
            _logger = logger;
        }

        public async Task<ApiResponse<TeacherDashboardDto>> GetAsync(string? schoolId, long callerUserId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result(400, TenantMessages.NoSchool, null);
            try
            {
                // The teacher is the caller: their login id is their employee id (the very lookup Timetable/teacher/my uses).
                var employeeId = await _timetableRepo.GetUserLoginIdAsync(schoolId, callerUserId, ct);
                var detail = string.IsNullOrEmpty(employeeId) ? null : await _employees.GetDetailAsync(schoolId, employeeId, ct);
                if (detail == null) return Result(403, "Your account is not linked to an employee record.", null);

                var today = SchoolClock.Today();
                var dto = new TeacherDashboardDto
                {
                    Today = today,
                    Profile = new TeacherProfileDto
                    {
                        EmployeeId = detail.Employee.EmployeeId, FullName = LeaveRules.FullName(detail.Employee.FirstName, detail.Employee.MiddleName, detail.Employee.LastName),
                        DesignationName = detail.DesignationName, DepartmentName = detail.DepartmentName, PhotoUrl = detail.Employee.PhotoPath
                    }
                };

                // One DbContext per request, so these run one after another. Each part is guarded on its own: a failure leaves that part empty / null and names it in PartsUnavailable.
                var grid = Part(await _timetable.GetMyGridAsync(schoolId, callerUserId, null, ct), TeacherDashboardParts.Subjects, dto);
                dto.SubjectsTaught = SubjectsTaught(grid);
                if (grid == null)
                {
                    // without the academic year (nothing covers today) the class-teacher sections cannot be found either
                    dto.PartsUnavailable.Add(TeacherDashboardParts.ClassTeacher);
                    return Result(200, "Successfully fetched", dto);
                }

                var list = Part(await _classTeachers.ListAsync(schoolId, grid.AcademicYearId, ct), TeacherDashboardParts.ClassTeacher, dto);
                if (list == null) return Result(200, "Successfully fetched", dto);
                dto.ClassTeacherOf = list.Sections.Where(s => s.EmployeeId == employeeId)
                    .Select(s => new TeacherSectionDto { ClassId = s.ClassId, ClassName = s.ClassName, SectionId = s.SectionId, SectionName = s.SectionName }).ToList();
                if (dto.ClassTeacherOf.Count == 0) return Result(200, "Successfully fetched", dto);   // not a class teacher: the four numbers stay null (not applicable)

                var sectionIds = dto.ClassTeacherOf.Select(s => s.SectionId).ToList();
                await FillRosterAndAttendanceAsync(schoolId, callerUserId, sectionIds, dto, ct);
                await FillFeePendingAsync(schoolId, grid.AcademicYearId, sectionIds, today, dto, ct);
                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while building the teacher dashboard");
                return Result(500, "Something went wrong. Please try again later.", null);
            }
        }

        /// <summary>One class-teacher section's day: whether anyone is marked, and the Present / Absent counts among the current roster.</summary>
        public record SectionDay(bool IsMarked, int Present, int Absent);

        /// <summary>
        /// Folds the class-teacher sections' day into the dashboard numbers. None marked -> NotMarkedYet (no 0 / 0); all marked -> the sums; a mix -> the sums of the
        /// marked ones plus how many are still unmarked. Pure, so every case is unit-testable.
        /// </summary>
        public static (int? Present, int? Absent, bool NotMarkedYet, int Unmarked) SummariseAttendance(IReadOnlyCollection<SectionDay> sections)
        {
            if (sections.Count == 0) return (null, null, false, 0);
            var marked = sections.Where(s => s.IsMarked).ToList();
            if (marked.Count == 0) return (null, null, true, 0);
            return (marked.Sum(s => s.Present), marked.Sum(s => s.Absent), false, sections.Count - marked.Count);
        }

        /// <summary>The distinct subject names of a teacher grid, in alphabetical order.</summary>
        public static List<string> SubjectsTaught(TimetableGridDto? grid) =>
            grid == null ? new() : grid.Rows.SelectMany(r => r.Cells).Where(c => c.Entry != null).Select(c => c.Entry!.SubjectName)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>First day of the month of <paramref name="today"/> and the first day of the next one (exclusive end).</summary>
        public static (DateOnly From, DateOnly ToExclusive) MonthRange(DateOnly today)
        {
            var first = new DateOnly(today.Year, today.Month, 1);
            return (first, first.AddMonths(1));
        }

        /// <summary>Roster size and today's marks for every class-teacher section, through the Attendance service (which also re-checks the caller is that section's class teacher).</summary>
        private async Task FillRosterAndAttendanceAsync(string schoolId, long callerUserId, List<long> sectionIds, TeacherDashboardDto dto, CancellationToken ct)
        {
            var days = new List<SectionDay>();
            var roster = 0;
            foreach (var sectionId in sectionIds)
            {
                var section = Part(await _attendance.GetSectionAsync(schoolId, callerUserId, false, sectionId, null, ct), TeacherDashboardParts.Attendance, dto);
                if (section == null)
                {
                    // one section failed: both the head count and the attendance would be partial, so report them unavailable rather than wrong
                    dto.PartsUnavailable.Add(TeacherDashboardParts.Students);
                    return;
                }
                roster += section.RosterCount;
                var current = section.Students.Where(s => s.OnRoster).ToList();
                days.Add(new SectionDay(section.IsMarked, current.Count(s => s.StatusCode == PresentCode), current.Count(s => s.StatusCode == AbsentCode)));
            }
            dto.TotalStudents = roster;
            (dto.PresentToday, dto.AbsentToday, dto.NotMarkedYet, dto.UnmarkedSections) = SummariseAttendance(days);
        }

        private async Task FillFeePendingAsync(string schoolId, long academicYearId, List<long> sectionIds, DateOnly today, TeacherDashboardDto dto, CancellationToken ct)
        {
            try
            {
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) throw new InvalidOperationException("Active status is missing in lut_status");
                var (from, toExclusive) = MonthRange(today);
                dto.FeePending = await _repo.CountStudentsWithUnpaidDuesAsync(schoolId, academicYearId, sectionIds, activeId.Value, from, toExclusive, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Teacher dashboard: could not count students with fees due");
                dto.PartsUnavailable.Add(TeacherDashboardParts.Fees);
            }
        }

        /// <summary>The data of a part's response; null (logged, and named in PartsUnavailable) when it failed.</summary>
        private T? Part<T>(ApiResponse<T> response, string part, TeacherDashboardDto dto)
        {
            if (response.Status && response.Data != null) return response.Data;
            _logger.LogWarning("Teacher dashboard: could not load {Part} ({StatusCode}: {Message})", part, response.StatusCode, response.Message);
            if (!dto.PartsUnavailable.Contains(part)) dto.PartsUnavailable.Add(part);
            return default;
        }

        private static ApiResponse<TeacherDashboardDto> Result(int code, string message, TeacherDashboardDto? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };
    }
}
