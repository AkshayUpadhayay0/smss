using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public interface IFeeBackdatingService
    {
        Task<ApiResponse<BackdatingPreviewDto>> PreviewAsync(string? schoolId, long feeStructureId, long feeTypeId, DateOnly? fromDate, CancellationToken ct);
        Task<ApiResponse<BackdatingResultDto>> ApplyAsync(string? schoolId, long userId, long feeStructureId, long feeTypeId, ApplyBackdatingRequest req, CancellationToken ct);
    }

    /// <summary>
    /// The explicit "Apply to earlier months" of one fee line: creates, for every student on the structure, the dues the schedule would have had from a past date up to today, marked
    /// BACKDATED and logged. School Admin only; current academic year only; preview first, then a reason and a confirmation; one transaction; running it again creates nothing new.
    /// Applying also moves the line's effective date back to that date, so the normal sync stays consistent with the dues that now exist.
    /// </summary>
    public class FeeBackdatingService : IFeeBackdatingService
    {
        private readonly IStudentFeeBillingRepository _repo;
        private readonly IFeeStructureRepository _structures;
        private readonly ILogger<FeeBackdatingService> _logger;

        public FeeBackdatingService(IStudentFeeBillingRepository repo, IFeeStructureRepository structures, ILogger<FeeBackdatingService> logger)
        {
            _repo = repo;
            _structures = structures;
            _logger = logger;
        }

        private sealed record Candidate(TbStudentFeeItem Item, List<FeeSchedule.Scheduled> Dues);
        private sealed record Context(TbFeeStructures Structure, SourceLineRow Line, TbAcademicYears Year, List<Candidate> Candidates);

        public async Task<ApiResponse<BackdatingPreviewDto>> PreviewAsync(string? schoolId, long feeStructureId, long feeTypeId, DateOnly? fromDate, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail<BackdatingPreviewDto>(400, TenantMessages.NoSchool);
            try
            {
                var (ctx, error) = await LoadAsync(schoolId, feeStructureId, feeTypeId, fromDate, ct);
                if (ctx == null) return error!.As<BackdatingPreviewDto>();
                var today = SchoolClock.Today();
                var names = await _repo.GetStudentNamesAsync(schoolId, ctx.Candidates.Select(c => c.Item.StudentId).ToList(), ct);

                var students = ctx.Candidates.Select(c => new BackdatingStudentDto
                {
                    StudentId = c.Item.StudentId, StudentName = names.GetValueOrDefault(c.Item.StudentId, c.Item.StudentId), DuesCount = c.Dues.Count, Amount = c.Dues.Sum(d => d.Amount)
                }).OrderBy(s => s.StudentName, StringComparer.OrdinalIgnoreCase).ToList();
                return Ok(new BackdatingPreviewDto
                {
                    FeeStructureId = feeStructureId, FeeTypeId = feeTypeId, FeeTypeName = ctx.Line.FeeTypeName, FromDate = fromDate!.Value, ToDate = today,
                    CurrentEffectiveFrom = ctx.Line.EffectiveFrom, StudentsCount = students.Count, DuesCount = students.Sum(s => s.DuesCount), TotalAmount = students.Sum(s => s.Amount), Students = students
                });
            }
            catch (Exception ex) { return Failed<BackdatingPreviewDto>(ex, "previewing the back-dated dues"); }
        }

        public async Task<ApiResponse<BackdatingResultDto>> ApplyAsync(string? schoolId, long userId, long feeStructureId, long feeTypeId, ApplyBackdatingRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail<BackdatingResultDto>(400, TenantMessages.NoSchool);
            var reason = req.Reason?.Trim();
            if (string.IsNullOrEmpty(reason)) return Fail<BackdatingResultDto>(400, "A reason is required to apply a fee to earlier months.");
            if (reason.Length > 500) return Fail<BackdatingResultDto>(400, "The reason can be at most 500 characters.");
            if (!req.Confirm) return Fail<BackdatingResultDto>(400, "Confirm that you have seen the preview before applying to earlier months.");
            try
            {
                // one structure save / back-dating at a time per school
                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockStructureSavesAsync(schoolId, ct);

                var (ctx, error) = await LoadAsync(schoolId, feeStructureId, feeTypeId, req.FromDate, ct);
                if (ctx == null) return error!.As<BackdatingResultDto>();
                var today = SchoolClock.Today();
                var from = req.FromDate!.Value;

                var count = ctx.Candidates.Sum(c => c.Dues.Count);
                if (req.ExpectedDuesCount is int expected && expected != count)
                    return Fail<BackdatingResultDto>(409, $"The preview showed {expected} due(s) but {count} would be created now. Review the preview again.");

                if (count == 0)
                    return new ApiResponse<BackdatingResultDto>
                    {
                        Status = true, StatusCode = 200, Message = "Nothing to create: the earlier months already have their dues.",
                        Data = new BackdatingResultDto { BackdatingLogId = null, EffectiveFrom = ctx.Line.EffectiveFrom }
                    };

                var now = DateTime.UtcNow;
                var log = new TbFeeBackdatingLog
                {
                    SchoolId = schoolId, Kind = "LINE", FeeStructureId = feeStructureId, FeeTypeId = feeTypeId, AcademicYearId = ctx.Year.AcademicYearId, FromDate = from, ToDate = today,
                    StudentsCount = ctx.Candidates.Count(c => c.Dues.Count > 0), DuesCount = count, TotalAmount = ctx.Candidates.Sum(c => c.Dues.Sum(d => d.Amount)),
                    Reason = reason, CreatedBy = userId, CreatedAt = now
                };
                foreach (var c in ctx.Candidates)
                    foreach (var d in c.Dues)
                        c.Item.Dues.Add(new TbStudentFeeDue
                        {
                            StudentId = c.Item.StudentId, SchoolId = schoolId, DueDate = d.Date, AmountDue = d.Amount, AmountPaid = 0, CreatedAt = now, UpdatedAt = now,
                            Source = "BACKDATED", BackdatingLog = log
                        });

                // the line is now charged from that date too (so the normal sync agrees with the dues that exist)
                var structure = await _structures.GetWithDetailsForUpdateAsync(schoolId, feeStructureId);
                var detail = structure?.Details.FirstOrDefault(d => d.FeeTypeId == feeTypeId);
                var newEffective = ctx.Line.EffectiveFrom;
                if (detail != null && from < detail.EffectiveFrom) { detail.EffectiveFrom = from; detail.UpdatedAt = now; newEffective = from; }

                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return new ApiResponse<BackdatingResultDto>
                {
                    Status = true, StatusCode = 200, Message = $"{count} due(s) created for {log.StudentsCount} student(s).",
                    Data = new BackdatingResultDto { BackdatingLogId = log.BackdatingLogId, StudentsCount = log.StudentsCount, DuesCount = count, TotalAmount = log.TotalAmount, EffectiveFrom = newEffective }
                };
            }
            catch (Exception ex) { return Failed<BackdatingResultDto>(ex, "applying a fee to earlier months"); }
        }

        /// <summary>Validates the request and works out which dues would be created (nothing is written).</summary>
        private async Task<(Context?, Failure?)> LoadAsync(string schoolId, long feeStructureId, long feeTypeId, DateOnly? fromDate, CancellationToken ct)
        {
            var structure = await _structures.GetByIdAsync(schoolId, feeStructureId);
            if (structure == null) return (null, new Failure(404, "Fee structure not found"));   // another school's structure too
            var year = await _repo.GetAcademicYearAsync(schoolId, structure.AcademicYearId, ct);
            if (year == null) return (null, new Failure(400, "The academic year of this structure was not found."));
            var line = (await _repo.GetSourceLinesAsync(feeStructureId, ct)).FirstOrDefault(l => l.FeeTypeId == feeTypeId);
            if (line == null) return (null, new Failure(404, "That fee is not a line of this fee structure."));

            if (fromDate == null) return (null, new Failure(400, "A date to apply from is required."));
            var today = SchoolClock.Today();
            if (!year.IsCurrent) return (null, new Failure(400, "Fees can be applied to earlier months only in the current academic year."));
            if (fromDate.Value < year.StartDate) return (null, new Failure(400, $"The date can't be before the academic year starts ({year.StartDate:dd MMM yyyy})."));
            if (fromDate.Value >= today) return (null, new Failure(400, "Choose a date before today: only earlier months can be applied."));

            var items = (await _repo.GetItemsForStructureLineAsync(schoolId, feeStructureId, feeTypeId, ct)).Where(i => i.IsActive && i.IsApplicable).ToList();
            var starts = await _repo.GetEnrollmentStartsAsync(schoolId, year.AcademicYearId, items.Select(i => i.StudentId).Distinct().ToList(), ct);
            var single = FeeSchedule.Installments(line.FrequencyCode) == 1;

            var candidates = new List<Candidate>();
            foreach (var item in items)
            {
                // a student only owes from their own start; a month that already has a due (paid or not) is never created again
                var studentFrom = starts.TryGetValue(item.StudentId, out var s) && s > fromDate.Value ? s : fromDate.Value;
                var full = FeeSchedule.FullSchedule(year.StartDate, line.FrequencyCode, line.DueDay, FeeSchedule.NormalisePlan(item.InstallmentPlan), item.NetAmount);
                var covered = item.Dues.Select(d => FeeSchedule.Slot(d.DueDate)).ToHashSet();
                var dues = full.Where(f => f.Date >= studentFrom && f.Date <= today && f.Amount > 0 && !covered.Contains(FeeSchedule.Slot(f.Date)) && (!single || item.Dues.Count == 0)).ToList();
                if (dues.Count > 0) candidates.Add(new Candidate(item, dues));
            }
            return (new Context(structure, line, year, candidates), null);
        }

        private sealed record Failure(int Code, string Message)
        {
            public ApiResponse<T> As<T>() => new() { Status = false, StatusCode = Code, Message = Message, Data = default };
        }

        private static ApiResponse<T> Ok<T>(T data) => new() { Status = true, StatusCode = 200, Message = "Successfully fetched", Data = data };
        private static ApiResponse<T> Fail<T>(int code, string message) => new() { Status = false, StatusCode = code, Message = message, Data = default };

        private ApiResponse<T> Failed<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Fail<T>(500, "Something went wrong. Please try again later.");
        }
    }
}
