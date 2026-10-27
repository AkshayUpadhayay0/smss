using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class StudentDashboardService : IStudentDashboardService
    {
        private const int RecentCount = 5;
        private const string PresentCode = "PRESENT";
        private const string AbsentCode = "ABSENT";

        private readonly IStudentRepository _students;
        private readonly IAttendanceService _attendance;
        private readonly ISchoolEventService _events;
        private readonly INoticeService _notices;
        private readonly ITimetableService _timetable;
        private readonly IAssessmentService _assessments;
        private readonly IStudentLeaveService _leave;
        private readonly ILogger<StudentDashboardService> _logger;

        public StudentDashboardService(IStudentRepository students, IAttendanceService attendance, ISchoolEventService events, INoticeService notices,
            ITimetableService timetable, IAssessmentService assessments, IStudentLeaveService leave, ILogger<StudentDashboardService> logger)
        {
            _students = students;
            _attendance = attendance;
            _events = events;
            _notices = notices;
            _timetable = timetable;
            _assessments = assessments;
            _leave = leave;
            _logger = logger;
        }

        public async Task<ApiResponse<StudentDashboardDto>> GetAsync(string? schoolId, long callerUserId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result(400, TenantMessages.NoSchool, null);
            try
            {
                // the student is the caller: their login id is their student id
                var loginId = await _students.GetUserLoginIdAsync(schoolId, callerUserId, ct);
                var student = string.IsNullOrEmpty(loginId) ? null : await _students.GetStudentAsync(schoolId, loginId, ct);
                if (student == null) return Result(403, "Your account is not linked to a student record.", null);

                // One DbContext per request, so these run one after another. Every part but the profile degrades to empty rather than failing the page.
                var today = SchoolClock.Today();
                var grid = Part(await _timetable.GetStudentMyGridAsync(schoolId, callerUserId, ct), "timetable", allowedMissing: 404);
                var attendance = Part(await _attendance.GetOwnYearAsync(schoolId, callerUserId, ct), "attendance");
                var eventsCount = Part(await _events.CountUpcomingInYearAsync(schoolId, ct), "events count");
                var unread = Part(await _notices.GetUnreadCountAsync(schoolId, new NoticeCaller(callerUserId, false, false), ct), "unread notices");
                var notices = Part(await _notices.GetMyAsync(schoolId, new NoticeCaller(callerUserId, false, false), false, null, null, ct), "notices");
                var upcoming = Part(await _events.GetUpcomingAsync(schoolId, RecentCount, ct), "upcoming events");
                var results = Part(await _assessments.GetStudentMyAsync(schoolId, new AssessmentCaller(callerUserId, false, false, true), null, null, ct), "results");
                var leave = Part(await _leave.GetMyAsync(schoolId, callerUserId, null, null, null, ct), "leave");

                var dto = new StudentDashboardDto
                {
                    Today = today,
                    Profile = await ProfileAsync(schoolId, student.StudentId, student.FirstName, student.MiddleName, student.LastName, student.AdmissionNumber, student.ProfilePhotoUrl, grid?.AcademicYearId, ct),
                    Attendance = attendance == null ? null : new DashboardAttendanceDto
                    {
                        PresentCount = attendance.Summary.ByStatus.GetValueOrDefault(PresentCode),
                        AbsentCount = attendance.Summary.ByStatus.GetValueOrDefault(AbsentCode),
                        TotalMarkedDays = attendance.Summary.MarkedDays,
                        PresentPercentage = attendance.Summary.PresentPercentage,
                        From = attendance.From, To = attendance.To
                    },
                    EventsCount = eventsCount,
                    UnreadNoticesCount = unread?.Count ?? 0,
                    TodayClasses = TodayClasses(grid, today, SchoolClock.TimeNow()),
                    RecentNotices = notices?.Take(RecentCount).ToList() ?? new(),
                    UpcomingEvents = upcoming?.Take(RecentCount).ToList() ?? new(),
                    RecentResults = results?.Take(RecentCount).ToList() ?? new(),
                    RecentLeave = leave?.Take(RecentCount).ToList() ?? new(),
                };
                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while building the student dashboard");
                return Result(500, "Something went wrong. Please try again later.", null);
            }
        }

        /// <summary>
        /// Today's periods from a section grid with a status each: Completed once the period's end time has passed, InProgress between its start (inclusive) and
        /// end (exclusive), Upcoming before it starts. Sunday (and any day with nothing scheduled) gives an empty list. Pure, so it can be tested with any clock.
        /// </summary>
        public static List<TodayClassDto> TodayClasses(TimetableGridDto? grid, DateOnly today, TimeOnly now)
        {
            if (grid == null) return new();
            // the grid's days are 1 = Monday .. 6 = Saturday; .NET's DayOfWeek has Sunday = 0
            var day = today.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)today.DayOfWeek;
            return grid.Rows
                .Select(r => (Row: r, Entry: r.Cells.FirstOrDefault(c => c.DayOfWeek == day)?.Entry))
                .Where(x => x.Entry != null)
                .OrderBy(x => x.Row.Period.StartTime).ThenBy(x => x.Row.Period.PeriodNumber)
                .Select(x => new TodayClassDto
                {
                    PeriodNumber = x.Row.Period.PeriodNumber, SubjectName = x.Entry!.SubjectName, TeacherName = x.Entry.EmployeeName, RoomNo = x.Entry.RoomNo,
                    StartTime = x.Row.Period.StartTime, EndTime = x.Row.Period.EndTime,
                    Status = now >= x.Row.Period.EndTime ? ClassStatuses.Completed : now >= x.Row.Period.StartTime ? ClassStatuses.InProgress : ClassStatuses.Upcoming
                }).ToList();
        }

        /// <summary>The profile card: the student's own record plus the class / section / roll number of the grid's year (or, with no grid, the current / latest enrollment).</summary>
        private async Task<DashboardProfileDto> ProfileAsync(string schoolId, string studentId, string first, string? middle, string last, string admissionNumber, string? photo, long? yearId, CancellationToken ct)
        {
            var enrollments = await _students.GetEnrollmentsAsync(schoolId, studentId, ct);
            var enrollment = yearId != null
                ? enrollments.FirstOrDefault(e => e.AcademicYearId == yearId)
                : enrollments.OrderByDescending(e => e.AcademicYearIsCurrent).ThenByDescending(e => e.AcademicYearStart).ThenByDescending(e => e.EnrollmentId).FirstOrDefault();
            return new DashboardProfileDto
            {
                StudentId = studentId, FullName = LeaveRules.FullName(first, middle, last), AdmissionNumber = admissionNumber, ProfilePhotoUrl = photo,
                ClassName = enrollment?.ClassName, SectionName = enrollment?.SectionName, RollNumber = enrollment?.RollNumber, AcademicYearName = enrollment?.AcademicYearName
            };
        }

        /// <summary>The data of a part's response; null (with a warning in the log) when it failed. <paramref name="allowedMissing"/> is a status that just means "nothing to show" and is not logged.</summary>
        private T? Part<T>(ApiResponse<T> response, string part, int? allowedMissing = null)
        {
            if (response.Status) return response.Data;
            if (response.StatusCode != allowedMissing)
                _logger.LogWarning("Student dashboard: could not load {Part} ({StatusCode}: {Message})", part, response.StatusCode, response.Message);
            return default;
        }

        private static ApiResponse<StudentDashboardDto> Result(int code, string message, StudentDashboardDto? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };
    }
}
