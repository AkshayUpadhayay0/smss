using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>
    /// The Principal dashboard, read-only, composed from the existing services (the same ones the School Admin dashboard uses, so the numbers can never disagree).
    /// A part that fails is named in PartsUnavailable and left empty / null: the client shows "Unavailable", never a wrong 0.
    /// </summary>
    public class PrincipalDashboardService : IPrincipalDashboardService
    {
        public const int AttendanceTrendDays = SchoolDashboardService.AttendanceTrendDays;
        public const int RecentRows = 5;
        private const int FeeUpcomingDays = 7;
        private const string PresentCode = "PRESENT";
        private const string AdmissionSubmitted = "APPLICATION_SUBMITTED";
        private const string AdmissionUnderReview = "UNDER_REVIEW";

        private readonly IAttendanceService _attendance;
        private readonly IFeeReportService _fees;
        private readonly IEmployeeService _employees;
        private readonly IEmployeeLeaveService _employeeLeave;
        private readonly IAdmissionService _admissions;
        private readonly IReportResultService _results;
        private readonly ISchoolEventService _events;
        private readonly INoticeService _notices;
        private readonly ISchoolDashboardRepository _repo;
        private readonly ILogger<PrincipalDashboardService> _logger;

        public PrincipalDashboardService(IAttendanceService attendance, IFeeReportService fees, IEmployeeService employees, IEmployeeLeaveService employeeLeave, IAdmissionService admissions,
            IReportResultService results, ISchoolEventService events, INoticeService notices, ISchoolDashboardRepository repo, ILogger<PrincipalDashboardService> logger)
        {
            _attendance = attendance; _fees = fees; _employees = employees; _employeeLeave = employeeLeave; _admissions = admissions;
            _results = results; _events = events; _notices = notices; _repo = repo; _logger = logger;
        }

        public async Task<ApiResponse<PrincipalDashboardDto>> GetSummaryAsync(string? schoolId, long callerUserId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result(400, TenantMessages.NoSchool, null);
            try
            {
                var today = SchoolClock.Today();
                var dto = new PrincipalDashboardDto { Today = today };

                // One DbContext per request: the parts run one after another, each guarded on its own.
                var sections = await Part(() => _attendance.GetMySectionsAsync(schoolId, callerUserId, true, ct), PrincipalDashboardParts.Students, dto);
                var yearId = sections?.AcademicYearId;
                if (sections != null)
                {
                    dto.AcademicYearId = sections.AcademicYearId;
                    dto.AcademicYearName = sections.AcademicYearName;
                    dto.Stats.TotalStudents = sections.Sections.Sum(s => s.RosterCount);
                }

                var staff = await Part(() => _employees.ListAsync(schoolId, null, null, null, null, null, StatusNames.Active, 1, 1, ct), PrincipalDashboardParts.Staff, dto);
                dto.Stats.TotalStaff = staff?.Total;

                await FillAttendanceAsync(schoolId, today, dto, ct);

                if (yearId == null) dto.PartsUnavailable.Add(PrincipalDashboardParts.Fees);   // the fee dashboard is per academic year
                else
                {
                    var fee = await Part(() => _fees.GetDashboardAsync(schoolId, yearId.Value, null, null, FeeUpcomingDays, false, ct), PrincipalDashboardParts.Fees, dto);
                    if (fee != null) { dto.Stats.FeeTotalDue = fee.TotalDue; dto.Stats.FeeCollected = fee.TotalCollected; dto.Stats.FeeOutstanding = fee.TotalOutstanding; }
                }

                var leave = await Part(() => _employeeLeave.ListAsync(schoolId, true, LeaveStatuses.Pending, null, ct), PrincipalDashboardParts.EmployeeLeave, dto);
                dto.Alerts.PendingEmployeeLeaves = leave?.Count;

                var admissions = await Part(() => _admissions.GetAllAsync(schoolId, null, null, null, ct), PrincipalDashboardParts.Admissions, dto);
                if (admissions != null)
                {
                    dto.Alerts.AdmissionsToDecide = admissions.Count(a => a.StatusName == AdmissionUnderReview);
                    dto.Alerts.AdmissionsAwaitingReview = admissions.Count(a => a.StatusName == AdmissionSubmitted);
                }

                var published = await Part(() => _results.ListAsync(schoolId, new ReportCaller(callerUserId, false, true, false), null, null, ct), PrincipalDashboardParts.Results, dto);
                if (published != null) dto.RecentPublications = RecentPublications(published);

                var events = await Part(() => _events.GetUpcomingAsync(schoolId, RecentRows, ct), PrincipalDashboardParts.Events, dto);
                if (events != null) dto.UpcomingEvents = events.Take(RecentRows).ToList();

                var notices = await Part(() => _notices.ListAsync(schoolId, new NoticeCaller(callerUserId, true, false), null, null, null, ct), PrincipalDashboardParts.Notices, dto);
                if (notices != null)
                    dto.RecentNotices = notices.OrderByDescending(n => n.PublishedAt).ThenByDescending(n => n.NoticeId).Take(RecentRows)
                        .Select(n => new DashboardNoticeDto { NoticeId = n.NoticeId, Title = n.Title, Audience = n.Audience, CreatedByName = n.CreatedByName, PublishedAt = n.PublishedAt }).ToList();

                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while building the Principal dashboard");
                return Result(500, "Something went wrong. Please try again later.", null);
            }
        }

        /// <summary>The newest published results first, at most <paramref name="take"/>.</summary>
        public static List<DashboardPublicationDto> RecentPublications(IEnumerable<PublicationDto> all, int take = RecentRows) =>
            all.OrderByDescending(p => p.PublishedAt).ThenByDescending(p => p.PublicationId).Take(take)
               .Select(p => new DashboardPublicationDto
               {
                   PublicationId = p.PublicationId, ClassName = p.ClassName, SectionName = p.SectionName, Scope = p.Scope, TermName = p.TermName,
                   Version = p.Version, Status = p.Status, PublishedAt = p.PublishedAt, StudentCount = p.StudentCount
               }).ToList();

        private async Task FillAttendanceAsync(string schoolId, DateOnly today, PrincipalDashboardDto dto, CancellationToken ct)
        {
            try
            {
                var rows = await _repo.GetAttendanceByDayAsync(schoolId, today.AddDays(-(AttendanceTrendDays - 1)), today, PresentCode, ct);
                dto.AttendanceTrend = SchoolDashboardService.AttendanceTrend(today, rows);
                var last = dto.AttendanceTrend[^1];   // today, from the very same query, so the card and the chart can never disagree
                dto.Stats.AttendanceMarkedToday = last.Marked;
                dto.Stats.AttendancePresentToday = last.Present;
                dto.Stats.AttendancePercentToday = last.PresentPercent;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Principal dashboard: could not load attendance");
                dto.PartsUnavailable.Add(PrincipalDashboardParts.Attendance);
            }
        }

        /// <summary>The data of a part's response; null (logged, and named in PartsUnavailable) when it failed, whether the service answered with a failure or threw.</summary>
        private async Task<T?> Part<T>(Func<Task<ApiResponse<T>>> load, string part, PrincipalDashboardDto dto)
        {
            try
            {
                var response = await load();
                if (response.Status && response.Data != null) return response.Data;
                _logger.LogWarning("Principal dashboard: could not load {Part} ({StatusCode}: {Message})", part, response.StatusCode, response.Message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Principal dashboard: could not load {Part}", part);
            }
            if (!dto.PartsUnavailable.Contains(part)) dto.PartsUnavailable.Add(part);
            return default;
        }

        private static ApiResponse<PrincipalDashboardDto> Result(int code, string message, PrincipalDashboardDto? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };
    }
}
