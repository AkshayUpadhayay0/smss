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

        public PromotionService(IPromotionRepository repo, ILogger<PromotionService> logger) { _repo = repo; _logger = logger; }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) where T : class => new() { Status = code is >= 200 and < 300, StatusCode = code, Message = message, Data = data };
        private static bool SameName(string? a, string? b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        private sealed class OfferedSection { public long SectionId; public string Name = ""; public int? Max; public int Enrolled; }
        private sealed class OfferedClass { public long ClassId; public string Name = ""; public bool HasFees; public List<OfferedSection> Sections = new(); }

        public async Task<ApiResponse<PromotionPreviewDto>> PreviewAsync(string? schoolId, PromotionPreviewRequest request, CancellationToken ct)
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

                    if (targetEnrol.ContainsKey(c.StudentId)) { row.Status = PromotionRowStatus.AlreadyEnrolled; row.Notes.Add($"Already enrolled in {tgt.YearName}: skipped, nothing is changed."); continue; }
                    if (!string.IsNullOrEmpty(c.Outcome)) { row.Status = PromotionRowStatus.AlreadyRecorded; row.Notes.Add($"This enrolment already has the outcome {c.Outcome}: skipped."); continue; }

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
                    SourceYearId = src.YearId, SourceYearName = src.YearName, SourceYearClosed = src.IsClosed, TargetYearId = tgt.YearId, TargetYearName = tgt.YearName,
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
    }
}
