using Microsoft.Extensions.Logging;
using smss_api_db_layer.helper;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>
    /// Promotion of students into a later year, READ SIDE (4b-2): works out, for ONE class (all its sections) or ONE section, what a run would do, and writes nothing.
    /// The rules, per student: a decision (the class teacher's report-card decision prefills it; no decision = "no decision yet", left out of the run); the next class by next_class_id (or the class
    /// the teacher wrote when it names exactly one the target year offers) with the same-named section; capacity of the target section when it has a limit; the next free roll number; and the dues
    /// block (unpaid dues of the SOURCE year due on or before the promotion date; the same for promote, repeat, pass out and leave). A student who already has an enrolment in the target year is
    /// skipped, never changed. The source year may be closed; the target year may not, and must start after the source year.
    /// </summary>
    public class PromotionService : IPromotionService
    {
        public const int MaxPerRun = 500;
        private static readonly string[] Decisions = { "PROMOTE", "REPEAT", "PASS_OUT", "LEAVE" };
        private readonly IPromotionRepository _repo;
        private readonly ILogger<PromotionService> _logger;

        private readonly IStudentFeeBillingRepository _years;
        private readonly IFeeRepairRepository _choices;
        private readonly IAdmissionRepository _admission;
        private readonly IStudentFeeBillingService _billing;

        public PromotionService(IPromotionRepository repo, IStudentFeeBillingRepository years, IFeeRepairRepository choices, IAdmissionRepository admission, IStudentFeeBillingService billing, ILogger<PromotionService> logger)
        {
            _repo = repo; _years = years; _choices = choices; _admission = admission; _billing = billing; _logger = logger;
        }

        /// <summary>What the work-out found about the years and the class, for the run.</summary>
        private sealed class Ctx { public PromoYearRow Src = null!; public PromoYearRow Tgt = null!; }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) where T : class => new() { Status = code is >= 200 and < 300, StatusCode = code, Message = message, Data = data };
        private static bool SameName(string? a, string? b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        private sealed class OfferedSection { public long SectionId; public string Name = ""; public int? Max; public int Enrolled; }
        private sealed class OfferedClass { public long ClassId; public string Name = ""; public bool HasFees; public List<OfferedSection> Sections = new(); }

        public Task<ApiResponse<PromotionPreviewDto>> PreviewAsync(string? schoolId, PromotionPreviewRequest request, CancellationToken ct) => PrepareAsync(schoolId, request, new Ctx(), ct);

        /// <summary>The whole work-out, read-only: used as it is by the preview, and again by the run (under the lock) so the run trusts nothing from an earlier preview.</summary>
        private async Task<ApiResponse<PromotionPreviewDto>> PrepareAsync(string? schoolId, PromotionPreviewRequest request, Ctx ctx, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<PromotionPreviewDto>(400, TenantMessages.NoSchool, null);
            if (request == null) return Result<PromotionPreviewDto>(400, "The request is empty.", null);
            try
            {
                var src = await _repo.GetYearAsync(schoolId, request.SourceYearId, ct);
                if (src == null) return Result<PromotionPreviewDto>(404, "The source academic year was not found.", null);
                var tgt = await _repo.GetYearAsync(schoolId, request.TargetYearId, ct);
                if (tgt == null) return Result<PromotionPreviewDto>(404, "The target academic year was not found.", null);
                if (tgt.YearId == src.YearId || tgt.StartDate <= src.StartDate) return Result<PromotionPreviewDto>(400, "The target year must start after the source year.", null);
                if (tgt.IsClosed) return Result<PromotionPreviewDto>(400, $"{tgt.YearName} is closed: students cannot be promoted into it.", null);
                if (!tgt.IsActive) return Result<PromotionPreviewDto>(400, $"{tgt.YearName} is not active: students cannot be promoted into it.", null);

                var classes = await _repo.ListClassesAsync(schoolId, ct);
                var cls = classes.FirstOrDefault(c => c.ClassId == request.ClassId);
                if (cls == null) return Result<PromotionPreviewDto>(404, "The class was not found.", null);
                var sections = await _repo.ListSectionsAsync(schoolId, cls.ClassId, ct);
                var section = request.SectionId == null ? null : sections.FirstOrDefault(s => s.SectionId == request.SectionId);
                if (request.SectionId != null && section == null) return Result<PromotionPreviewDto>(404, "The section was not found in this class.", null);

                var candidates = await _repo.ListCandidatesAsync(schoolId, src.YearId, cls.ClassId, request.SectionId, ct);
                if (candidates.Count > MaxPerRun)
                    return Result<PromotionPreviewDto>(400, $"This {(section == null ? "class" : "section")} has {candidates.Count} students; a run takes up to {MaxPerRun}. Promote it one section at a time.", null);

                ctx.Src = src; ctx.Tgt = tgt;
                var promotionDate = request.PromotionDate ?? SchoolClock.Today();
                var ids = candidates.Select(c => c.StudentId).ToList();
                var targetEnrol = (await _repo.ListTargetEnrollmentsAsync(schoolId, tgt.YearId, ids, ct)).ToDictionary(e => e.StudentId, e => e);
                var teacher = (await _repo.ListTeacherDecisionsAsync(schoolId, src.YearId, ids, ct)).ToDictionary(t => t.StudentId, t => t);
                var duesByStudent = (await _repo.ListUnpaidDuesAsync(schoolId, src.YearId, ids, ct)).GroupBy(d => d.StudentId).ToDictionary(g => g.Key, g => g.ToList());

                // what the target year offers, with capacity and how full each section already is
                var offer = (await _repo.ListTargetOfferAsync(schoolId, tgt.YearId, ct)).GroupBy(o => o.ClassId).Select(g =>
                {
                    var first = g.First();
                    return new OfferedClass
                    {
                        ClassId = first.ClassId, Name = first.ClassName, HasFees = first.HasFeeStructure,
                        Sections = g.Where(x => x.SectionId != null).Select(x => new OfferedSection { SectionId = x.SectionId!.Value, Name = x.SectionName!, Max = x.MaxStrength, Enrolled = x.Enrolled }).OrderBy(x => x.Name).ToList(),
                    };
                }).ToList();
                var offerById = offer.ToDictionary(o => o.ClassId);
                var sectionById = offer.SelectMany(o => o.Sections.Select(s => (Class: o, Section: s))).ToDictionary(x => x.Section.SectionId);

                // roll numbers already taken in the target year, per section, and the highest plain number
                var rollKeys = new Dictionary<long, HashSet<string>>();
                var rollMax = new Dictionary<long, long>();
                foreach (var r in await _repo.ListTargetRollsAsync(schoolId, tgt.YearId, ct))
                {
                    var key = RollKey.Of(r.RollNumber);
                    if (key == null) continue;
                    if (!rollKeys.TryGetValue(r.SectionId, out var set)) rollKeys[r.SectionId] = set = new HashSet<string>();
                    set.Add(key);
                    if (long.TryParse(key, out var n) && n > rollMax.GetValueOrDefault(r.SectionId)) rollMax[r.SectionId] = n;
                }
                var placed = new Dictionary<long, int>();
                var inputs = (request.Decisions ?? new()).GroupBy(d => d.StudentId).ToDictionary(g => g.Key, g => g.Last());

                OfferedSection? DefaultSection(OfferedClass? oc, string sourceSectionName) =>
                    oc == null ? null : oc.Sections.FirstOrDefault(s => SameName(s.Name, sourceSectionName)) ?? (oc.Sections.Count == 1 ? oc.Sections[0] : null);
                PromotionTargetDto? Target(long classId, string className, OfferedSection? s) => new() { ClassId = classId, ClassName = className, SectionId = s?.SectionId, SectionName = s?.Name };
                string ClassName(long id) => classes.FirstOrDefault(c => c.ClassId == id)?.ClassName ?? "that class";

                var next = cls.NextClassId == null ? null : classes.FirstOrDefault(c => c.ClassId == cls.NextClassId);
                var rows = new List<PromotionRowDto>();
                foreach (var c in candidates)
                {
                    var row = new PromotionRowDto
                    {
                        StudentId = c.StudentId, AdmissionNumber = c.AdmissionNumber, StudentName = c.StudentName, SourceEnrollmentId = c.EnrollmentId,
                        SourceSectionId = c.SectionId, SourceSectionName = c.SectionName, SourceRollNumber = c.RollNumber,
                    };
                    rows.Add(row);

                    // dues: due on or before the promotion date = the block; later ones are information
                    if (duesByStudent.TryGetValue(c.StudentId, out var dues))
                    {
                        foreach (var d in dues)
                        {
                            var open = d.AmountDue - d.AmountPaid;
                            if (d.DueDate <= promotionDate)
                            {
                                row.Outstanding += open;
                                row.Dues.Add(new PromotionDueDto { FeeDueId = d.FeeDueId, FeeType = d.FeeTypeName, DueDate = d.DueDate, AmountDue = d.AmountDue, AmountPaid = d.AmountPaid, Outstanding = open });
                            }
                            else row.NotYetDue += open;
                        }
                    }
                    row.BlockedByDues = row.Outstanding > 0;


                    // defaults the screen can switch to without asking again
                    var nextOffered = next == null ? null : offerById.GetValueOrDefault(next.ClassId);
                    if (next != null) row.PromoteDefault = Target(next.ClassId, next.ClassName, DefaultSection(nextOffered, c.SectionName));
                    var sameOffered = offerById.GetValueOrDefault(cls.ClassId);
                    row.RepeatDefault = Target(cls.ClassId, cls.ClassName, DefaultSection(sameOffered, c.SectionName));

                    // the decision: the admin's edit, else the teacher's, else none
                    teacher.TryGetValue(c.StudentId, out var t);
                    row.TeacherDecision = t?.Decision;
                    inputs.TryGetValue(c.StudentId, out var input);
                    string? decision = null;
                    if (!string.IsNullOrWhiteSpace(input?.Decision))
                    {
                        row.DecisionSource = "ADMIN";
                        decision = input!.Decision!.Trim().ToUpperInvariant();
                        if (!Decisions.Contains(decision)) { row.Issues.Add($"\"{input.Decision}\" is not a decision. Choose Promote, Repeat, Pass out or Leave."); decision = null; }
                    }
                    else if (t != null)
                    {
                        row.DecisionSource = "TEACHER";
                        decision = t.Decision == "PROMOTED" ? "PROMOTE" : "REPEAT";   // DETAINED and NOT_PROMOTED both mean the same class again
                    }
                    row.Decision = decision;

                    // a student who is skipped keeps the decision they would have had (the audit records it) but nothing else is worked out
                    if (targetEnrol.ContainsKey(c.StudentId)) { row.Status = PromotionRowStatus.AlreadyEnrolled; row.Issues.Clear(); row.Notes.Add($"Already enrolled in {tgt.YearName}: skipped, nothing is changed."); continue; }
                    if (!string.IsNullOrEmpty(c.Outcome)) { row.Status = PromotionRowStatus.AlreadyRecorded; row.Issues.Clear(); row.Notes.Add($"This enrolment already has the outcome {c.Outcome}: skipped."); continue; }

                    if (decision is "PROMOTE" or "REPEAT")
                    {
                        long? toClassId; string? teacherNote = null;
                        if (decision == "REPEAT")
                        {
                            toClassId = cls.ClassId;
                            if (input?.ToClassId != null && input.ToClassId != cls.ClassId) row.Issues.Add("Repeat means the same class again; to move the student to another class choose Promote.");
                        }
                        else
                        {
                            toClassId = input?.ToClassId;
                            if (toClassId == null && row.DecisionSource == "TEACHER" && !string.IsNullOrWhiteSpace(t?.PromotedTo))
                            {
                                var wrote = t!.PromotedTo!.Trim();
                                var match = offer.FirstOrDefault(o => SameName(o.Name, wrote));
                                if (match != null)
                                {
                                    toClassId = match.ClassId;
                                    if (next == null) teacherNote = $"The teacher wrote \"{wrote}\"; this class has no next class, so \"{wrote}\" is used.";
                                    else if (match.ClassId != next.ClassId) teacherNote = $"The teacher wrote \"{wrote}\" but the next class is {next.ClassName}. \"{wrote}\" is used; change it here if that is wrong.";
                                }
                                else teacherNote = $"The teacher wrote \"{wrote}\", which is not a class {tgt.YearName} offers" + (next == null ? "." : $"; the next class, {next.ClassName}, is used.");
                            }
                            toClassId ??= cls.NextClassId;
                            if (teacherNote != null) row.Notes.Add(teacherNote);
                            if (toClassId == null) row.Issues.Add("This is the last class, so there is no next class: choose Pass out or Repeat, or pick the class to move to.");
                            else if (toClassId == cls.ClassId) row.Issues.Add("Promote needs a different class; use Repeat for the same class again.");
                            else if (!classes.Any(x => x.ClassId == toClassId)) row.Issues.Add("That class was not found in this school.");
                        }

                        if (row.Issues.Count == 0 && toClassId != null)
                        {
                            if (!offerById.TryGetValue(toClassId.Value, out var oc)) row.Issues.Add($"{ClassName(toClassId.Value)} is not offered in {tgt.YearName}. Offer it in the academic setup first.");
                            else
                            {
                                OfferedSection? sec = null;
                                if (input?.ToSectionId != null)
                                {
                                    sec = oc.Sections.FirstOrDefault(s => s.SectionId == input.ToSectionId);
                                    if (sec == null) row.Issues.Add($"That section is not offered for {oc.Name} in {tgt.YearName}.");
                                }
                                else
                                {
                                    sec = DefaultSection(oc, c.SectionName);
                                    if (sec == null) row.Issues.Add(oc.Sections.Count == 0 ? $"{oc.Name} has no section offered in {tgt.YearName}." : $"Choose a section of {oc.Name}.");
                                }
                                if (row.Issues.Count == 0 && sec != null)
                                {
                                    row.Target = Target(oc.ClassId, oc.Name, sec);
                                    var used = sec.Enrolled + placed.GetValueOrDefault(sec.SectionId);
                                    if (sec.Max != null && used >= sec.Max) row.Issues.Add($"{oc.Name} {sec.Name} is full ({sec.Max} students).");
                                    else
                                    {
                                        placed[sec.SectionId] = placed.GetValueOrDefault(sec.SectionId) + 1;
                                        if (!rollKeys.TryGetValue(sec.SectionId, out var keys)) rollKeys[sec.SectionId] = keys = new HashSet<string>();
                                        var typed = input?.RollNumber?.Trim();
                                        if (!string.IsNullOrEmpty(typed))
                                        {
                                            var key = RollKey.Of(typed)!;
                                            if (keys.Contains(key)) row.Issues.Add($"Roll number {typed} is already taken in {oc.Name} {sec.Name}.");
                                            else { keys.Add(key); row.RollNumber = typed; if (long.TryParse(key, out var n) && n > rollMax.GetValueOrDefault(sec.SectionId)) rollMax[sec.SectionId] = n; }
                                        }
                                        else
                                        {
                                            var n = rollMax.GetValueOrDefault(sec.SectionId) + 1;
                                            while (keys.Contains(n.ToString())) n++;
                                            rollMax[sec.SectionId] = n; keys.Add(n.ToString()); row.RollNumber = n.ToString();
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else if (decision is "PASS_OUT" or "LEAVE" && input?.ToClassId != null) row.Issues.Add("Pass out and Leave do not move the student to a class.");

                    row.Status = row.Issues.Count > 0 ? PromotionRowStatus.NeedsAttention
                        : decision == null ? PromotionRowStatus.NoDecision
                        : row.BlockedByDues ? PromotionRowStatus.Blocked
                        : PromotionRowStatus.Ready;
                    if (row.Status == PromotionRowStatus.NoDecision) row.Notes.Add("No decision yet: the student is left out of the run until you choose.");
                }

                var placedRows = rows.Where(r => r.Target != null && r.Status is PromotionRowStatus.Ready or PromotionRowStatus.Blocked).ToList();
                var noFees = placedRows.Select(r => r.Target!.ClassId).Distinct().Where(id => !offerById[id].HasFees).Select(id => offerById[id].Name).OrderBy(n => n).ToList();
                var dto = new PromotionPreviewDto
                {
                    SourceYearId = src.YearId, SourceYearName = src.YearName, SourceYearClosed = src.IsClosed, TargetYearId = tgt.YearId, TargetYearName = tgt.YearName, TargetYearStart = tgt.StartDate, TargetYearStarted = tgt.StartDate < SchoolClock.Today(),
                    Scope = section == null ? "CLASS" : "SECTION", ClassId = cls.ClassId, ClassName = cls.ClassName, SectionId = section?.SectionId, SectionName = section?.SectionName,
                    PromotionDate = promotionDate, NextClassId = next?.ClassId, NextClassName = next?.ClassName, IsLastClass = cls.NextClassId == null, MaxPerRun = MaxPerRun,
                    ClassesWithoutFeeStructure = noFees, NeedsFeeAcknowledgement = noFees.Count > 0,
                    TargetOptions = offer.Select(o => new PromotionClassOptionDto
                    {
                        ClassId = o.ClassId, ClassName = o.Name, HasFeeStructure = o.HasFees,
                        Sections = o.Sections.Select(s => new PromotionSectionOptionDto { SectionId = s.SectionId, SectionName = s.Name, MaxStrength = s.Max, Enrolled = s.Enrolled }).ToList(),
                    }).ToList(),
                    Rows = rows,
                    Counts = new PromotionCountsDto
                    {
                        Total = rows.Count,
                        Ready = rows.Count(r => r.Status == PromotionRowStatus.Ready), Blocked = rows.Count(r => r.Status == PromotionRowStatus.Blocked),
                        NeedsAttention = rows.Count(r => r.Status == PromotionRowStatus.NeedsAttention), NoDecision = rows.Count(r => r.Status == PromotionRowStatus.NoDecision),
                        AlreadyEnrolled = rows.Count(r => r.Status == PromotionRowStatus.AlreadyEnrolled), AlreadyRecorded = rows.Count(r => r.Status == PromotionRowStatus.AlreadyRecorded),
                    },
                };
                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Promotion preview failed (source {Source}, target {Target}, class {Class})", request.SourceYearId, request.TargetYearId, request.ClassId);
                return Result<PromotionPreviewDto>(500, "The promotion preview could not be prepared. Please try again.", null);
            }
        }

        // ================================================================== the run (4b-3)
        private static string? OutcomeOf(string decision) => decision switch { "PROMOTE" => "PROMOTED", "REPEAT" => "REPEATED", "PASS_OUT" => "PASSED_OUT", "LEAVE" => "LEFT", _ => null };

        private static string SkipReasonOf(PromotionRowDto r) => r.Status switch
        {
            PromotionRowStatus.Blocked => $"Unpaid dues of {r.Outstanding:0.##} up to the promotion date; not overridden.",
            PromotionRowStatus.NeedsAttention => r.Issues.FirstOrDefault() ?? "The choice cannot be carried out.",
            PromotionRowStatus.AlreadyEnrolled => "Already enrolled in the target year.",
            PromotionRowStatus.AlreadyRecorded => r.Notes.FirstOrDefault() ?? "The enrolment already has an outcome.",
            _ => "Left out of this run.",
        };

        public async Task<ApiResponse<PromotionResultDto>> ExecuteAsync(string? schoolId, long userId, PromotionExecuteRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<PromotionResultDto>(400, TenantMessages.NoSchool, null);
            if (request == null) return Result<PromotionResultDto>(400, "The request is empty.", null);
            if (!request.Confirm) return Result<PromotionResultDto>(400, "Promoting students saves changes: confirm the run first.", null);
            var reason = request.OverrideReason?.Trim();
            if (reason is { Length: > 500 }) return Result<PromotionResultDto>(400, "The reason can be at most 500 characters.", null);
            try
            {
                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockSchoolAsync(schoolId, ct);   // a second run waits here and then sees what the first one did

                // everything is worked out again now, under the lock
                var ctx = new Ctx();
                var prep = await PrepareAsync(schoolId, request, ctx, ct);
                if (!prep.Status || prep.Data == null) return Result<PromotionResultDto>(prep.StatusCode, prep.Message, null);
                var p = prep.Data;
                var tgt = ctx.Tgt;
                var today = SchoolClock.Today();

                var ticked = (request.OverrideStudentIds ?? new()).Select(s => s?.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToHashSet();
                var overridden = p.Rows.Where(r => r.Status == PromotionRowStatus.Blocked && ticked.Contains(r.StudentId)).ToList();
                if (overridden.Count > 0 && (reason == null || reason.Length < 10))
                    return Result<PromotionResultDto>(400, "A reason of at least 10 characters is needed to override the dues block.", null);

                var doRows = p.Rows.Where(r => r.Decision != null && (r.Status == PromotionRowStatus.Ready || overridden.Contains(r))).ToList();
                var placing = doRows.Where(r => r.Decision is "PROMOTE" or "REPEAT").ToList();

                // the target year has already started: charging fees from its start needs the same confirmation as "Assign fees" (current year only, explicit, with a reason)
                var backdate = placing.Count > 0 && tgt.StartDate < today;
                if (backdate)
                {
                    if (reason == null || reason.Length < 10) return Result<PromotionResultDto>(400, $"{tgt.YearName} has already started, so fees are charged from its first day: a reason of at least 10 characters is needed.", null);
                    var problem = await _billing.CheckChargeFromAsync(schoolId, tgt.YearId, tgt.StartDate, request.ConfirmBackdating, reason, ct);
                    if (problem != null) return Result<PromotionResultDto>(400, problem, null);
                }

                // a target class with no fee structure: one acknowledgement for the run
                var noFees = placing.Select(r => r.Target!.ClassId).Distinct().Where(id => !p.TargetOptions.Single(o => o.ClassId == id).HasFeeStructure).ToList();
                if (noFees.Count > 0 && !request.AcknowledgeNoFeeStructure)
                    return Result<PromotionResultDto>(400, "A class you are promoting into has no fee structure in " + tgt.YearName + ". Tick the acknowledgement to continue without fees for them.", null);

                var result = new PromotionResultDto { Backdated = backdate };
                var items = new List<(PromotionRowDto Row, PromoItemWrite Item, string Outcome)>();
                var enrollmentDate = p.PromotionDate > tgt.StartDate ? p.PromotionDate : tgt.StartDate;
                var n = 0;
                foreach (var row in p.Rows)
                {
                    if (row.Decision == null) { result.LeftOut++; result.Items.Add(new PromotionResultItemDto { StudentId = row.StudentId, StudentName = row.StudentName, Outcome = PromotionItemOutcome.Skipped, Reason = row.Notes.FirstOrDefault() ?? "No decision yet.", Outstanding = row.Outstanding }); continue; }
                    var item = new PromoItemWrite
                    {
                        StudentId = row.StudentId, SourceEnrollmentId = row.SourceEnrollmentId, Decision = row.Decision, FromClassId = p.ClassId, FromSectionId = row.SourceSectionId, OutstandingAmount = row.Outstanding,
                    };
                    var isOverride = overridden.Contains(row);
                    string? skip = null;
                    if (!doRows.Contains(row)) skip = SkipReasonOf(row);
                    else
                    {
                        await tx.CreateSavepointAsync("p" + (++n), ct);
                        try
                        {
                            if (row.Decision is "PROMOTE" or "REPEAT")
                            {
                                item.TargetEnrollmentId = await _repo.InsertEnrollmentAsync(schoolId, row.StudentId, tgt.YearId, row.Target!.SectionId!.Value, row.RollNumber, enrollmentDate, row.SourceEnrollmentId, ct);
                                item.ToClassId = row.Target.ClassId; item.ToSectionId = row.Target.SectionId; item.RollNumber = row.RollNumber;
                            }
                            if (!await _repo.SetOutcomeAsync(schoolId, row.SourceEnrollmentId, OutcomeOf(row.Decision)!, ct)) throw new InvalidOperationException("outcome already recorded");
                            await tx.ReleaseSavepointAsync("p" + n, ct);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogWarning(ex, "Promotion of {StudentId} could not be saved; the rest of the run continues", row.StudentId);
                            await tx.RollbackToSavepointAsync("p" + n, ct);
                            item.TargetEnrollmentId = null; item.ToClassId = null; item.ToSectionId = null; item.RollNumber = null;
                            skip = "This student could not be saved; nothing was changed for them.";
                        }
                    }
                    if (skip != null) item.SkipReason = skip;
                    else if (isOverride) { item.OverrideApplied = true; item.OverrideReason = reason; }
                    items.Add((row, item, skip != null ? PromotionItemOutcome.Skipped : isOverride ? PromotionItemOutcome.Overridden : PromotionItemOutcome.Done));
                }

                var done = items.Where(i => i.Outcome != PromotionItemOutcome.Skipped).ToList();
                foreach (var (row, item, outcome) in items)
                    result.Items.Add(new PromotionResultItemDto
                    {
                        StudentId = row.StudentId, StudentName = row.StudentName, Decision = row.Decision, Outcome = outcome, Reason = item.SkipReason ?? (outcome == PromotionItemOutcome.Overridden ? reason : null),
                        Target = item.TargetEnrollmentId != null ? row.Target : null, RollNumber = item.RollNumber, Outstanding = row.Outstanding,
                    });
                result.Skipped = items.Count - done.Count;
                if (done.Count == 0)
                {
                    await tx.CommitAsync(ct);   // nothing was written: no batch either
                    return Result(200, "Nobody could be promoted in this run; nothing was changed.", result);
                }

                result.Promoted = done.Count(i => i.Item.Decision == "PROMOTE"); result.Repeated = done.Count(i => i.Item.Decision == "REPEAT");
                result.PassedOut = done.Count(i => i.Item.Decision == "PASS_OUT"); result.Left = done.Count(i => i.Item.Decision == "LEAVE");
                result.Overridden = done.Count(i => i.Outcome == PromotionItemOutcome.Overridden);
                var batchId = await _repo.InsertBatchAsync(new PromoBatchWrite
                {
                    SchoolId = schoolId, SourceYearId = ctx.Src.YearId, TargetYearId = tgt.YearId, Scope = p.Scope, ClassId = p.ClassId, SectionId = p.SectionId, PromotionDate = p.PromotionDate, CreatedBy = userId,
                    StudentsTotal = items.Count, Promoted = result.Promoted, Repeated = result.Repeated, PassedOut = result.PassedOut, Left = result.Left, Skipped = result.Skipped, Overrides = result.Overridden,
                    OverrideReason = reason is { Length: >= 10 } ? reason : null, NoFeeStructureAcknowledged = noFees.Count > 0 && request.AcknowledgeNoFeeStructure,
                }, ct);
                foreach (var (_, item, _) in items) await _repo.InsertItemAsync(schoolId, batchId, item, ct);
                await tx.CommitAsync(ct);
                result.BatchId = batchId;

                // Only now that the promotion is safely saved: assigning the new year's fees (the same code as "Assign fees for a year"). A failure never undoes or fails the promotion.
                await AssignFeesAsync(schoolId, userId, tgt, placing.Where(r => done.Any(d => d.Row == r)).ToList(), backdate, reason, result, ct);
                return Result(200, $"{done.Count} student{(done.Count == 1 ? "" : "s")} done" + (result.Skipped > 0 ? $", {result.Skipped} left out." : "."), result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Promotion run failed (source {Source}, target {Target}, class {Class})", request.SourceYearId, request.TargetYearId, request.ClassId);
                return Result<PromotionResultDto>(500, "The promotion could not be completed and nothing was changed. Please try again.", null);
            }
        }

        private async Task AssignFeesAsync(string schoolId, long userId, PromoYearRow tgt, List<PromotionRowDto> placed, bool backdate, string? reason, PromotionResultDto result, CancellationToken ct)
        {
            if (placed.Count == 0) return;
            try
            {
                var choices = (await _choices.GetPreviousChoicesAsync(schoolId, tgt.YearId, placed.Select(r => r.StudentId).ToList(), ct))
                    .GroupBy(c => c.StudentId).ToDictionary(g => g.Key, g => g.Select(c => new FeeItemSelection { FeeTypeId = c.FeeTypeId, InstallmentPlan = c.InstallmentPlan, IsApplicable = c.IsApplicable }).ToList());
                foreach (var r in placed)
                {
                    try
                    {
                        var structure = await _years.FindActiveStructureAsync(schoolId, r.Target!.ClassId, tgt.YearId, ct);
                        if (structure == null) { result.FeesNoStructure++; continue; }
                        var options = backdate ? new FeeSyncOptions { ChargeFrom = tgt.StartDate, Backdate = true, BackdatingReason = reason, UserId = userId } : null;
                        var warning = await FeeAutoAssignment.TryAssignAsync(_admission, _billing, choices.GetValueOrDefault(r.StudentId), _logger, schoolId, r.StudentId, tgt.YearId, structure.FeeStructureId, DateTime.UtcNow, ct, options);
                        if (warning == null) result.FeesAssigned++;
                        else result.FeeFailures.Add(new PromotionFeeFailureDto { StudentId = r.StudentId, StudentName = r.StudentName, Reason = warning });
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Fees for promoted student {StudentId} could not be assigned", r.StudentId);
                        _admission.ClearTracking();
                        result.FeeFailures.Add(new PromotionFeeFailureDto { StudentId = r.StudentId, StudentName = r.StudentName, Reason = FeeAutoAssignment.AssignFailedWarning });
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Fees for the promoted students could not be assigned");
                foreach (var r in placed.Where(r => result.FeeFailures.All(f => f.StudentId != r.StudentId)).ToList())
                    result.FeeFailures.Add(new PromotionFeeFailureDto { StudentId = r.StudentId, StudentName = r.StudentName, Reason = FeeAutoAssignment.AssignFailedWarning });
            }
        }

        // ================================================================== pending promotion
        public const int PendingListLimit = 1000;

        public async Task<ApiResponse<PromotionPendingDto>> GetPendingAsync(string? schoolId, bool includeRows, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<PromotionPendingDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var years = await _repo.GetPendingYearsAsync(schoolId, ct);
                var dto = new PromotionPendingDto { HasPair = years?.SourceYearId != null && years.TargetYearId != null, SourceYearId = years?.SourceYearId, SourceYearName = years?.SourceYearName, TargetYearId = years?.TargetYearId, TargetYearName = years?.TargetYearName, TargetIsCurrent = years?.TargetIsCurrent ?? false };
                if (!dto.HasPair) return Result(200, "Successfully fetched", dto);
                var today = SchoolClock.Today();
                dto.Total = await _repo.CountPendingAsync(schoolId, today, ct);
                if (includeRows)
                {
                    dto.Rows = (await _repo.ListPendingAsync(schoolId, today, PendingListLimit, ct)).Select(r => new PromotionPendingRowDto
                    {
                        StudentId = r.StudentId, AdmissionNumber = r.AdmissionNumber, StudentName = r.StudentName, ClassId = r.ClassId, ClassName = r.ClassName, SectionId = r.SectionId, SectionName = r.SectionName,
                        RollNumber = r.RollNumber, TeacherDecision = r.TeacherDecision, Outstanding = r.Outstanding, Status = r.Outstanding > 0 ? "BLOCKED" : "NO_DECISION",
                    }).ToList();
                    dto.Blocked = dto.Rows.Count(r => r.Status == "BLOCKED");
                }
                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pending promotion could not be read");
                return Result<PromotionPendingDto>(500, "The students waiting for promotion could not be read. Please try again.", null);
            }
        }

        // ================================================================== the correction of one promotion (4b-4)
        /// <summary>
        /// Works out whether the promotion of ONE student out of a year can be corrected, and what it would remove. Refused (with the plain reasons) when the new-year enrolment has attendance, payment allocations
        /// (or any paid due), marks or report-card entries, concessions or transport; or when the year is closed. A concession is REFUSED, never silently removed: it is something a person decided for the new
        /// year, so the admin removes it on the student's Fees tab first.
        /// </summary>
        private async Task<(ApiResponse<PromotionCorrectionCheckDto>? Error, PromoCorrectionContext? Ctx, PromotionCorrectionCheckDto? Check)> CheckCoreAsync(string schoolId, string studentId, long sourceYearId, CancellationToken ct)
        {
            var c = await _repo.GetCorrectionContextAsync(schoolId, studentId, sourceYearId, ct);
            if (c == null) return (Result<PromotionCorrectionCheckDto>(404, "That student has no enrolment in that year.", null), null, null);
            var dto = new PromotionCorrectionCheckDto { StudentId = studentId, StudentName = c.StudentName, SourceYearName = c.SourceYearName, Decision = c.Decision, Outcome = c.Outcome };
            if (c.ItemId == null || string.IsNullOrEmpty(c.Outcome) || c.Outcome is "TRANSFERRED_OUT")
            {
                dto.Blockers.Add(string.IsNullOrEmpty(c.Outcome) ? $"{c.StudentName} has no promotion outcome in {c.SourceYearName}: there is nothing to correct."
                                                                 : $"The outcome {c.Outcome} of {c.StudentName} in {c.SourceYearName} was not recorded by a promotion run, so it cannot be corrected here.");
                return (null, c, dto);
            }
            if (c.TargetEnrollmentId != null)
            {
                dto.WillRemove = new PromotionCorrectionRemovalDto { Enrolment = true, NewYearName = c.TargetYearName, ClassName = c.TargetClassName, SectionName = c.TargetSectionName, RollNumber = c.TargetRoll };
                var year = c.TargetYearId!.Value;
                if (c.TargetYearClosed) dto.Blockers.Add($"{c.TargetYearName} is closed: its enrolments cannot be removed.");
                var b = await _repo.GetCorrectionBlockersAsync(schoolId, studentId, year, ct);
                if (b.Attendance > 0) dto.Blockers.Add($"{b.Attendance} attendance record{(b.Attendance == 1 ? " exists" : "s exist")} in {c.TargetYearName}.");
                if (b.PaymentAllocations > 0 || b.PaidDues > 0) dto.Blockers.Add($"A payment has been recorded against the {c.TargetYearName} fees.");
                if (b.Marks > 0) dto.Blockers.Add($"Marks or report-card entries exist in {c.TargetYearName} ({b.Marks}).");
                if (b.Transport > 0) dto.Blockers.Add($"The student is assigned to transport in {c.TargetYearName}: remove that first.");
                if (b.Concessions > 0)
                {
                    var names = await _repo.ListConcessionNamesAsync(schoolId, studentId, year, ct);
                    dto.Blockers.Add($"A concession was given for {c.TargetYearName} ({string.Join(", ", names)}). Remove it on the student's Fees tab first; it is never removed automatically.");
                }
                var fees = await _repo.CountFeesAsync(schoolId, studentId, year, ct);
                dto.WillRemove.FeeItems = fees.FeeItems; dto.WillRemove.Dues = fees.Dues; dto.WillRemove.DuesAmount = fees.DuesAmount; dto.WillRemove.FeeStructure = fees.FeeStructure;
            }
            dto.CanCorrect = dto.Blockers.Count == 0;
            return (null, c, dto);
        }

        public async Task<ApiResponse<PromotionCorrectionCheckDto>> CheckCorrectionAsync(string? schoolId, string? studentId, long sourceYearId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<PromotionCorrectionCheckDto>(400, TenantMessages.NoSchool, null);
            if (string.IsNullOrWhiteSpace(studentId)) return Result<PromotionCorrectionCheckDto>(400, "Enter a student id.", null);
            try
            {
                var (error, _, check) = await CheckCoreAsync(schoolId, studentId.Trim(), sourceYearId, ct);
                return error ?? Result(200, "Successfully fetched", check);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Promotion correction check failed for {StudentId}", studentId);
                return Result<PromotionCorrectionCheckDto>(500, "The correction could not be checked. Please try again.", null);
            }
        }

        public async Task<ApiResponse<PromotionCorrectionResultDto>> CorrectAsync(string? schoolId, long userId, PromotionCorrectionRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<PromotionCorrectionResultDto>(400, TenantMessages.NoSchool, null);
            if (request == null || string.IsNullOrWhiteSpace(request.StudentId)) return Result<PromotionCorrectionResultDto>(400, "Enter a student id.", null);
            if (!request.Confirm) return Result<PromotionCorrectionResultDto>(400, "Correcting a promotion removes the new-year enrolment: confirm it first.", null);
            var reason = request.Reason?.Trim();
            if (reason == null || reason.Length < 10) return Result<PromotionCorrectionResultDto>(400, "A reason of at least 10 characters is needed.", null);
            if (reason.Length > 500) return Result<PromotionCorrectionResultDto>(400, "The reason can be at most 500 characters.", null);
            try
            {
                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockSchoolAsync(schoolId, ct);   // the same lock as a promotion run: a correction never runs beside one

                var studentId = request.StudentId.Trim();
                var (error, c, check) = await CheckCoreAsync(schoolId, studentId, request.SourceYearId, ct);   // worked out again, under the lock
                if (error != null) return Result<PromotionCorrectionResultDto>(error.StatusCode, error.Message, null);
                if (!check!.CanCorrect) return Result<PromotionCorrectionResultDto>(409, "This promotion cannot be corrected: " + string.Join(" ", check.Blockers), null);

                var removed = new PromotionCorrectionRemovalDto { NewYearName = c!.TargetYearName, ClassName = c.TargetClassName, SectionName = c.TargetSectionName, RollNumber = c.TargetRoll };
                long? removedEnrollmentId = null;
                if (c.TargetEnrollmentId != null)
                {
                    var fees = await _repo.RemoveFeesAsync(schoolId, studentId, c.TargetYearId!.Value, ct);
                    removed.FeeItems = fees.FeeItems; removed.Dues = fees.Dues; removed.DuesAmount = fees.DuesAmount; removed.FeeStructure = fees.FeeStructure;
                    await _repo.ReverseItemAsync(schoolId, c.ItemId!.Value, ct);                  // detach the audit item before the enrolment goes
                    if (!await _repo.DeleteEnrollmentAsync(schoolId, c.TargetEnrollmentId.Value, ct)) throw new InvalidOperationException("the new enrolment vanished");
                    removed.Enrolment = true; removedEnrollmentId = c.TargetEnrollmentId;
                }
                else await _repo.ReverseItemAsync(schoolId, c.ItemId!.Value, ct);
                if (!await _repo.ClearOutcomeAsync(schoolId, c.SourceEnrollmentId, c.Outcome!, ct)) throw new InvalidOperationException("the outcome changed");

                var snapshot = System.Text.Json.JsonSerializer.Serialize(new
                {
                    enrolment = removed.Enrolment, enrollmentId = removedEnrollmentId, newYear = removed.NewYearName, className = removed.ClassName, sectionName = removed.SectionName, rollNumber = removed.RollNumber,
                    enrollmentDate = c.TargetEnrollmentDate, feeItems = removed.FeeItems, dues = removed.Dues, duesAmount = removed.DuesAmount, feeStructureAssignment = removed.FeeStructure,
                }, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                var id = await _repo.InsertCorrectionAsync(new PromoCorrectionWrite
                {
                    SchoolId = schoolId, StudentId = studentId, BatchId = c.BatchId!.Value, ItemId = c.ItemId!.Value, SourceEnrollmentId = c.SourceEnrollmentId, RemovedEnrollmentId = removedEnrollmentId,
                    Decision = c.Decision!, OutcomeCleared = c.Outcome!, RemovedJson = snapshot, Reason = reason, CorrectedBy = userId,
                }, ct);
                await tx.CommitAsync(ct);
                return Result(200, $"The promotion of {c.StudentName} is corrected. They can be promoted again.", new PromotionCorrectionResultDto { CorrectionId = id, StudentId = studentId, OutcomeCleared = c.Outcome!, Removed = removed });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Promotion correction failed for {StudentId}", request.StudentId);
                return Result<PromotionCorrectionResultDto>(500, "The correction could not be completed and nothing was changed. Please try again.", null);
            }
        }
    }
}
