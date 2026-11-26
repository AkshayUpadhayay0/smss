using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.helper;

namespace smss_api_service_layer.service
{
    /// <summary>
    /// Record leaving (Stage 8a). A student leaves by an OUTCOME on their latest enrolment (LEFT or TRANSFERRED_OUT) plus the leaving date, reason and who: no new enrolment is created and
    /// tb_students.status_id is NOT changed. The dues check is promotion's: unpaid dues of the enrolment's year due on or before the leaving date block it unless the admin overrides with a reason of at
    /// least 10 characters. The student's refresh tokens are revoked in the same transaction. A correction undoes it (only while no later enrolment exists).
    /// </summary>
    public class LeavingService : ILeavingService
    {
        private readonly ILeavingRepository _repo;
        private readonly ILogger<LeavingService> _logger;
        public LeavingService(ILeavingRepository repo, ILogger<LeavingService> logger) { _repo = repo; _logger = logger; }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) where T : class => new() { Status = code is >= 200 and < 300, StatusCode = code, Message = message, Data = data };
        private const string NotFound = "Student not found.";
        private const string Failed = "The request could not be completed. Please try again.";

        private async Task<(LeavingCheckDto Dto, LeavingContext Ctx)?> BuildAsync(string schoolId, string studentId, DateOnly? requestedDate, CancellationToken ct)
        {
            var ctx = await _repo.GetContextAsync(schoolId, studentId, ct);
            if (ctx == null) return null;
            var today = SchoolClock.Today();
            var date = requestedDate ?? today;
            var dto = new LeavingCheckDto
            {
                StudentId = studentId, StudentName = ctx.StudentName, AdmissionNumber = ctx.AdmissionNumber, ClassName = ctx.ClassName, SectionName = ctx.SectionName,
                YearName = ctx.YearName, EnrollmentDate = ctx.EnrollmentDate, LeavingDate = date,
            };
            if (!string.IsNullOrEmpty(ctx.Outcome))
                dto.Blockers.Add(ctx.Outcome == LeavingRules.PassedOut ? $"{ctx.StudentName} has already passed out of {ctx.YearName}."
                    : ctx.Outcome is LeavingRules.Left or LeavingRules.TransferredOut ? $"{ctx.StudentName} has already been recorded as {(ctx.Outcome == LeavingRules.Left ? "left" : "transferred out")}."
                    : $"{ctx.StudentName} has already been moved on from {ctx.YearName}.");
            if (!ctx.EnrolmentActive) dto.Blockers.Add("The enrolment is not active.");
            if (ctx.YearClosed) dto.Blockers.Add(ClosedYear.Message);
            var dateProblem = LeavingRules.DateProblem(date, today, ctx.EnrollmentDate);
            if (dateProblem != null) dto.Blockers.Add(dateProblem);
            var dues = await _repo.ListUnpaidDuesAsync(schoolId, studentId, ctx.YearId, ct);
            foreach (var d in dues)
            {
                var open = d.AmountDue - d.AmountPaid;
                if (d.DueDate <= date)
                {
                    dto.Outstanding += open;
                    dto.Dues.Add(new LeavingDueDto { FeeDueId = d.FeeDueId, FeeType = d.FeeTypeName, DueDate = d.DueDate, AmountDue = d.AmountDue, AmountPaid = d.AmountPaid, Outstanding = open });
                }
                else dto.NotYetDue += open;
            }
            dto.RequiresOverride = dto.Outstanding > 0;
            dto.CanRecord = dto.Blockers.Count == 0;
            return (dto, ctx);
        }

        public async Task<ApiResponse<LeavingCheckDto>> CheckAsync(string? schoolId, string? studentId, DateOnly? leavingDate, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<LeavingCheckDto>(400, TenantMessages.NoSchool, null);
            if (string.IsNullOrWhiteSpace(studentId)) return Result<LeavingCheckDto>(400, "Enter a student id.", null);
            try
            {
                var b = await BuildAsync(schoolId, studentId.Trim(), leavingDate, ct);
                return b == null ? Result<LeavingCheckDto>(404, NotFound, null) : Result(200, "Successfully fetched", b.Value.Dto);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Leaving check failed for {StudentId}", studentId);
                return Result<LeavingCheckDto>(500, Failed, null);
            }
        }

        public async Task<ApiResponse<RecordLeavingResultDto>> RecordAsync(string? schoolId, long userId, RecordLeavingRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<RecordLeavingResultDto>(400, TenantMessages.NoSchool, null);
            if (request == null) return Result<RecordLeavingResultDto>(400, "The request is empty.", null);
            var type = request.Type?.Trim().ToUpperInvariant();
            if (!LeavingRules.IsLeavingType(type)) return Result<RecordLeavingResultDto>(400, "Choose Left or Transferred out.", null);
            var reason = request.Reason?.Trim();
            if (string.IsNullOrEmpty(reason)) return Result<RecordLeavingResultDto>(400, "Give the reason for leaving.", null);
            if (reason.Length > 500) return Result<RecordLeavingResultDto>(400, "The reason can be at most 500 characters.", null);
            if (!request.Confirm) return Result<RecordLeavingResultDto>(400, "Recording a leaving ends the student's login: confirm first.", null);
            try
            {
                var date = request.LeavingDate ?? SchoolClock.Today();
                var b = await BuildAsync(schoolId, request.StudentId.Trim(), date, ct);
                if (b == null) return Result<RecordLeavingResultDto>(404, NotFound, null);
                var (dto, ctx) = b.Value;
                if (!dto.CanRecord) return Result<RecordLeavingResultDto>(409, dto.Blockers[0], null);
                var ovrProblem = LeavingRules.OverrideProblem(dto.Outstanding, request.OverrideDues, request.OverrideReason);
                if (ovrProblem != null) return Result<RecordLeavingResultDto>(400, ovrProblem, null);
                var overridden = dto.Outstanding > 0;
                await using var tx = await _repo.BeginTransactionAsync(ct);
                var ok = await _repo.ApplyAsync(new LeavingWrite(schoolId, dto.StudentId, ctx.EnrollmentId, type!, date, reason, userId, overridden, overridden ? request.OverrideReason!.Trim() : null, dto.Outstanding), DateTime.UtcNow, ct);
                if (!ok)
                {
                    await tx.RollbackAsync(ct);
                    return Result<RecordLeavingResultDto>(409, "This student's enrolment was changed by someone else. Reload and check again.", null);
                }
                await tx.CommitAsync(ct);
                return Result(200, $"{ctx.StudentName} is recorded as {(type == LeavingRules.Left ? "left" : "transferred out")}.",
                    new RecordLeavingResultDto { StudentId = dto.StudentId, StudentName = ctx.StudentName, Outcome = type!, LeavingDate = date, Outstanding = dto.Outstanding, OverrideApplied = overridden });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Record leaving failed for {StudentId}", request.StudentId);
                return Result<RecordLeavingResultDto>(500, Failed, null);
            }
        }

        private async Task<(LeavingCorrectionCheckDto Dto, LeavingCorrectionContext Ctx)?> CorrectionCoreAsync(string schoolId, string studentId, CancellationToken ct)
        {
            var c = await _repo.GetCorrectionContextAsync(schoolId, studentId, ct);
            if (c == null) return null;
            var dto = new LeavingCorrectionCheckDto { StudentId = studentId, StudentName = c.StudentName, YearName = c.YearName, Outcome = c.Outcome, LeavingDate = c.LeavingDate };
            if (c.Outcome is not (LeavingRules.Left or LeavingRules.TransferredOut))
                dto.Blockers.Add($"{c.StudentName} has not been recorded as left or transferred out: there is nothing to correct.");
            else if (c.FromPromotion)
                dto.Blockers.Add($"The outcome {c.Outcome} of {c.StudentName} was recorded by a promotion run, so it is corrected on the Promotion screen.");
            if (c.LaterEnrolments > 0) dto.Blockers.Add($"{c.StudentName} has a later enrolment: a leaving can no longer be corrected.");
            if (c.YearClosed) dto.Blockers.Add(ClosedYear.Message);
            if (c.Certificates > 0) dto.Blockers.Add("A transfer certificate has been issued for this student: void it first.");
            dto.CanCorrect = dto.Blockers.Count == 0;
            return (dto, c);
        }

        public async Task<ApiResponse<LeavingCorrectionCheckDto>> CheckCorrectionAsync(string? schoolId, string? studentId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<LeavingCorrectionCheckDto>(400, TenantMessages.NoSchool, null);
            if (string.IsNullOrWhiteSpace(studentId)) return Result<LeavingCorrectionCheckDto>(400, "Enter a student id.", null);
            try
            {
                var r = await CorrectionCoreAsync(schoolId, studentId.Trim(), ct);
                return r == null ? Result<LeavingCorrectionCheckDto>(404, NotFound, null) : Result(200, "Successfully fetched", r.Value.Dto);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Leaving correction check failed for {StudentId}", studentId);
                return Result<LeavingCorrectionCheckDto>(500, Failed, null);
            }
        }

        public async Task<ApiResponse<LeavingCorrectionCheckDto>> CorrectAsync(string? schoolId, long userId, LeavingCorrectionRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<LeavingCorrectionCheckDto>(400, TenantMessages.NoSchool, null);
            if (request == null) return Result<LeavingCorrectionCheckDto>(400, "The request is empty.", null);
            var reason = request.Reason?.Trim();
            if (reason == null || reason.Length < LeavingRules.MinReason) return Result<LeavingCorrectionCheckDto>(400, $"Give a reason of at least {LeavingRules.MinReason} characters.", null);
            if (reason.Length > 500) return Result<LeavingCorrectionCheckDto>(400, "The reason can be at most 500 characters.", null);
            if (!request.Confirm) return Result<LeavingCorrectionCheckDto>(400, "Correcting a leaving changes the student's record: confirm first.", null);
            try
            {
                var r = await CorrectionCoreAsync(schoolId, request.StudentId.Trim(), ct);
                if (r == null) return Result<LeavingCorrectionCheckDto>(404, NotFound, null);
                var (dto, c) = r.Value;
                if (!dto.CanCorrect) return Result(409, dto.Blockers[0], dto);
                await using var tx = await _repo.BeginTransactionAsync(ct);
                var ok = await _repo.ApplyCorrectionAsync(schoolId, dto.StudentId, c.EnrollmentId, c.Outcome!, reason, userId, ct);
                if (!ok)
                {
                    await tx.RollbackAsync(ct);
                    return Result<LeavingCorrectionCheckDto>(409, "This student's record was changed by someone else. Reload and check again.", null);
                }
                await tx.CommitAsync(ct);
                return Result(200, $"The leaving of {c.StudentName} is corrected. They can sign in again.", dto);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Leaving correction failed for {StudentId}", request.StudentId);
                return Result<LeavingCorrectionCheckDto>(500, Failed, null);
            }
        }

        public async Task<ApiResponse<List<LeaverDto>>> ListLeaversAsync(string? schoolId, long? academicYearId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<LeaverDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var rows = await _repo.ListLeaversAsync(schoolId, academicYearId, ct);
                return Result(200, "Successfully fetched", rows.Select(r => new LeaverDto
                {
                    StudentId = r.StudentId, StudentName = r.StudentName, AdmissionNumber = r.AdmissionNumber, ClassName = r.ClassName, SectionName = r.SectionName, Outcome = r.Outcome,
                    LeavingDate = r.LeavingDate, Reason = r.Reason, Outstanding = r.Outstanding, Overridden = r.Overridden, RecordedBy = r.RecordedBy, YearName = r.YearName,
                }).ToList());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Leavers list failed");
                return Result<List<LeaverDto>>(500, Failed, null);
            }
        }
    }
}
