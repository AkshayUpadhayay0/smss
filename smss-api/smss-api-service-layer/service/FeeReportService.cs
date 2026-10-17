using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class FeeReportService : IFeeReportService
    {
        public const int DefaultUpcomingDays = 7;
        public const int MaxUpcomingDays = 366;

        private readonly IFeeReportRepository _repo;
        private readonly IStudentRepository _students;
        private readonly ILogger<FeeReportService> _logger;

        public FeeReportService(IFeeReportRepository repo, IStudentRepository students, ILogger<FeeReportService> logger)
        {
            _repo = repo;
            _students = students;
            _logger = logger;
        }

        public async Task<ApiResponse<FeeDashboardDto>> GetDashboardAsync(string? schoolId, long academicYearId, long? classId, long? sectionId, int days, bool canManage, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<FeeDashboardDto>(400, TenantMessages.NoSchool, null);
            if (days < 1 || days > MaxUpcomingDays) return Result<FeeDashboardDto>(400, $"days must be between 1 and {MaxUpcomingDays}.", null);
            try
            {
                var year = await _repo.GetYearAsync(schoolId, academicYearId, ct);
                if (year == null) return Result<FeeDashboardDto>(400, "The selected academic year was not found for your school.", null);
                if (classId != null && await _repo.GetClassNameAsync(schoolId, classId.Value, ct) == null)
                    return Result<FeeDashboardDto>(400, "The selected class was not found for your school.", null);
                if (sectionId != null)
                {
                    // a section only makes sense inside a class: with a class given it must be one of that class's sections
                    var ok = classId != null ? await _repo.SectionBelongsToClassAsync(schoolId, classId.Value, sectionId.Value, ct)
                                             : await _students.SectionBelongsToSchoolAsync(schoolId, sectionId.Value, ct);
                    if (!ok) return Result<FeeDashboardDto>(400, "The selected section was not found for your school" + (classId != null ? " and class." : "."), null);
                }

                var today = SchoolClock.Today();
                // two queries for the whole dashboard, whatever the number of students
                var totals = await _repo.GetDashboardTotalsAsync(schoolId, academicYearId, classId, sectionId, today, today.AddDays(days), ct);
                var months = await _repo.GetCollectionByMonthAsync(schoolId, academicYearId, classId, sectionId, ct);

                return Result(200, "Successfully fetched", new FeeDashboardDto
                {
                    AcademicYearId = year.AcademicYearId, AcademicYearName = year.YearName, ClassId = classId, SectionId = sectionId,
                    UpcomingDays = days, AsOf = today,
                    TotalDue = totals.TotalDue, TotalCollected = totals.TotalCollected,
                    TotalOutstanding = Math.Max(totals.TotalDue - totals.TotalCollected, 0),
                    OverdueAmount = totals.OverdueAmount, OverdueCount = totals.OverdueCount,
                    UpcomingAmount = totals.UpcomingAmount, UpcomingCount = totals.UpcomingCount,
                    CollectionByMonth = FillMonths(year.StartDate, year.EndDate, months),
                    CanManage = canManage
                });
            }
            catch (Exception ex) { return Error<FeeDashboardDto>(ex, "building the fee dashboard"); }
        }

        /// <summary>Every month of the academic year (zero where nothing was collected), plus any month outside it that did get a payment.</summary>
        private static List<FeeCollectionMonthDto> FillMonths(DateOnly start, DateOnly end, List<CollectionMonth> collected)
        {
            var byMonth = collected.ToDictionary(m => new DateOnly(m.Year, m.Month, 1), m => m.Amount);
            var keys = new SortedSet<DateOnly>(byMonth.Keys);
            for (var m = new DateOnly(start.Year, start.Month, 1); m <= end; m = m.AddMonths(1)) keys.Add(m);
            return keys.Select(k => new FeeCollectionMonthDto
            {
                Month = k, Label = k.ToDateTime(TimeOnly.MinValue).ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture),
                Amount = byMonth.GetValueOrDefault(k)
            }).ToList();
        }

        public async Task<ApiResponse<ClassFeeReportDto>> GetClassReportAsync(string? schoolId, long classId, long academicYearId, long? sectionId, bool canManage, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ClassFeeReportDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var year = await _repo.GetYearAsync(schoolId, academicYearId, ct);
                if (year == null) return Result<ClassFeeReportDto>(400, "The selected academic year was not found for your school.", null);
                var className = await _repo.GetClassNameAsync(schoolId, classId, ct);
                if (className == null) return Result<ClassFeeReportDto>(404, "Class not found.", null);
                if (sectionId != null && !await _repo.SectionBelongsToClassAsync(schoolId, classId, sectionId.Value, ct))
                    return Result<ClassFeeReportDto>(400, "The selected section was not found in this class.", null);

                var students = await _repo.GetClassStudentsAsync(schoolId, academicYearId, classId, sectionId, ct);
                // the same one grouped query the student list uses, for the whole class at once
                var fees = await _students.GetFeeAggregatesAsync(schoolId, academicYearId, students.Select(s => s.StudentId).ToList(), SchoolClock.Today(), ct);

                var rows = students.Select(s =>
                {
                    fees.TryGetValue(s.StudentId, out var f);
                    var due = f?.TotalDue ?? 0; var paid = f?.TotalPaid ?? 0;
                    return new ClassFeeRowDto
                    {
                        StudentId = s.StudentId,
                        FullName = string.Join(' ', new[] { s.FirstName, s.MiddleName, s.LastName }.Where(p => !string.IsNullOrWhiteSpace(p))),
                        RollNumber = s.RollNumber, SectionName = s.SectionName,
                        TotalDue = due, TotalCollected = paid, Outstanding = Math.Max(due - paid, 0),
                        Status = FeeSchedule.Overall(f != null, due, paid, f?.OverdueCount ?? 0)
                    };
                }).OrderBy(r => r.SectionName, StringComparer.OrdinalIgnoreCase).ThenBy(r => int.TryParse(r.RollNumber, out var n) ? n : int.MaxValue).ThenBy(r => r.RollNumber, StringComparer.OrdinalIgnoreCase)
                  .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase).ToList();

                return Result(200, "Successfully fetched", new ClassFeeReportDto
                {
                    AcademicYearId = year.AcademicYearId, AcademicYearName = year.YearName, ClassId = classId, ClassName = className, SectionId = sectionId,
                    Students = rows,
                    TotalDue = rows.Sum(r => r.TotalDue), TotalCollected = rows.Sum(r => r.TotalCollected), TotalOutstanding = rows.Sum(r => r.Outstanding),
                    StatusCounts = FeeSchedule.OverallStatuses.ToDictionary(s => s, s => rows.Count(r => r.Status == s)),
                    CanManage = canManage
                });
            }
            catch (Exception ex) { return Error<ClassFeeReportDto>(ex, "building the class fee report"); }
        }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        private ApiResponse<T> Error<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", default);
        }
    }
}
