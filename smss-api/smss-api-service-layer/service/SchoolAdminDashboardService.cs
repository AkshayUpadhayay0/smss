using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>
    /// The redesigned School Admin dashboard: database aggregates only, one query after another on the request's context. "Today" and "this month" are India dates.
    /// Definitions: collected = cash received (allocations of Active payments minus the discounted part, by India payment date); a due is cleared when amount_paid >= amount_due
    /// (a discount counts as paid); session = the current Active academic year from its start to today.
    /// </summary>
    public class SchoolAdminDashboardService : ISchoolAdminDashboardService
    {
        private const string PresentCode = "PRESENT";
        private readonly ISchoolAdminDashboardRepository _repo;
        private readonly ISchoolDashboardRepository _attendance;
        private readonly ILogger<SchoolAdminDashboardService> _logger;

        public SchoolAdminDashboardService(ISchoolAdminDashboardRepository repo, ISchoolDashboardRepository attendance, ILogger<SchoolAdminDashboardService> logger)
        {
            _repo = repo; _attendance = attendance; _logger = logger;
        }

        public async Task<ApiResponse<SchoolAdminDashboardDto>> GetAsync(string? schoolId, CancellationToken ct, DateOnly? todayOverride = null, long? academicYearId = null)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await _repo.ActiveStatusIdAsync(ct) ?? throw new InvalidOperationException("The Active status is missing from lut_status.");
                var today = todayOverride ?? SchoolClock.Today();
                var dto = new SchoolAdminDashboardDto { Today = today };

                var current = await _repo.CurrentYearAsync(schoolId, active, ct);
                dto.CurrentYearName = current?.YearName;
                // the year being VIEWED: the one asked for (404 for another school's, like any foreign id), else the current one
                var year = current;
                if (academicYearId != null)
                {
                    year = await _repo.YearAsync(schoolId, academicYearId.Value, ct);
                    if (year == null) return Result(404, "Academic year not found.", null);
                }
                if (year != null) dto.Session = new DashboardSessionDto { AcademicYearId = year.AcademicYearId, Name = year.YearName, StartDate = year.StartDate, EndDate = year.EndDate, IsCurrent = year.IsCurrent, IsClosed = year.IsClosed };

                // "this month" is the calendar month of today, moved INTO the viewed year's range when today is outside it
                var monthDay = MonthDay(today, year, out var clampedNote);
                var monthStart = new DateOnly(monthDay.Year, monthDay.Month, 1);
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                dto.MonthStart = monthStart; dto.MonthEnd = monthEnd;
                dto.MonthClamped = clampedNote != null; dto.MonthNote = clampedNote;

                if (year != null)
                {
                    dto.TotalStudents = await _repo.EnrolledStudentsAsync(schoolId, active, year.AcademicYearId, ct);
                    dto.HasFeeSetup = await _repo.HasFeeItemsAsync(schoolId, year.AcademicYearId, ct);
                    if (dto.HasFeeSetup)
                    {
                        var fee = await _repo.FeeMonthAsync(schoolId, active, year.AcademicYearId, monthStart, monthEnd, ct);
                        dto.StudentsNotPaidThisMonth = fee.StudentsNotPaid;
                        dto.StudentsPaidThisMonth = fee.StudentsWithDues - fee.StudentsNotPaid;
                        dto.FeeOutstandingThisMonth = fee.Outstanding;
                        // India day boundaries as UTC instants: a payment at 23:30 IST on the last evening of the month belongs to that month.
                        // The running month stops at today; a past or future month is bounded by its own last day.
                        var monthEndExclusive = SchoolClock.StartOfDayUtc(monthEnd.AddDays(1));
                        var upTo = today >= monthStart && today <= monthEnd ? SchoolClock.StartOfDayUtc(today.AddDays(1)) : monthEndExclusive;
                        dto.FeeCollectedThisMonth = await _repo.CollectedAsync(schoolId, year.AcademicYearId, SchoolClock.StartOfDayUtc(monthStart), upTo, ct);
                        dto.IncomeThisMonth = dto.FeeCollectedThisMonth;   // no other-income table exists: income = fee collection only
                        dto.IncomeThisSession = await _repo.CollectedAsync(schoolId, year.AcademicYearId, null, null, ct);
                    }
                }
                dto.ExpensesSetUp = false;
                dto.OtherIncomeSetUp = false;

                var att = (await _attendance.GetAttendanceByDayAsync(schoolId, today, today, PresentCode, ct)).FirstOrDefault();
                dto.AttendanceMarkedToday = att?.Marked ?? 0;
                dto.AttendancePresentToday = att?.Present ?? 0;

                if (year != null) dto.NewAdmissionsThisSession = await _repo.NewAdmissionsAsync(schoolId, active, year.StartDate, year.EndDate, ct);

                var emp = await _repo.EmployeesAsync(schoolId, active, ct);
                dto.Employees = new DashboardEmployeesDto
                {
                    Total = emp.Total, Teachers = emp.Teachers, Accountants = emp.Accountants, AdmissionStaff = emp.AdmissionStaff, Drivers = emp.Drivers,
                    Other = emp.Total - emp.Teachers - emp.Accountants - emp.AdmissionStaff - emp.Drivers,
                };
                dto.ActiveVehicles = await _repo.ActiveVehiclesAsync(schoolId, active, ct);
                dto.WhatsAppMissing = await _repo.WhatsAppMissingAsync(schoolId, ct);

                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error while building the School Admin dashboard");
                return Result(500, "Something went wrong. Please try again later.", null);
            }
        }

        /// <summary>The day whose calendar month is "this month": today when it is inside the year, else the year's first / last day, with a sentence saying so.</summary>
        public static DateOnly MonthDay(DateOnly today, DashboardYearRow? year, out string? note)
        {
            note = null;
            if (year == null || (today >= year.StartDate && today <= year.EndDate)) return today;
            var day = today < year.StartDate ? year.StartDate : year.EndDate;
            note = "Showing " + day.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture) + ": today is " + (today < year.StartDate ? "before" : "after") + " the academic year " + year.YearName + ".";
            return day;
        }

        private static ApiResponse<SchoolAdminDashboardDto> Result(int code, string message, SchoolAdminDashboardDto? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };
    }
}
