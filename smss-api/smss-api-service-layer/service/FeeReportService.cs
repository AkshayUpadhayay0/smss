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
        private readonly ILateFeeReportRepository? _lateFees;
        private readonly IEarlierYearDuesRepository? _earlier;

        public FeeReportService(IFeeReportRepository repo, IStudentRepository students, ILogger<FeeReportService> logger, ILateFeeReportRepository? lateFees = null, IEarlierYearDuesRepository? earlier = null)
        {
            _lateFees = lateFees;
            _earlier = earlier;
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
                // late fee and tax by month, for the Late fee and Total received columns; if they cannot be read the fee figures are still shown (the two columns stay empty, never 0)
                List<LateFeeMonthRow>? lateMonths = null;
                if (_lateFees != null)
                {
                    try { lateMonths = await _lateFees.GetByMonthAsync(schoolId, academicYearId, classId, sectionId, ct); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Fee dashboard: the late fee by month could not be read"); }
                }
                var taxRows = await _repo.GetTaxCollectedAsync(schoolId, academicYearId, classId, sectionId, ct);
                var concessionRows = await _repo.GetConcessionGivenAsync(schoolId, academicYearId, classId, sectionId, ct);
                var concessionGiven = concessionRows.Sum(c => c.Amount);

                return Result(200, "Successfully fetched", new FeeDashboardDto
                {
                    AcademicYearId = year.AcademicYearId, AcademicYearName = year.YearName, ClassId = classId, SectionId = sectionId,
                    UpcomingDays = days, AsOf = today,
                    // cash collected = what the installments count as paid minus the discounted part; outstanding is driven by what is PAID (a discounted installment is cleared)
                    TotalDue = totals.TotalDue, TotalCollected = totals.TotalPaid - concessionGiven,
                    ConcessionGiven = concessionGiven,
                    ConcessionByType = concessionRows.Select(c => new FeeConcessionBreakdownDto { ConcessionId = c.ConcessionId, ConcessionName = c.ConcessionName, ConcessionCode = c.ConcessionCode, ConcessionAmount = c.Amount, PaymentCount = c.PaymentCount }).ToList(),
                    TaxCollected = taxRows.Sum(t => t.Amount),
                    TaxByType = taxRows.Select(t => new FeeTaxBreakdownDto { TaxTypeId = t.TaxTypeId, TaxName = t.TaxName, TaxCode = t.TaxCode, TaxAmount = t.Amount, PaymentCount = t.PaymentCount }).ToList(),
                    TotalOutstanding = Math.Max(totals.TotalDue - totals.TotalPaid, 0),
                    OverdueAmount = totals.OverdueAmount, OverdueCount = totals.OverdueCount,
                    UpcomingAmount = totals.UpcomingAmount, UpcomingCount = totals.UpcomingCount,
                    CollectionByMonth = FillMonths(year.StartDate, year.EndDate, months, lateMonths),
                    CanManage = canManage
                });
            }
            catch (Exception ex) { return Error<FeeDashboardDto>(ex, "building the fee dashboard"); }
        }

        /// <summary>Every month of the academic year (zero where nothing was collected), plus any month outside it that did get a payment.</summary>
        private static List<FeeCollectionMonthDto> FillMonths(DateOnly start, DateOnly end, List<CollectionMonth> collected, List<LateFeeMonthRow>? late = null)
        {
            var byMonth = collected.ToDictionary(m => new DateOnly(m.Year, m.Month, 1), m => m.Amount);
            var lateBy = (late ?? new()).ToDictionary(m => new DateOnly(m.Year, m.Month, 1));
            var keys = new SortedSet<DateOnly>(byMonth.Keys.Concat(lateBy.Keys));
            for (var m = new DateOnly(start.Year, start.Month, 1); m <= end; m = m.AddMonths(1)) keys.Add(m);
            return keys.Select(k => new FeeCollectionMonthDto
            {
                Month = k, Label = k.ToDateTime(TimeOnly.MinValue).ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture),
                Amount = byMonth.GetValueOrDefault(k),
                LateFee = late == null ? null : lateBy.GetValueOrDefault(k)?.LateFee ?? 0m,
                TotalReceived = late == null ? null : byMonth.GetValueOrDefault(k) + (lateBy.GetValueOrDefault(k)?.LateFee ?? 0m) + (lateBy.GetValueOrDefault(k)?.Tax ?? 0m)
            }).ToList();
        }

        public async Task<ApiResponse<ClassFeeReportDto>> GetClassReportAsync(string? schoolId, long classId, long academicYearId, long? sectionId, bool canManage, CancellationToken ct, DateOnly? dueMonth = null)
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
                // optional month view: only installments due in that calendar month count, and only students who have one are listed
                DateOnly? monthFrom = dueMonth == null ? null : new DateOnly(dueMonth.Value.Year, dueMonth.Value.Month, 1);
                DateOnly? monthTo = monthFrom?.AddMonths(1).AddDays(-1);
                var fees = await _students.GetFeeAggregatesAsync(schoolId, academicYearId, students.Select(s => s.StudentId).ToList(), SchoolClock.Today(), ct, monthFrom, monthTo);
                // what each still owes from years BEFORE this one: its own column (a failure leaves the column at 0 and is logged, the report still works)
                var earlier = new Dictionary<string, decimal>();
                if (_earlier != null && students.Count > 0)
                {
                    try
                    {
                        var rowsEarlier = await _earlier.GetOutstandingAsync(schoolId, students.Select(s => s.StudentId).ToList(), students.Select(_ => year.StartDate).ToList(), SchoolClock.Today(), ct);
                        foreach (var g in rowsEarlier.GroupBy(r => r.StudentId)) earlier[g.Key] = g.Sum(r => r.Outstanding);
                    }
                    catch (Exception ex) { _logger.LogWarning(ex, "Class fee report: earlier-year dues could not be read"); }
                }
                // a student who left (or was transferred out) is no longer counted in the class, but stays on the report while anything is unpaid, flagged Left
                students = students.Where(s => s.Live || (fees.TryGetValue(s.StudentId, out var lf) && lf.TotalDue > lf.TotalPaid) || earlier.GetValueOrDefault(s.StudentId) > 0).ToList();
                if (monthFrom != null) students = students.Where(s => fees.TryGetValue(s.StudentId, out var f) && f.TotalDue > 0).ToList();
                var concessions = await _repo.GetConcessionGivenByStudentAsync(schoolId, academicYearId, students.Select(s => s.StudentId).ToList(), ct);

                var rows = students.Select(s =>
                {
                    fees.TryGetValue(s.StudentId, out var f);
                    var due = f?.TotalDue ?? 0; var paid = f?.TotalPaid ?? 0;   // paid = what the installments count as paid (cash + discounted part)
                    var concession = concessions.GetValueOrDefault(s.StudentId);
                    return new ClassFeeRowDto
                    {
                        StudentId = s.StudentId,
                        FullName = string.Join(' ', new[] { s.FirstName, s.MiddleName, s.LastName }.Where(p => !string.IsNullOrWhiteSpace(p))),
                        RollNumber = s.RollNumber, SectionName = s.SectionName,
                        TotalDue = due, TotalCollected = paid - concession, Concession = concession, Outstanding = Math.Max(due - paid, 0),
                        Status = FeeSchedule.Overall(f != null, due, paid, f?.OverdueCount ?? 0),
                        EarlierYears = earlier.GetValueOrDefault(s.StudentId), Left = s.Left
                    };
                }).OrderBy(r => r.SectionName, StringComparer.OrdinalIgnoreCase).ThenBy(r => int.TryParse(r.RollNumber, out var n) ? n : int.MaxValue).ThenBy(r => r.RollNumber, StringComparer.OrdinalIgnoreCase)
                  .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase).ToList();

                return Result(200, "Successfully fetched", new ClassFeeReportDto
                {
                    AcademicYearId = year.AcademicYearId, AcademicYearName = year.YearName, ClassId = classId, ClassName = className, SectionId = sectionId, Month = monthFrom?.ToString("yyyy-MM"),
                    Students = rows,
                    TotalDue = rows.Sum(r => r.TotalDue), TotalCollected = rows.Sum(r => r.TotalCollected), TotalConcession = rows.Sum(r => r.Concession), TotalOutstanding = rows.Sum(r => r.Outstanding), TotalEarlierYears = rows.Sum(r => r.EarlierYears),
                    StatusCounts = FeeSchedule.OverallStatuses.ToDictionary(s => s, s => rows.Count(r => r.Status == s)),
                    CanManage = canManage
                });
            }
            catch (Exception ex) { return Error<ClassFeeReportDto>(ex, "building the class fee report"); }
        }

        public async Task<ApiResponse<ConcessionsGivenDto>> GetConcessionsGivenAsync(string? schoolId, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ConcessionsGivenDto>(400, TenantMessages.NoSchool, null);
            if (from != null && to != null && to < from) return Result<ConcessionsGivenDto>(400, "The end date can't be before the start date.", null);
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 25;
            if (pageSize > 100) pageSize = 100;
            try
            {
                // the dates are India calendar days, like every other date on the fee screens
                DateTime? fromUtc = from == null ? null : SchoolClock.StartOfDayUtc(from.Value);
                DateTime? toExclusiveUtc = to == null ? null : SchoolClock.StartOfDayUtc(to.Value.AddDays(1));
                var result = await _repo.GetConcessionLogAsync(schoolId, fromUtc, toExclusiveUtc, page, pageSize, ct);
                return Result(200, "Successfully fetched", new ConcessionsGivenDto
                {
                    Page = page, PageSize = pageSize, TotalItems = result.TotalRows, TotalAmount = result.ActiveTotal,
                    Items = result.Rows.Select(r => new ConcessionGivenRowDto
                    {
                        PaymentId = r.PaymentId, ReceiptNumber = r.ReceiptNumber, PaymentDate = r.PaymentDate, StudentId = r.StudentId, StudentName = r.StudentName,
                        ConcessionName = r.ConcessionName, ConcessionCode = r.ConcessionCode, ConcessionType = r.ConcessionType, ConcessionValue = r.ConcessionValue,
                        ConcessionAmount = r.ConcessionAmount, Reason = r.Reason, AppliedByName = r.AppliedByName, Status = r.Status
                    }).ToList()
                });
            }
            catch (Exception ex) { return Error<ConcessionsGivenDto>(ex, "listing the concessions given"); }
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
