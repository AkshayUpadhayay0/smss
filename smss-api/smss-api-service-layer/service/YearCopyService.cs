using System.Text.Json;
using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>
    /// S7: copy configuration (offerings, subjects, schedule assignments, class teachers, timetables) from a source year into a target year of the SAME school.
    /// One transaction under the per-school academic-setup lock. A preview runs the very same code and then ROLLS BACK, so it can't disagree with a real run.
    /// Merge rules: skip what the target already has, never overwrite or delete, never create a master, never touch the source or any history. A closed target is refused.
    /// </summary>
    public class YearCopyService : IYearCopyService
    {
        public static readonly string[] Order = { YearCopyRepository.PartClassesSections, YearCopyRepository.PartSubjects, YearCopyRepository.PartSchedules, YearCopyRepository.PartClassTeachers, YearCopyRepository.PartTimetable };
        private static readonly Dictionary<string, string> Labels = new()
        {
            [YearCopyRepository.PartClassesSections] = "Classes and sections (with capacity)",
            [YearCopyRepository.PartSubjects] = "Subjects and section exceptions",
            [YearCopyRepository.PartSchedules] = "Bell schedule assignments",
            [YearCopyRepository.PartClassTeachers] = "Class teachers",
            [YearCopyRepository.PartTimetable] = "Timetables (as drafts)",
        };
        private const int MaxExamples = 200;
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly IYearCopyRepository _repo;
        private readonly ILogger<YearCopyService> _logger;

        public YearCopyService(IYearCopyRepository repo, ILogger<YearCopyService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public Task<ApiResponse<YearCopyResultDto>> PreviewAsync(string? schoolId, long targetYearId, long sourceYearId, YearCopyRequest request, CancellationToken ct) =>
            RunAsync(schoolId, null, targetYearId, sourceYearId, request, preview: true, ct);

        public Task<ApiResponse<YearCopyResultDto>> ApplyAsync(string? schoolId, long userId, long targetYearId, long sourceYearId, YearCopyRequest request, CancellationToken ct) =>
            RunAsync(schoolId, userId, targetYearId, sourceYearId, request, preview: false, ct);

        private async Task<ApiResponse<YearCopyResultDto>> RunAsync(string? schoolId, long? userId, long targetYearId, long sourceYearId, YearCopyRequest req, bool preview, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<YearCopyResultDto>(400, TenantMessages.NoSchool, null);
            var parts = new List<string>();
            try
            {
                var wanted = (req.Parts ?? new()).Select(p => (p ?? "").Trim().ToUpperInvariant()).Distinct().ToList();
                if (wanted.Count == 0) return Result<YearCopyResultDto>(400, "Choose at least one part to copy.", null);
                var unknown = wanted.Where(p => !Order.Contains(p)).ToList();
                if (unknown.Count > 0) return Result<YearCopyResultDto>(400, $"Unknown part: {string.Join(", ", unknown)}.", null);
                parts = Order.Where(wanted.Contains).ToList();   // always run in dependency order, whatever order was asked for
                if (!preview && !req.Confirm) return Result<YearCopyResultDto>(400, "Confirm the copy to run it (confirm = true). A preview needs no confirm.", null);
                if (sourceYearId == targetYearId) return Result<YearCopyResultDto>(400, "The source and the target must be different academic years.", null);

                var source = await _repo.GetYearAsync(schoolId, sourceYearId, ct);
                var target = await _repo.GetYearAsync(schoolId, targetYearId, ct);
                if (source == null || target == null) return Result<YearCopyResultDto>(404, "Academic year not found", null);
                var refusal = Refusal(target);
                if (refusal != null) return refusal;

                var started = DateTime.UtcNow;
                var outcomes = new List<(string Part, CopyPartOutcome Outcome)>();
                await using (var tx = await _repo.BeginTransactionAsync(ct))
                {
                    await _repo.LockSchoolAsync(schoolId, ct);
                    target = await _repo.GetYearAsync(schoolId, targetYearId, ct);   // may have been closed while we waited for the lock
                    if (target == null) return Result<YearCopyResultDto>(404, "Academic year not found", null);
                    refusal = Refusal(target);
                    if (refusal != null) return refusal;

                    foreach (var part in parts) outcomes.Add((part, await _repo.RunPartAsync(part, schoolId, source, target, userId, ct)));
                    var dto = Build(source, target, preview, outcomes);

                    if (preview)
                    {
                        await tx.RollbackAsync(ct);   // the same code ran; nothing is kept
                        return Result(200, "Preview only. Nothing was copied.", dto);
                    }
                    var log = NewLog(schoolId, source, target, parts, dto, TbYearCopyLog.Completed, null, userId, started);
                    await _repo.AddLogAsync(log, ct);
                    await _repo.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    dto.CopyId = log.CopyId;
                    var total = dto.Parts.Sum(p => p.Created);
                    return Result(200, total == 0 ? "Nothing new to copy: the target year already has everything selected." : $"Copied {total} row{(total == 1 ? "" : "s")} into {target.YearName}.", dto);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while copying academic year configuration");
                if (!preview) await TryLogFailureAsync(schoolId, userId, sourceYearId, targetYearId, parts, ex, ct);
                return Result<YearCopyResultDto>(500, "Something went wrong, so nothing was copied. Please try again later.", null);
            }
        }

        public async Task<ApiResponse<List<YearCopyLogDto>>> GetLogAsync(string? schoolId, long targetYearId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<YearCopyLogDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                if (await _repo.GetYearAsync(schoolId, targetYearId, ct) == null) return Result<List<YearCopyLogDto>>(404, "Academic year not found", null);
                var rows = await _repo.GetLogAsync(schoolId, targetYearId, 10, ct);
                return Result(200, "Successfully fetched", rows.Select(r => new YearCopyLogDto
                {
                    CopyId = r.Log.CopyId, SourceYearId = r.Log.SourceYearId, SourceYearName = r.SourceYearName, TargetYearId = r.Log.TargetYearId, Status = r.Log.Status, ErrorNote = r.Log.ErrorNote,
                    RunByName = r.RunByName, StartedAt = r.Log.StartedAt, FinishedAt = r.Log.FinishedAt,
                    Parts = Parse<List<string>>(r.Log.Parts) ?? new(), Result = Parse<List<YearCopyPartDto>>(r.Log.Result) ?? new()
                }).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while reading the year copy log");
                return Result<List<YearCopyLogDto>>(500, "Something went wrong. Please try again later.", null);
            }
        }

        // ---------------------------------------------------------------- helpers
        private static ApiResponse<YearCopyResultDto>? Refusal(CopyYear target) =>
            target.IsClosed ? Result<YearCopyResultDto>(409, ClosedYear.Message, null)
            : !target.IsActiveStatus ? Result<YearCopyResultDto>(409, $"The target year {target.YearName} is inactive. Activate it first.", null) : null;

        private static YearCopyResultDto Build(CopyYear source, CopyYear target, bool preview, List<(string Part, CopyPartOutcome Outcome)> outcomes) => new()
        {
            SourceYearId = source.AcademicYearId, SourceYearName = source.YearName, TargetYearId = target.AcademicYearId, TargetYearName = target.YearName, Preview = preview,
            Parts = outcomes.Select(x => new YearCopyPartDto
            {
                Part = x.Part, Label = Labels[x.Part],
                Created = x.Outcome.Created.Values.Sum(),
                Details = x.Outcome.Created.Select(c => new YearCopyCountDto { Kind = c.Key, Count = c.Value }).ToList(),
                SkippedExisting = x.Outcome.SkippedExisting,
                SkippedWithReason = x.Outcome.Skipped.Count,
                Skipped = x.Outcome.Skipped.GroupBy(s => s.Reason).Select(g => new YearCopySkipDto { Reason = g.Key, Count = g.Count(), Examples = g.Select(s => s.Ref).Take(MaxExamples).ToList() }).ToList(),
                Notes = x.Outcome.Notes.Select(n => new YearCopyCountDto { Kind = n.Key, Count = n.Value }).ToList()
            }).ToList()
        };

        private static TbYearCopyLog NewLog(string schoolId, CopyYear source, CopyYear target, List<string> parts, YearCopyResultDto dto, string status, string? error, long? userId, DateTime started)
        {
            // the log keeps counts and a few examples, not every row
            var slim = dto.Parts.Select(p => new YearCopyPartDto
            {
                Part = p.Part, Label = p.Label, Created = p.Created, Details = p.Details, SkippedExisting = p.SkippedExisting, SkippedWithReason = p.SkippedWithReason, Notes = p.Notes,
                Skipped = p.Skipped.Select(s => new YearCopySkipDto { Reason = s.Reason, Count = s.Count, Examples = s.Examples.Take(5).ToList() }).ToList()
            }).ToList();
            return new TbYearCopyLog
            {
                SchoolId = schoolId, SourceYearId = source.AcademicYearId, TargetYearId = target.AcademicYearId, Parts = JsonSerializer.Serialize(parts, Json), Result = JsonSerializer.Serialize(slim, Json),
                Status = status, ErrorNote = error, RunBy = userId, StartedAt = started, FinishedAt = DateTime.UtcNow
            };
        }

        /// <summary>The run rolled back, so nothing was copied: a FAILED row (no data) is written in its own small transaction.</summary>
        private async Task TryLogFailureAsync(string schoolId, long? userId, long sourceYearId, long targetYearId, List<string> parts, Exception ex, CancellationToken ct)
        {
            try
            {
                _repo.ClearTracker();
                var source = await _repo.GetYearAsync(schoolId, sourceYearId, ct);
                var target = await _repo.GetYearAsync(schoolId, targetYearId, ct);
                if (source == null || target == null || sourceYearId == targetYearId) return;
                var note = ex.Message.Length > 240 ? ex.Message[..240] : ex.Message;
                await _repo.AddLogAsync(NewLog(schoolId, source, target, parts, new YearCopyResultDto(), TbYearCopyLog.Failed, note, userId, DateTime.UtcNow), ct);
                await _repo.SaveChangesAsync(ct);
            }
            catch (Exception inner) { _logger.LogError(inner, "Could not write the FAILED year copy log row"); }
        }

        private static T? Parse<T>(string json) { try { return JsonSerializer.Deserialize<T>(json, Json); } catch { return default; } }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) where T : class => new() { Status = code is >= 200 and < 300, StatusCode = code, Message = message, Data = data };
    }
}
