using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>
    /// "Assign fees for a year": gives every active, enrolled student of the year who has NO fee structure the active structure of their class, through the same code admission uses
    /// (FeeAutoAssignment: the assignment, then the items and dues). A student who already has a structure is never touched, so running it twice assigns nothing the second time. Optional-fee
    /// choices carry over from the previous year. Dues start from today (this never back-dates; use the student's Fees tab for that). Best-effort per student, and capped per run.
    /// </summary>
    public class FeeRepairService : IFeeRepairService
    {
        public const int MaxPerRun = 500;
        private readonly IFeeRepairRepository _repair;
        private readonly IStudentFeeBillingRepository _years;
        private readonly IAdmissionRepository _admission;
        private readonly IStudentFeeBillingService _billing;
        private readonly ILogger<FeeRepairService> _logger;

        public FeeRepairService(IFeeRepairRepository repair, IStudentFeeBillingRepository years, IAdmissionRepository admission, IStudentFeeBillingService billing, ILogger<FeeRepairService> logger)
        {
            _repair = repair; _years = years; _admission = admission; _billing = billing; _logger = logger;
        }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) where T : class => new() { Status = code is >= 200 and < 300, StatusCode = code, Message = message, Data = data };

        public async Task<ApiResponse<FeeRepairPreviewDto>> PreviewAsync(string? schoolId, long academicYearId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<FeeRepairPreviewDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var year = await _years.GetAcademicYearAsync(schoolId, academicYearId, ct);
                if (year == null) return Result<FeeRepairPreviewDto>(404, "The academic year was not found.", null);
                var missing = await _repair.ListMissingAsync(schoolId, academicYearId, ct);
                var dto = new FeeRepairPreviewDto
                {
                    AcademicYearId = academicYearId, YearName = year.YearName, MissingCount = missing.Count, MaxPerRun = MaxPerRun,
                    WouldAssign = Math.Min(MaxPerRun, missing.Count(m => m.FeeStructureId != null)),
                    ClassesWithoutStructure = missing.Where(m => m.FeeStructureId == null).GroupBy(m => m.ClassName).Select(g => new FeeRepairNoStructureDto { ClassName = g.Key, Students = g.Count() }).OrderBy(g => g.ClassName).ToList(),
                };
                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fee repair preview failed for year {YearId}", academicYearId);
                return Result<FeeRepairPreviewDto>(500, "The students without fees could not be checked. Please try again.", null);
            }
        }

        public async Task<ApiResponse<FeeRepairResultDto>> RunAsync(string? schoolId, long academicYearId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<FeeRepairResultDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var year = await _years.GetAcademicYearAsync(schoolId, academicYearId, ct);
                if (year == null) return Result<FeeRepairResultDto>(404, "The academic year was not found.", null);
                if (year.ClosedAt != null) return Result<FeeRepairResultDto>(400, "This academic year is closed: fees cannot be assigned in it.", null);

                var missing = await _repair.ListMissingAsync(schoolId, academicYearId, ct);
                var result = new FeeRepairResultDto { AcademicYearId = academicYearId, YearName = year.YearName, SkippedNoStructure = missing.Count(m => m.FeeStructureId == null) };
                var todo = missing.Where(m => m.FeeStructureId != null).ToList();
                var batch = todo.Take(MaxPerRun).ToList();
                result.Remaining = todo.Count - batch.Count;

                var choices = (await _repair.GetPreviousChoicesAsync(schoolId, academicYearId, batch.Select(b => b.StudentId).ToList(), ct))
                    .GroupBy(c => c.StudentId).ToDictionary(g => g.Key, g => g.Select(c => new FeeItemSelection { FeeTypeId = c.FeeTypeId, InstallmentPlan = c.InstallmentPlan, IsApplicable = c.IsApplicable }).ToList());

                foreach (var m in batch)
                {
                    var warning = await FeeAutoAssignment.TryAssignAsync(_admission, _billing, choices.GetValueOrDefault(m.StudentId), _logger, schoolId, m.StudentId, academicYearId, m.FeeStructureId!.Value, DateTime.UtcNow, ct);
                    if (warning == null) result.Assigned++;
                    else result.Failed.Add(new FeeRepairStudentDto { StudentId = m.StudentId, StudentName = m.StudentName, ClassName = m.ClassName, Reason = warning });
                }
                var msg = result.Assigned == 0 && result.Failed.Count == 0 ? "Nobody needed fees assigned." : $"Fees assigned to {result.Assigned} student(s).";
                return Result(200, msg, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fee repair run failed for year {YearId}", academicYearId);
                return Result<FeeRepairResultDto>(500, "Fees could not be assigned. Please try again.", null);
            }
        }
    }
}
