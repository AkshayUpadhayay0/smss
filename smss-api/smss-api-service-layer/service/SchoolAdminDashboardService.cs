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

        public async Task<ApiResponse<SchoolAdminDashboardDto>> GetAsync(string? schoolId, CancellationToken ct, DateOnly? todayOverride = null)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await _repo.ActiveStatusIdAsync(ct) ?? throw new InvalidOperationException("The Active status is missing from lut_status.");
                var today = todayOverride ?? SchoolClock.Today();
                var monthStart = new DateOnly(today.Year, today.Month, 1);
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                var dto = new SchoolAdminDashboardDto { Today = today, MonthStart = monthStart, MonthEnd = monthEnd };

                var year = await _repo.CurrentYearAsync(schoolId, active, ct);
                if (year != null) dto.Session = new DashboardSessionDto { AcademicYearId = year.AcademicYearId, Name = year.YearName, StartDate = year.StartDate, EndDate = year.EndDate };

                dto.TotalStudents = await _repo.ActiveStudentsAsync(schoolId, active, ct);
                var fee = await _repo.FeeMonthAsync(schoolId, active, monthStart, monthEnd, ct);
                dto.StudentsNotPaidThisMonth = fee.StudentsNotPaid;
                dto.StudentsPaidThisMonth = fee.StudentsWithDues - fee.StudentsNotPaid;
                dto.FeeOutstandingThisMonth = fee.Outstanding;

                // India day boundaries as UTC instants: a payment at 23:30 IST on the last evening of the month belongs to that month
                var tomorrowUtc = SchoolClock.StartOfDayUtc(today.AddDays(1));
                dto.FeeCollectedThisMonth = await _repo.CollectedAsync(schoolId, SchoolClock.StartOfDayUtc(monthStart), tomorrowUtc, ct);
                dto.IncomeThisMonth = dto.FeeCollectedThisMonth;   // no other-income table exists: income = fee collected
                if (year != null) dto.IncomeThisSession = await _repo.CollectedAsync(schoolId, SchoolClock.StartOfDayUtc(year.StartDate), tomorrowUtc, ct);
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

                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error while building the School Admin dashboard");
                return Result(500, "Something went wrong. Please try again later.", null);
            }
        }

        private static ApiResponse<SchoolAdminDashboardDto> Result(int code, string message, SchoolAdminDashboardDto? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };
    }
}
