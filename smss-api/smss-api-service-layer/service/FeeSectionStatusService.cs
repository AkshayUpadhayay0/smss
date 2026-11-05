using System.Globalization;
using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>
    /// The class teacher's read-only, MONTH-WISE fee status of their class. The figures come from the existing instalments and the amounts the existing payment allocations applied to them
    /// (see FeeMonth); nothing is allocated here. Two reads for the fees (who has fee items, and every instalment of the section's students), whatever the class size.
    /// </summary>
    public class FeeSectionStatusService : IFeeSectionStatusService
    {
        private readonly IStudentRepository _students;
        private readonly IAttendanceRepository _lookups;
        private readonly IClassTeacherService _classTeachers;
        private readonly ILogger<FeeSectionStatusService> _logger;

        public FeeSectionStatusService(IStudentRepository students, IAttendanceRepository lookups, IClassTeacherService classTeachers, ILogger<FeeSectionStatusService> logger)
        {
            _students = students;
            _lookups = lookups;
            _classTeachers = classTeachers;
            _logger = logger;
        }

        /// <summary>Section + year (404 for another school's), then WHICH teacher (403). Returns the error response, or the resolved rows.</summary>
        private async Task<(ApiResponse<T>? Error, SectionRef? Section, AttendanceYearRow? Year, List<StudentListRow>? Roster, Dictionary<string, StudentFeeAggregate>? HasItems, List<StudentDueRow>? Dues)> LoadAsync<T>(
            string? schoolId, long callerUserId, bool callerIsSchoolAdmin, long sectionId, long? academicYearId, CancellationToken ct)
        {
            ApiResponse<T> Fail(int code, string msg) => new() { Status = false, StatusCode = code, Message = msg, Data = default };
            if (string.IsNullOrWhiteSpace(schoolId)) return (Fail(400, TenantMessages.NoSchool), null, null, null, null, null);
            // 404 for a section / year of another school (or none), whoever asks: nothing can be probed
            var section = await _lookups.GetSectionAsync(schoolId, sectionId, ct);
            if (section == null) return (Fail(404, "Section not found."), null, null, null, null, null);
            var yearId = academicYearId ?? await _lookups.GetCurrentYearIdAsync(schoolId, ct);
            if (yearId == null) return (Fail(400, "No current academic year is set for this school."), null, null, null, null, null);
            var year = await _lookups.GetYearSpanAsync(schoolId, yearId.Value, ct);
            if (year == null) return (Fail(404, "The selected academic year was not found for your school."), null, null, null, null, null);

            if (!callerIsSchoolAdmin)
            {
                var loginId = await _students.GetUserLoginIdAsync(schoolId, callerUserId, ct);
                var answer = string.IsNullOrEmpty(loginId) ? null : await _classTeachers.IsClassTeacherAsync(schoolId, loginId, sectionId, year.AcademicYearId, ct);
                if (answer == null || !answer.Status || answer.Data?.IsClassTeacher != true)
                    return (Fail(403, "You can only see the fee status of the section you are the class teacher of."), null, null, null, null, null);
            }

            var statuses = await _students.GetGeneralStatusNamesAsync(ct);
            var roster = (await _students.GetEnrolledStudentsAsync(schoolId, year.AcademicYearId, sectionId, ct)).Where(r => statuses.GetValueOrDefault(r.StatusId) == StatusNames.Active).ToList();
            var ids = roster.Select(r => r.StudentId).ToList();
            var withItems = await _students.GetFeeAggregatesAsync(schoolId, year.AcademicYearId, ids, SchoolClock.Today(), ct);   // presence = has fee items
            var dues = await _students.GetFeeDueRowsAsync(schoolId, year.AcademicYearId, ids, ct);
            return (null, section, year, roster, withItems, dues);
        }

        private static string FullName(StudentListRow r) => string.Join(' ', new[] { r.FirstName, r.MiddleName, r.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)));

        private static string Label(int y, int m) => new DateOnly(y, m, 1).ToString("MMM yyyy", CultureInfo.InvariantCulture);

        private static List<SectionFeeMonthDto> MonthList(AttendanceYearRow year, DateOnly today) =>
            FeeMonth.MonthsOf(year.StartDate, year.EndDate).Select(x => new SectionFeeMonthDto { Month = FeeMonth.Key(x.Year, x.Month), Label = Label(x.Year, x.Month), IsCurrent = x.Year == today.Year && x.Month == today.Month }).ToList();

        public async Task<ApiResponse<SectionFeeStatusDto>> GetAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, long sectionId, long? academicYearId, string? month, CancellationToken ct)
        {
            try
            {
                var today = SchoolClock.Today();
                int y, m;
                var asked = !string.IsNullOrWhiteSpace(month);
                if (asked && !FeeMonth.TryParse(month!.Trim(), out y, out m)) return Result<SectionFeeStatusDto>(400, "month must look like 2026-11.", null);

                var l = await LoadAsync<SectionFeeStatusDto>(schoolId, callerUserId, callerIsSchoolAdmin, sectionId, academicYearId, ct);
                if (l.Error != null) return l.Error;
                var year = l.Year!;
                if (asked) FeeMonth.TryParse(month!.Trim(), out y, out m); else (y, m) = FeeMonth.Default(today, year.StartDate, year.EndDate);

                var dto = new SectionFeeStatusDto
                {
                    SectionId = l.Section!.SectionId, ClassName = l.Section.ClassName, SectionName = l.Section.SectionName, AcademicYearId = year.AcademicYearId, AcademicYearName = year.YearName,
                    Month = FeeMonth.Key(y, m), MonthLabel = Label(y, m), IsCurrentMonth = y == today.Year && m == today.Month, Months = MonthList(year, today)
                };
                dto.MonthInAcademicYear = dto.Months.Any(x => x.Month == dto.Month);
                if (!dto.MonthInAcademicYear) return Result(200, "That month is outside the academic year.", dto);

                var byStudent = l.Dues!.GroupBy(d => d.StudentId).ToDictionary(g => g.Key, g => g.ToList());
                dto.Students = l.Roster!.Select(r => FeeMonth.Student(r.RollNumber, FullName(r), byStudent.GetValueOrDefault(r.StudentId) ?? new List<StudentDueRow>(), y, m, today)).ToList();
                dto.Students = dto.Students
                    .OrderBy(s => int.TryParse(s.RollNumber, out var n) ? n : int.MaxValue).ThenBy(s => s.RollNumber, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.FullName, StringComparer.OrdinalIgnoreCase).ToList();

                var sm = dto.Summary;
                sm.Students = dto.Students.Count;
                sm.Paid = dto.Students.Count(s => s.MonthStatus == FeeMonth.Paid); sm.Partial = dto.Students.Count(s => s.MonthStatus == FeeMonth.Partial);
                sm.Unpaid = dto.Students.Count(s => s.MonthStatus == FeeMonth.Unpaid); sm.Overdue = dto.Students.Count(s => s.MonthStatus == FeeMonth.Overdue);
                sm.NotDueYet = dto.Students.Count(s => s.MonthStatus == FeeMonth.NotDueYet); sm.NoFeeThisMonth = dto.Students.Count(s => s.MonthStatus == FeeMonth.NoFee);
                sm.NoFeeAssigned = l.Roster!.Count(r => !l.HasItems!.ContainsKey(r.StudentId));
                sm.DueTotal = dto.Students.Sum(s => s.DueThisMonth); sm.CollectedTotal = dto.Students.Sum(s => s.PaidThisMonth);
                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while building a section's monthly fee status");
                return Result<SectionFeeStatusDto>(500, "Something went wrong. Please try again later.", null);
            }
        }

        public async Task<ApiResponse<SectionFeeGridDto>> GetGridAsync(string? schoolId, long callerUserId, bool callerIsSchoolAdmin, long sectionId, long? academicYearId, CancellationToken ct)
        {
            try
            {
                var l = await LoadAsync<SectionFeeGridDto>(schoolId, callerUserId, callerIsSchoolAdmin, sectionId, academicYearId, ct);
                if (l.Error != null) return l.Error;
                var today = SchoolClock.Today();
                var year = l.Year!;
                var months = FeeMonth.MonthsOf(year.StartDate, year.EndDate);
                var byStudent = l.Dues!.GroupBy(d => d.StudentId).ToDictionary(g => g.Key, g => g.ToList());
                var grid = new SectionFeeGridDto
                {
                    SectionId = l.Section!.SectionId, ClassName = l.Section.ClassName, SectionName = l.Section.SectionName, AcademicYearId = year.AcademicYearId, AcademicYearName = year.YearName,
                    Months = MonthList(year, today),
                    Students = l.Roster!
                        .OrderBy(r => int.TryParse(r.RollNumber, out var n) ? n : int.MaxValue).ThenBy(r => r.RollNumber, StringComparer.OrdinalIgnoreCase).ThenBy(r => FullName(r), StringComparer.OrdinalIgnoreCase)
                        .Select(r =>
                        {
                            var dues = byStudent.GetValueOrDefault(r.StudentId) ?? new List<StudentDueRow>();
                            return new SectionFeeGridRowDto
                            {
                                RollNumber = r.RollNumber, FullName = FullName(r),
                                Cells = months.Select(x => FeeMonth.Code(FeeMonth.StatusOf(dues.Where(d => d.DueDate.Year == x.Year && d.DueDate.Month == x.Month).ToList(), x.Year, x.Month, today))).ToList()
                            };
                        }).ToList()
                };
                return Result(200, "Successfully fetched", grid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while building a section's fee grid");
                return Result<SectionFeeGridDto>(500, "Something went wrong. Please try again later.", null);
            }
        }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };
    }
}
