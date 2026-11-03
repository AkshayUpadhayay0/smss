using System.Globalization;
using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class SchoolDashboardService : ISchoolDashboardService
    {
        public const int AttendanceTrendDays = 14;
        public const int FeeTrendMonths = 6;
        public const int RecentRows = 5;
        private const int FeeUpcomingDays = 7;                 // the Fee dashboard's own default; the upcoming figures are not used here
        private const string PresentCode = "PRESENT";
        private const string AdmissionSubmitted = "APPLICATION_SUBMITTED";
        private const string AdmissionUnderReview = "UNDER_REVIEW";

        private readonly IAttendanceService _attendance;
        private readonly IFeeReportService _fees;
        private readonly IEmployeeService _employees;
        private readonly IEmployeeLeaveService _employeeLeave;
        private readonly IStudentLeaveService _studentLeave;
        private readonly IClassTeacherService _classTeachers;
        private readonly ISchoolEventService _events;
        private readonly INoticeService _notices;
        private readonly IAdmissionService _admissions;
        private readonly ISchoolDashboardRepository _repo;
        private readonly ILogger<SchoolDashboardService> _logger;

        public SchoolDashboardService(IAttendanceService attendance, IFeeReportService fees, IEmployeeService employees, IEmployeeLeaveService employeeLeave,
            IStudentLeaveService studentLeave, IClassTeacherService classTeachers, ISchoolEventService events, INoticeService notices, IAdmissionService admissions,
            ISchoolDashboardRepository repo, ILogger<SchoolDashboardService> logger)
        {
            _attendance = attendance;
            _fees = fees;
            _employees = employees;
            _employeeLeave = employeeLeave;
            _studentLeave = studentLeave;
            _classTeachers = classTeachers;
            _events = events;
            _notices = notices;
            _admissions = admissions;
            _repo = repo;
            _logger = logger;
        }

        public async Task<ApiResponse<SchoolDashboardDto>> GetSummaryAsync(string? schoolId, long callerUserId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result(400, TenantMessages.NoSchool, null);
            try
            {
                var today = SchoolClock.Today();
                var dto = new SchoolDashboardDto { Today = today };

                // One DbContext per request, so these run one after another. Each part is guarded on its own: a failure leaves that part empty / null and names it in PartsUnavailable.

                // The current academic year and today's section roster come from Attendance (the year that contains today, as Attendance / Class-Teacher use it).
                var sections = Part(await _attendance.GetMySectionsAsync(schoolId, callerUserId, true, ct), SchoolDashboardParts.Students, dto);
                var yearId = sections?.AcademicYearId;
                if (sections != null)
                {
                    dto.AcademicYearId = sections.AcademicYearId;
                    dto.AcademicYearName = sections.AcademicYearName;
                    dto.Stats.TotalStudents = sections.Sections.Sum(s => s.RosterCount);
                    dto.Stats.SectionsTotal = sections.Sections.Count;
                    dto.Stats.SectionsMarkedToday = sections.Sections.Count(s => s.MarkedToday);
                }

                var employees = Part(await _employees.ListAsync(schoolId, null, null, null, null, null, StatusNames.Active, 1, 1, ct), SchoolDashboardParts.Employees, dto);
                dto.Stats.TotalEmployees = employees?.Total;

                await FillAttendanceAsync(schoolId, today, dto, ct);
                await FillFeesAsync(schoolId, yearId, today, dto, ct);
                await FillAdmissionsAsync(schoolId, dto, ct);

                var employeeLeave = Part(await _employeeLeave.ListAsync(schoolId, true, LeaveStatuses.Pending, null, ct), SchoolDashboardParts.EmployeeLeave, dto);
                dto.Alerts.PendingEmployeeLeaves = employeeLeave?.Count;
                var studentLeave = Part(await _studentLeave.ListAsync(schoolId, callerUserId, true, null, LeaveStatuses.Pending, ct), SchoolDashboardParts.StudentLeave, dto);
                dto.Alerts.PendingStudentLeaves = studentLeave?.Count;

                if (yearId == null) dto.PartsUnavailable.Add(SchoolDashboardParts.ClassTeachers);   // the class teachers are per academic year
                else
                {
                    var classTeachers = Part(await _classTeachers.ListAsync(schoolId, yearId.Value, ct), SchoolDashboardParts.ClassTeachers, dto);
                    dto.Alerts.SectionsWithoutClassTeacher = classTeachers?.UnassignedCount;
                }

                var events = Part(await _events.GetUpcomingAsync(schoolId, RecentRows, ct), SchoolDashboardParts.Events, dto);
                if (events != null) dto.UpcomingEvents = events.Take(RecentRows).ToList();

                var notices = Part(await _notices.ListAsync(schoolId, new NoticeCaller(callerUserId, true, true), null, null, null, ct), SchoolDashboardParts.Notices, dto);
                if (notices != null)
                    dto.RecentNotices = notices.OrderByDescending(n => n.PublishedAt).ThenByDescending(n => n.NoticeId).Take(RecentRows)
                        .Select(n => new DashboardNoticeDto { NoticeId = n.NoticeId, Title = n.Title, Audience = n.Audience, CreatedByName = n.CreatedByName, PublishedAt = n.PublishedAt }).ToList();

                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while building the School Admin dashboard");
                return Result(500, "Something went wrong. Please try again later.", null);
            }
        }

        // ---------------------------------------------------------------- pure helpers (unit-testable)
        /// <summary>Present / marked x 100 to one decimal; null when nothing is marked.</summary>
        public static decimal? PresentPercent(int present, int marked) =>
            marked <= 0 ? null : Math.Round(present * 100m / marked, 1, MidpointRounding.AwayFromZero);

        /// <summary>Exactly <paramref name="days"/> calendar days ending today (oldest first), each with its marks from <paramref name="rows"/> (a day without any has 0 / 0 and a null percentage).</summary>
        public static List<AttendanceTrendPointDto> AttendanceTrend(DateOnly today, IEnumerable<SchoolAttendanceDayRow> rows, int days = AttendanceTrendDays)
        {
            var byDate = rows.ToDictionary(r => r.Date);
            return Enumerable.Range(0, days).Select(i => today.AddDays(i - (days - 1))).Select(d =>
            {
                byDate.TryGetValue(d, out var r);
                return new AttendanceTrendPointDto { Date = d, Marked = r?.Marked ?? 0, Present = r?.Present ?? 0, PresentPercent = PresentPercent(r?.Present ?? 0, r?.Marked ?? 0) };
            }).ToList();
        }

        /// <summary>The first day of each of the last <paramref name="months"/> calendar months, oldest first, the month of <paramref name="today"/> last.</summary>
        public static List<DateOnly> LastMonths(DateOnly today, int months = FeeTrendMonths)
        {
            var thisMonth = new DateOnly(today.Year, today.Month, 1);
            return Enumerable.Range(0, months).Select(i => thisMonth.AddMonths(i - (months - 1))).ToList();
        }

        /// <summary>
        /// The month-by-month collection series for <paramref name="months"/>, summed over every academic year's dashboard given (a payment belongs to the year its installments are in,
        /// so a window that crosses a year boundary needs both). Months nobody paid in are zero.
        /// </summary>
        public static List<FeeCollectionMonthDto> FeeTrend(IEnumerable<DateOnly> months, IEnumerable<FeeDashboardDto> yearDashboards)
        {
            var sums = new Dictionary<DateOnly, decimal>();
            foreach (var m in yearDashboards.SelectMany(d => d.CollectionByMonth))
                sums[m.Month] = sums.GetValueOrDefault(m.Month) + m.Amount;
            return months.Select(m => new FeeCollectionMonthDto { Month = m, Label = m.ToString("MMM yyyy", CultureInfo.InvariantCulture), Amount = sums.GetValueOrDefault(m) }).ToList();
        }

        public static bool IsPendingAdmission(string statusName) => statusName == AdmissionSubmitted || statusName == AdmissionUnderReview;

        // ---------------------------------------------------------------- parts
        private async Task FillAttendanceAsync(string schoolId, DateOnly today, SchoolDashboardDto dto, CancellationToken ct)
        {
            try
            {
                var from = today.AddDays(-(AttendanceTrendDays - 1));
                var rows = await _repo.GetAttendanceByDayAsync(schoolId, from, today, PresentCode, ct);
                dto.AttendanceTrend = AttendanceTrend(today, rows);
                var last = dto.AttendanceTrend[^1];   // today, from the very same query, so the card and the chart can never disagree
                dto.Stats.AttendanceMarkedToday = last.Marked;
                dto.Stats.AttendancePresentToday = last.Present;
                dto.Stats.AttendancePercentToday = last.PresentPercent;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "School dashboard: could not load attendance");
                dto.PartsUnavailable.Add(SchoolDashboardParts.Attendance);
            }
        }

        private async Task FillFeesAsync(string schoolId, long? yearId, DateOnly today, SchoolDashboardDto dto, CancellationToken ct)
        {
            if (yearId == null) { dto.PartsUnavailable.Add(SchoolDashboardParts.Fees); return; }   // the fee dashboard is per academic year
            try
            {
                var current = Part(await _fees.GetDashboardAsync(schoolId, yearId.Value, null, null, FeeUpcomingDays, true, ct), SchoolDashboardParts.Fees, dto);
                if (current == null) return;

                // The six-month window can reach back into the previous academic year, whose payments the current year's dashboard does not hold: add any other year it touches.
                var months = LastMonths(today);
                var dashboards = new List<FeeDashboardDto> { current };
                foreach (var span in await _repo.GetYearsOverlappingAsync(schoolId, months[0], months[^1].AddMonths(1).AddDays(-1), ct))
                {
                    if (span.AcademicYearId == yearId.Value) continue;
                    var other = Part(await _fees.GetDashboardAsync(schoolId, span.AcademicYearId, null, null, FeeUpcomingDays, true, ct), SchoolDashboardParts.Fees, dto);
                    if (other == null) return;   // a partial series would understate a month, so show none
                    dashboards.Add(other);
                }

                dto.FeeCollectionTrend = FeeTrend(months, dashboards);
                dto.Stats.FeeCollectedThisMonth = dto.FeeCollectionTrend[^1].Amount;
                dto.Stats.FeeOutstanding = current.TotalOutstanding;
                dto.Alerts.FeeOverdueCount = current.OverdueCount;
                dto.Alerts.FeeOverdueAmount = current.OverdueAmount;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "School dashboard: could not load fees");
                dto.FeeCollectionTrend = new();
                if (!dto.PartsUnavailable.Contains(SchoolDashboardParts.Fees)) dto.PartsUnavailable.Add(SchoolDashboardParts.Fees);
            }
        }

        private async Task FillAdmissionsAsync(string schoolId, SchoolDashboardDto dto, CancellationToken ct)
        {
            // every application of the school, newest first (the Admission list's own order); the count and the latest five come from that one read
            var all = Part(await _admissions.GetAllAsync(schoolId, null, null, null, ct), SchoolDashboardParts.Admissions, dto);
            if (all == null) return;
            dto.Stats.PendingAdmissions = all.Count(a => IsPendingAdmission(a.StatusName));
            dto.RecentAdmissions = all.OrderByDescending(a => a.ApplicationDate).ThenByDescending(a => a.AdmissionId).Take(RecentRows)
                .Select(a => new DashboardAdmissionDto
                {
                    AdmissionId = a.AdmissionId, ApplicationNumber = a.ApplicationNumber, ApplicantFullName = a.ApplicantFullName,
                    AppliedClassName = a.AppliedClassName, StatusName = a.StatusName, ApplicationDate = a.ApplicationDate
                }).ToList();
        }

        /// <summary>The data of a part's response; null (logged, and named in PartsUnavailable) when it failed.</summary>
        private T? Part<T>(ApiResponse<T> response, string part, SchoolDashboardDto dto)
        {
            if (response.Status && response.Data != null) return response.Data;
            _logger.LogWarning("School dashboard: could not load {Part} ({StatusCode}: {Message})", part, response.StatusCode, response.Message);
            if (!dto.PartsUnavailable.Contains(part)) dto.PartsUnavailable.Add(part);
            return default;
        }

        private static ApiResponse<SchoolDashboardDto> Result(int code, string message, SchoolDashboardDto? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };
    }
}
