using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_db_layer.helper;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.helper;

namespace smss_api_service_layer.service
{
    /// <summary>
    /// Mid-year SECTION change (Stage 8c): same class, same academic year. The single enrolment is kept; its section and roll number are updated in place and a history row is written. Fees, transport and class are
    /// not touched, and attendance, marks and report rows entered under the old section stay under it. The enrolment must be live, the year open, the target section offered by the same year in the same class and
    /// within its capacity. A student with a PUBLISHED report card in that year is refused (the published card belongs to the old section's publication). One change at a time per school.
    /// </summary>
    public class SectionChangeService : ISectionChangeService
    {
        private const string NotFound = "Student not found.";
        private const string Failed = "The request could not be completed. Please try again.";
        private readonly ISectionChangeRepository _repo;
        private readonly ILogger<SectionChangeService> _logger;
        public SectionChangeService(ISectionChangeRepository repo, ILogger<SectionChangeService> logger) { _repo = repo; _logger = logger; }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) where T : class => new() { Status = code is >= 200 and < 300, StatusCode = code, Message = message, Data = data };

        /// <summary>The smallest number above every numeric roll already in the section (a leaver's roll is still taken).</summary>
        public static string NextRoll(IEnumerable<string?> rolls)
        {
            var max = 0;
            foreach (var r in rolls) { var k = RollKey.Of(r); if (k != null && int.TryParse(k, out var n) && n > max) max = n; }
            return (max + 1).ToString();
        }

        private static List<string> Blockers(SecContext c)
        {
            var b = new List<string>();
            if (!c.StudentActive) b.Add($"{c.StudentName} is not active.");
            if (!LiveEnrolment.IsLive(c.EnrolmentActive, c.Outcome, c.YearIsCurrent)) b.Add($"{c.StudentName} is not on the live roll (left, transferred out, or already moved on), so the section cannot be changed.");
            if (c.YearClosed) b.Add(ClosedYear.Message);
            if (c.PublishedCards > 0)
                b.Add($"{c.StudentName} already has a published report card in {c.YearName} (under Section {c.SectionName}). Moving the student now could leave that card without its section, so the change is refused: make the change before report cards are published.");
            return b;
        }

        public async Task<ApiResponse<SectionChangeCheckDto>> CheckAsync(string? schoolId, string? studentId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<SectionChangeCheckDto>(400, TenantMessages.NoSchool, null);
            if (string.IsNullOrWhiteSpace(studentId)) return Result<SectionChangeCheckDto>(400, "Enter a student id.", null);
            try
            {
                var c = await _repo.GetContextAsync(schoolId, studentId.Trim(), ct);
                if (c == null) return Result<SectionChangeCheckDto>(404, NotFound, null);
                var dto = new SectionChangeCheckDto
                {
                    StudentId = studentId.Trim(), StudentName = c.StudentName, AdmissionNumber = c.AdmissionNumber, ClassName = c.ClassName, YearName = c.YearName, CurrentSectionId = c.SectionId,
                    CurrentSectionName = c.SectionName, CurrentRoll = c.RollNumber, Blockers = Blockers(c),
                };
                foreach (var t in await _repo.ListTargetsAsync(schoolId, c.YearId, c.ClassId, c.SectionId, ct))
                    dto.Targets.Add(new SectionTargetDto
                    {
                        SectionId = t.SectionId, SectionName = t.SectionName, MaxStrength = t.MaxStrength, Enrolled = t.Enrolled, IsFull = t.MaxStrength != null && t.Enrolled >= t.MaxStrength,
                        NextRoll = NextRoll(await _repo.ListRollsAsync(schoolId, c.YearId, t.SectionId, ct)),
                    });
                if (dto.Targets.Count == 0) dto.Blockers.Add($"{c.ClassName} has no other section offered in {c.YearName}.");
                dto.CanChange = dto.Blockers.Count == 0;
                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "Section change check failed for {StudentId}", studentId); return Result<SectionChangeCheckDto>(500, Failed, null); }
        }

        public async Task<ApiResponse<SectionChangeResultDto>> ChangeAsync(string? schoolId, long userId, ChangeSectionRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<SectionChangeResultDto>(400, TenantMessages.NoSchool, null);
            if (request == null) return Result<SectionChangeResultDto>(400, "The request is empty.", null);
            var reason = request.Reason?.Trim();
            if (reason == null || reason.Length < LeavingRules.MinReason) return Result<SectionChangeResultDto>(400, $"Give a reason of at least {LeavingRules.MinReason} characters.", null);
            if (reason.Length > 500) return Result<SectionChangeResultDto>(400, "The reason can be at most 500 characters.", null);
            if (!request.Confirm) return Result<SectionChangeResultDto>(400, "Changing a section moves the student's class lists: confirm first.", null);
            var typedRoll = string.IsNullOrWhiteSpace(request.RollNumber) ? null : request.RollNumber.Trim();
            try
            {
                var studentId = request.StudentId.Trim();
                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockAsync(schoolId, ct);   // everything below is read and written under the lock
                var c = await _repo.GetContextAsync(schoolId, studentId, ct);
                if (c == null) return Result<SectionChangeResultDto>(404, NotFound, null);
                var blockers = Blockers(c);
                if (blockers.Count > 0) return Result<SectionChangeResultDto>(409, blockers[0], null);
                if (request.ToSectionId == c.SectionId) return Result<SectionChangeResultDto>(409, $"{c.StudentName} is already in Section {c.SectionName}.", null);
                var today = SchoolClock.Today();
                var date = request.EffectiveDate ?? today;
                var dateProblem = LeavingRules.DateProblem(date, today, c.EnrollmentDate);
                if (dateProblem != null) return Result<SectionChangeResultDto>(409, dateProblem, null);
                var target = (await _repo.ListTargetsAsync(schoolId, c.YearId, c.ClassId, c.SectionId, ct)).FirstOrDefault(t => t.SectionId == request.ToSectionId);
                if (target == null) return Result<SectionChangeResultDto>(409, $"That section is not offered in {c.YearName} for {c.ClassName}. Only another section of the same class and year can be chosen.", null);
                if (target.MaxStrength != null && target.Enrolled >= target.MaxStrength)
                    return Result<SectionChangeResultDto>(409, $"Section {target.SectionName} is full ({target.Enrolled} of {target.MaxStrength}).", null);
                var rolls = await _repo.ListRollsAsync(schoolId, c.YearId, target.SectionId, ct);
                string roll;
                if (typedRoll != null)
                {
                    if (rolls.Any(r => RollKey.Same(r, typedRoll))) return Result<SectionChangeResultDto>(409, $"Roll number {typedRoll} is already used in Section {target.SectionName}.", null);
                    roll = typedRoll;
                }
                else roll = NextRoll(rolls);
                var ok = await _repo.ApplyAsync(new SecChangeWrite(schoolId, studentId, c.EnrollmentId, c.YearId, c.SectionId, target.SectionId, c.RollNumber, roll, date, reason, userId), ct);
                if (!ok) { await tx.RollbackAsync(ct); return Result<SectionChangeResultDto>(409, "This student's enrolment was changed by someone else. Reload and check again.", null); }
                await tx.CommitAsync(ct);
                return Result(200, $"{c.StudentName} is moved from Section {c.SectionName} to Section {target.SectionName} (roll {roll}).",
                    new SectionChangeResultDto { StudentId = studentId, StudentName = c.StudentName, FromSection = c.SectionName, ToSection = target.SectionName, FromRoll = c.RollNumber, ToRoll = roll, EffectiveDate = date });
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "Section change failed for {StudentId}", request.StudentId); return Result<SectionChangeResultDto>(500, Failed, null); }
        }

        public async Task<ApiResponse<List<SectionChangeHistoryDto>>> HistoryAsync(string? schoolId, string? studentId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<SectionChangeHistoryDto>>(400, TenantMessages.NoSchool, null);
            if (string.IsNullOrWhiteSpace(studentId)) return Result<List<SectionChangeHistoryDto>>(400, "Enter a student id.", null);
            try
            {
                if (await _repo.GetContextAsync(schoolId, studentId.Trim(), ct) == null) return Result<List<SectionChangeHistoryDto>>(404, NotFound, null);
                var rows = await _repo.ListHistoryAsync(schoolId, studentId.Trim(), ct);
                return Result(200, "Successfully fetched", rows.Select(r => new SectionChangeHistoryDto
                {
                    ChangeId = r.ChangeId, YearName = r.YearName, FromSection = r.FromSection, ToSection = r.ToSection, FromRoll = r.FromRoll, ToRoll = r.ToRoll, EffectiveDate = r.EffectiveDate,
                    Reason = r.Reason, ChangedBy = r.ChangedBy, ChangedAt = r.ChangedAt,
                }).ToList());
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "Section change history failed for {StudentId}", studentId); return Result<List<SectionChangeHistoryDto>>(500, Failed, null); }
        }
    }
}
