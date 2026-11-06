using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>
    /// The class teacher's side of the report card: completeness of the section's marks, activity grades, remarks and attendance, promotion, and the lock.
    /// Who: the section's class teacher edits (until LOCKED), the School Admin edits too (until LOCKED) and is the only one who locks and unlocks, the Principal only reads.
    /// A LOCKED term-section blocks every input for that term, for everyone.
    /// </summary>
    public class ReportClassService : IReportClassService
    {
        private const string LockedMessage = "This term is locked for the section. A School Admin has to unlock it before anything can change.";
        private readonly IReportMarksRepository _repo;
        private readonly IAttendanceRepository _attendance;
        private readonly ReportAccess _access;
        private readonly ILogger<ReportClassService> _logger;
        public ReportClassService(IReportMarksRepository repo, IAttendanceRepository attendance, ReportAccess access, ILogger<ReportClassService> logger) { _repo = repo; _attendance = attendance; _access = access; _logger = logger; }

        // ============================================================================ shared loading
        private sealed class Ctx
        {
            public int Active;
            public SchemeHeader Scheme = null!;
            public TermHeader? Term;
            public SectionInfo Section = null!;
            public ReportRights Rights = null!;
            public TbAcademicYears Year = null!;
            public bool CanEdit => Rights.IsAdmin || Rights.IsClassTeacher;
        }

        private async Task<(Ctx? Ctx, ApiResponse<T>? Failure)> LoadAsync<T>(string schoolId, ReportCaller caller, long? termId, long? schemeId, long sectionId, CancellationToken ct)
        {
            var active = await _repo.ActiveStatusIdAsync(ct);
            if (active == null) return (null, Result<T>(500, "The system is not configured correctly. Please contact support.", default));
            TermHeader? term = null; SchemeHeader? scheme;
            if (termId != null)
            {
                term = await _repo.GetTermAsync(schoolId, termId.Value, ct);
                if (term == null || term.SchemeStatusId != active.Value) return (null, Result<T>(404, "Term not found.", default));
                scheme = new SchemeHeader(term.SchemeId, term.SchemeName, term.AcademicYearId, term.SchemeStatusId, term.EvaluationMode, term.GradeScaleId, term.AttendanceSource);
            }
            else
            {
                scheme = await _repo.GetSchemeAsync(schoolId, schemeId!.Value, ct);
                if (scheme == null || scheme.StatusId != active.Value) return (null, Result<T>(404, "Report scheme not found.", default));
            }
            var section = await _repo.GetSectionAsync(schoolId, sectionId, active.Value, ct);
            if (section == null || !await _repo.ClassInSchemeAsync(scheme.SchemeId, section.ClassId, ct)) return (null, Result<T>(404, "Section not found in this report scheme.", default));
            var year = await _repo.YearAsync(schoolId, scheme.AcademicYearId, ct);
            if (year == null) return (null, Result<T>(500, "Something went wrong. Please try again later.", default));
            var rights = await _access.RightsAsync(schoolId, caller, scheme.AcademicYearId, section.ClassId, section.SectionId, null, active.Value, ct);
            if (!rights.CanView) return (null, Result<T>(403, "You can only see the report-card inputs of the section you are the class teacher of.", default));
            return (new Ctx { Active = active.Value, Scheme = scheme, Term = term, Section = section, Rights = rights, Year = year }, null);
        }

        private static string? WhyReadOnly(Ctx c, bool locked) =>
            locked ? LockedMessage : c.CanEdit ? null : c.Rights.IsPrincipal ? "The Principal can read these inputs but not change them." : "Only the School Admin or the section's class teacher can change these.";

        // ============================================================================ completeness
        public async Task<ApiResponse<CompletenessDto>> CompletenessAsync(string? schoolId, ReportCaller caller, long schemeId, long sectionId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<CompletenessDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var (c, fail) = await LoadAsync<CompletenessDto>(schoolId, caller, null, schemeId, sectionId, ct);
                if (c == null) return fail!;
                var roster = await _repo.GetRosterAsync(schoolId, c.Scheme.AcademicYearId, sectionId, c.Active, ct);
                var structure = await _repo.GetStructureAsync(schoolId, schemeId, c.Section.ClassId, ct);
                var esIds = structure.Select(s => s.ExamSubjectId).ToList();
                var statuses = (await _repo.GetStatusesForSectionAsync(schoolId, sectionId, esIds, ct)).ToDictionary(s => s.ExamSubjectId);
                var counts = (await _repo.GetEntryCountsAsync(schoolId, sectionId, roster.Select(r => r.StudentId).ToList(), esIds, ct)).GroupBy(x => x.ExamSubjectId).ToDictionary(g => g.Key, g => g.ToDictionary(x => x.State, x => x.Count));
                var skillCounts = await _repo.GetSkillCountsAsync(schoolId, c.Scheme.AcademicYearId, ct);
                var locks = (await _repo.GetTermSectionsAsync(schemeId, sectionId, ct)).ToDictionary(t => t.TermId);

                var dto = new CompletenessDto
                {
                    SchemeId = schemeId, SchemeName = c.Scheme.SchemeName, EvaluationMode = c.Scheme.EvaluationMode, AttendanceSource = c.Scheme.AttendanceSource, SectionId = sectionId, ClassId = c.Section.ClassId,
                    ClassName = c.Section.ClassName, SectionName = c.Section.SectionName, RosterCount = roster.Count, CanEdit = c.CanEdit, CanLock = c.Rights.IsAdmin
                };
                foreach (var term in structure.GroupBy(s => (s.TermId, s.TermName, s.TermSequence, s.TermStart, s.TermEnd)).OrderBy(g => g.Key.TermSequence))
                {
                    locks.TryGetValue(term.Key.TermId, out var ts);
                    var t = new CompletenessTermDto
                    {
                        TermId = term.Key.TermId, TermName = term.Key.TermName, StartDate = term.Key.TermStart, EndDate = term.Key.TermEnd, Locked = ts?.Status == "LOCKED", LockedAt = ts?.Status == "LOCKED" ? ts.LockedAt : null,
                        LastUnlockReason = ts?.UnlockReason,
                        Exams = term.GroupBy(x => (x.ExamId, x.ExamName, x.ExamSequence)).OrderBy(x => x.Key.ExamSequence).Select(x => new CompletenessExamDto { ExamId = x.Key.ExamId, Name = x.Key.ExamName }).ToList()
                    };
                    foreach (var subj in term.GroupBy(x => (x.SchemeSubjectId, Name: x.LabelOverride ?? x.SubjectName, x.DisplayOrder)).OrderBy(x => x.Key.DisplayOrder).ThenBy(x => x.Key.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        var sd = new CompletenessSubjectDto { SchemeSubjectId = subj.Key.SchemeSubjectId, SubjectName = subj.Key.Name };
                        foreach (var cell in subj.OrderBy(x => x.ExamSequence))
                        {
                            var per = cell.EntryMode == "GRADE" ? Math.Max(1, skillCounts.GetValueOrDefault(cell.SchemeSubjectId)) : 1;
                            var cn = counts.GetValueOrDefault(cell.ExamSubjectId) ?? new Dictionary<string, int>();
                            var expected = roster.Count * per;
                            var entered = cn.GetValueOrDefault("ENTERED"); var absent = cn.GetValueOrDefault("ABSENT"); var exempt = cn.GetValueOrDefault("EXEMPT");
                            statuses.TryGetValue(cell.ExamSubjectId, out var st);
                            sd.Cells.Add(new CompletenessCellDto
                            {
                                ExamId = cell.ExamId, ExamSubjectId = cell.ExamSubjectId, EntryMode = cell.EntryMode, Status = st?.Status ?? "NOT_STARTED", Expected = expected,
                                Entered = entered, Absent = absent, Exempt = exempt, Missing = Math.Max(0, expected - entered - absent - exempt)
                            });
                        }
                        t.Subjects.Add(sd);
                    }
                    var cells = t.Subjects.SelectMany(s => s.Cells).ToList();
                    t.Cells = cells.Count; t.Accepted = cells.Count(x => x.Status == "ACCEPTED"); t.Submitted = cells.Count(x => x.Status == "SUBMITTED"); t.Draft = cells.Count(x => x.Status == "DRAFT"); t.NotStarted = cells.Count(x => x.Status == "NOT_STARTED");
                    t.ReadyToLock = cells.Count > 0 && roster.Count > 0 && cells.All(x => x.Status == "ACCEPTED" && x.Missing == 0);
                    dto.Terms.Add(t);
                }
                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex) { return Error<CompletenessDto>(ex, "building the completeness of a section"); }
        }

        // ============================================================================ activity grades
        public async Task<ApiResponse<ActivityGradesDto>> GetActivityGradesAsync(string? schoolId, ReportCaller caller, long termId, long sectionId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ActivityGradesDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var (c, fail) = await LoadAsync<ActivityGradesDto>(schoolId, caller, termId, null, sectionId, ct);
                if (c == null) return fail!;
                return Result(200, "Successfully fetched", await BuildActivityAsync(schoolId, c, ct));
            }
            catch (Exception ex) { return Error<ActivityGradesDto>(ex, "fetching activity grades"); }
        }

        private async Task<ActivityGradesDto> BuildActivityAsync(string schoolId, Ctx c, CancellationToken ct)
        {
            var areas = await _repo.GetActivityAreasAsync(schoolId, c.Active, ct);
            var bands = (await _repo.GetBandsAsync(schoolId, areas.Select(a => a.RatingScaleId).Distinct().ToList(), ct)).GroupBy(b => b.GradeScaleId).ToDictionary(g => g.Key, g => g.OrderBy(b => b.Sequence).ToList());
            var roster = await _repo.GetRosterAsync(schoolId, c.Scheme.AcademicYearId, c.Section.SectionId, c.Active, ct);
            var grades = (await _repo.GetActivityGradesAsync(c.Term!.TermId, c.Section.SectionId, false, ct)).GroupBy(g => g.StudentId).ToDictionary(g => g.Key, g => g.ToDictionary(x => x.AreaId.ToString(CultureInfo.InvariantCulture), x => x.GradeCode));
            var ts = await _repo.GetTermSectionAsync(c.Term.TermId, c.Section.SectionId, false, ct);
            var locked = ts?.Status == "LOCKED";
            return new ActivityGradesDto
            {
                TermId = c.Term.TermId, TermName = c.Term.TermName, SectionId = c.Section.SectionId, Locked = locked, CanEdit = c.CanEdit && !locked, ReadOnlyReason = c.CanEdit && !locked ? null : WhyReadOnly(c, locked),
                Areas = areas.Select(a => new ActivityGradeAreaDto { AreaId = a.AreaId, Category = a.Category, Name = a.Name, Options = bands.GetValueOrDefault(a.RatingScaleId)?.Select(b => new GradeOptionDto { Code = b.Code, Label = b.Label }).ToList() ?? new() }).ToList(),
                Students = roster.OrderBy(r => r.RollNumber, RollComparer.Instance).ThenBy(r => r.FirstName, StringComparer.OrdinalIgnoreCase)
                    .Select(r => new ActivityStudentDto { StudentId = r.StudentId, RollNumber = r.RollNumber ?? "", FullName = LeaveRules.FullName(r.FirstName, r.MiddleName, r.LastName), Grades = grades.GetValueOrDefault(r.StudentId) ?? new() }).ToList()
            };
        }

        public async Task<ApiResponse<ActivityGradesDto>> SaveActivityGradesAsync(string? schoolId, ReportCaller caller, SaveActivityGradesRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ActivityGradesDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var (c, fail) = await LoadAsync<ActivityGradesDto>(schoolId, caller, req.TermId, null, req.SectionId!.Value, ct);
                if (c == null) return fail!;
                if (!c.CanEdit) return Result<ActivityGradesDto>(403, WhyReadOnly(c, false)!, null);
                var roster = (await _repo.GetRosterAsync(schoolId, c.Scheme.AcademicYearId, c.Section.SectionId, c.Active, ct)).ToDictionary(r => r.StudentId);
                var areas = (await _repo.GetActivityAreasAsync(schoolId, c.Active, ct)).ToDictionary(a => a.AreaId);
                var bands = (await _repo.GetBandsAsync(schoolId, areas.Values.Select(a => a.RatingScaleId).Distinct().ToList(), ct)).GroupBy(b => b.GradeScaleId).ToDictionary(g => g.Key, g => g.ToDictionary(b => b.Code, b => b.Code, StringComparer.OrdinalIgnoreCase));

                var problems = new List<string>(); var seen = new HashSet<(string, long)>();
                var clean = new List<(string Student, long Area, string? Code)>();
                foreach (var g in req.Grades)
                {
                    if (!roster.TryGetValue(g.StudentId, out var s)) { problems.Add($"{g.StudentId}: not on the active roster of this section."); continue; }
                    var who = LeaveRules.FullName(s.FirstName, s.MiddleName, s.LastName);
                    if (!areas.TryGetValue(g.AreaId!.Value, out var area)) { problems.Add($"{who}: that activity area does not exist or is inactive."); continue; }
                    if (!seen.Add((g.StudentId, g.AreaId.Value))) { problems.Add($"{who}: {area.Name} is listed twice."); continue; }
                    var code = string.IsNullOrWhiteSpace(g.GradeCode) ? null : g.GradeCode.Trim();
                    if (code != null)
                    {
                        if (!(bands.GetValueOrDefault(area.RatingScaleId)?.TryGetValue(code, out var canon) ?? false)) { problems.Add($"{who}: '{code}' is not a grade of {area.Name}'s rating scale."); continue; }
                        code = bands[area.RatingScaleId][code];
                    }
                    clean.Add((g.StudentId, g.AreaId.Value, code));
                }
                if (problems.Count > 0) return Result<ActivityGradesDto>(400, string.Join(" ", problems.Take(10)) + (problems.Count > 10 ? $" … and {problems.Count - 10} more." : ""), null);

                await using var tx = await _repo.BeginTransactionAsync(ct);
                try
                {
                    await _repo.LockTermSectionKeyAsync(c.Term!.TermId, c.Section.SectionId, ct);
                    if ((await _repo.GetTermSectionAsync(c.Term.TermId, c.Section.SectionId, false, ct))?.Status == "LOCKED") return Result<ActivityGradesDto>(409, LockedMessage, null);
                    var existing = (await _repo.GetActivityGradesAsync(c.Term.TermId, c.Section.SectionId, true, ct)).ToDictionary(g => (g.StudentId, g.AreaId));
                    var now = DateTime.UtcNow;
                    foreach (var (student, area, code) in clean)
                    {
                        existing.TryGetValue((student, area), out var row);
                        if (code == null) { if (row != null) _repo.Remove(row); continue; }
                        if (row == null) _repo.Add(new TbReportStudentActivityGrade { TermId = c.Term.TermId, StudentId = student, AreaId = area, SchemeId = c.Scheme.SchemeId, SectionId = c.Section.SectionId, SchoolId = schoolId, GradeCode = code, UpdatedBy = caller.UserId, UpdatedAt = now });
                        else { row.GradeCode = code; row.UpdatedBy = caller.UserId; row.UpdatedAt = now; }
                    }
                    await _repo.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                }
                catch (DbUpdateException ex) when ((ex.InnerException as PostgresException)?.SqlState == PostgresErrorCodes.UniqueViolation)
                { return Result<ActivityGradesDto>(409, "A grade for one of these students was recorded under another section of the class. Ask the School Admin to look at it.", null); }
                _repo.ClearTracking();
                return Result(200, "Activity grades saved.", await BuildActivityAsync(schoolId, c, ct));
            }
            catch (Exception ex) { return Error<ActivityGradesDto>(ex, "saving activity grades"); }
        }

        // ============================================================================ remarks, conduct, rank text, manual attendance
        public async Task<ApiResponse<TermStudentsDto>> GetTermStudentsAsync(string? schoolId, ReportCaller caller, long termId, long sectionId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TermStudentsDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var (c, fail) = await LoadAsync<TermStudentsDto>(schoolId, caller, termId, null, sectionId, ct);
                if (c == null) return fail!;
                return Result(200, "Successfully fetched", await BuildTermStudentsAsync(schoolId, c, ct));
            }
            catch (Exception ex) { return Error<TermStudentsDto>(ex, "fetching term inputs"); }
        }

        private async Task<TermStudentsDto> BuildTermStudentsAsync(string schoolId, Ctx c, CancellationToken ct)
        {
            var roster = await _repo.GetRosterAsync(schoolId, c.Scheme.AcademicYearId, c.Section.SectionId, c.Active, ct);
            var rows = (await _repo.GetTermStudentsAsync(c.Term!.TermId, c.Section.SectionId, false, ct)).ToDictionary(r => r.StudentId);
            var ts = await _repo.GetTermSectionAsync(c.Term.TermId, c.Section.SectionId, false, ct);
            var locked = ts?.Status == "LOCKED";
            return new TermStudentsDto
            {
                TermId = c.Term.TermId, TermName = c.Term.TermName, SectionId = c.Section.SectionId, AttendanceSource = c.Scheme.AttendanceSource, EvaluationMode = c.Scheme.EvaluationMode, Locked = locked,
                CanEdit = c.CanEdit && !locked, ReadOnlyReason = c.CanEdit && !locked ? null : WhyReadOnly(c, locked),
                Students = roster.OrderBy(r => r.RollNumber, RollComparer.Instance).ThenBy(r => r.FirstName, StringComparer.OrdinalIgnoreCase).Select(r =>
                {
                    rows.TryGetValue(r.StudentId, out var t);
                    return new TermStudentDto { StudentId = r.StudentId, RollNumber = r.RollNumber ?? "", FullName = LeaveRules.FullName(r.FirstName, r.MiddleName, r.LastName), Remark = t?.Remark, Conduct = t?.Conduct, RankText = t?.RankText, ManualWorkingDays = t?.ManualWorkingDays, ManualPresentDays = t?.ManualPresentDays };
                }).ToList()
            };
        }

        public async Task<ApiResponse<TermStudentsDto>> SaveTermStudentsAsync(string? schoolId, ReportCaller caller, SaveTermStudentsRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TermStudentsDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var (c, fail) = await LoadAsync<TermStudentsDto>(schoolId, caller, req.TermId, null, req.SectionId!.Value, ct);
                if (c == null) return fail!;
                if (!c.CanEdit) return Result<TermStudentsDto>(403, WhyReadOnly(c, false)!, null);
                var roster = (await _repo.GetRosterAsync(schoolId, c.Scheme.AcademicYearId, c.Section.SectionId, c.Active, ct)).ToDictionary(r => r.StudentId);
                var problems = new List<string>(); var seen = new HashSet<string>();
                foreach (var s in req.Students)
                {
                    if (!roster.TryGetValue(s.StudentId, out var r)) { problems.Add($"{s.StudentId}: not on the active roster of this section."); continue; }
                    var who = LeaveRules.FullName(r.FirstName, r.MiddleName, r.LastName);
                    if (!seen.Add(s.StudentId)) problems.Add($"{who}: listed twice.");
                    if (s.ManualPresentDays != null && s.ManualWorkingDays != null && s.ManualPresentDays > s.ManualWorkingDays) problems.Add($"{who}: present days ({s.ManualPresentDays}) cannot be more than working days ({s.ManualWorkingDays}).");
                    if (s.ManualPresentDays != null && s.ManualWorkingDays == null) problems.Add($"{who}: give the working days as well as the present days.");
                }
                if (problems.Count > 0) return Result<TermStudentsDto>(400, string.Join(" ", problems.Take(10)) + (problems.Count > 10 ? $" … and {problems.Count - 10} more." : ""), null);

                await using var tx = await _repo.BeginTransactionAsync(ct);
                try
                {
                    await _repo.LockTermSectionKeyAsync(c.Term!.TermId, c.Section.SectionId, ct);
                    if ((await _repo.GetTermSectionAsync(c.Term.TermId, c.Section.SectionId, false, ct))?.Status == "LOCKED") return Result<TermStudentsDto>(409, LockedMessage, null);
                    var existing = (await _repo.GetTermStudentsAsync(c.Term.TermId, c.Section.SectionId, true, ct)).ToDictionary(t => t.StudentId);
                    var now = DateTime.UtcNow;
                    foreach (var s in req.Students)
                    {
                        existing.TryGetValue(s.StudentId, out var row);
                        string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
                        var remark = Clean(s.Remark); var conduct = Clean(s.Conduct); var rank = Clean(s.RankText);
                        if (remark == null && conduct == null && rank == null && s.ManualWorkingDays == null && s.ManualPresentDays == null) { if (row != null) _repo.Remove(row); continue; }
                        if (row == null) { row = new TbReportTermStudent { TermId = c.Term.TermId, StudentId = s.StudentId, SchemeId = c.Scheme.SchemeId, SectionId = c.Section.SectionId, SchoolId = schoolId }; _repo.Add(row); }
                        row.Remark = remark; row.Conduct = conduct; row.RankText = rank; row.ManualWorkingDays = (short?)s.ManualWorkingDays; row.ManualPresentDays = (short?)s.ManualPresentDays; row.UpdatedBy = caller.UserId; row.UpdatedAt = now;
                    }
                    await _repo.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                }
                catch (DbUpdateException ex) when ((ex.InnerException as PostgresException)?.SqlState == PostgresErrorCodes.UniqueViolation)
                { return Result<TermStudentsDto>(409, "Inputs for one of these students were recorded under another section of the class. Ask the School Admin to look at it.", null); }
                _repo.ClearTracking();
                return Result(200, "Saved.", await BuildTermStudentsAsync(schoolId, c, ct));
            }
            catch (Exception ex) { return Error<TermStudentsDto>(ex, "saving term inputs"); }
        }

        // ============================================================================ promotion (per year)
        public async Task<ApiResponse<YearStudentsDto>> GetYearStudentsAsync(string? schoolId, ReportCaller caller, long schemeId, long sectionId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<YearStudentsDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var (c, fail) = await LoadAsync<YearStudentsDto>(schoolId, caller, null, schemeId, sectionId, ct);
                if (c == null) return fail!;
                return Result(200, "Successfully fetched", await BuildYearAsync(schoolId, c, ct));
            }
            catch (Exception ex) { return Error<YearStudentsDto>(ex, "fetching promotion inputs"); }
        }

        /// <summary>Year inputs are final once the LAST term of the scheme is locked for the section.</summary>
        private async Task<(bool Locked, long? LastTermId)> YearLockedAsync(Ctx c, CancellationToken ct)
        {
            var last = await _repo.LastTermIdAsync(c.Scheme.SchemeId, ct);
            if (last == null) return (false, null);
            return ((await _repo.GetTermSectionAsync(last.Value, c.Section.SectionId, false, ct))?.Status == "LOCKED", last);
        }

        private async Task<YearStudentsDto> BuildYearAsync(string schoolId, Ctx c, CancellationToken ct)
        {
            var roster = await _repo.GetRosterAsync(schoolId, c.Scheme.AcademicYearId, c.Section.SectionId, c.Active, ct);
            var rows = (await _repo.GetYearStudentsAsync(c.Scheme.SchemeId, c.Section.SectionId, false, ct)).ToDictionary(r => r.StudentId);
            var (locked, _) = await YearLockedAsync(c, ct);
            return new YearStudentsDto
            {
                SchemeId = c.Scheme.SchemeId, SectionId = c.Section.SectionId, Locked = locked, CanEdit = c.CanEdit && !locked,
                ReadOnlyReason = c.CanEdit && !locked ? null : locked ? "The last term of this scheme is locked for the section, so the year's promotion decisions are final until a School Admin unlocks it." : WhyReadOnly(c, false),
                Students = roster.OrderBy(r => r.RollNumber, RollComparer.Instance).ThenBy(r => r.FirstName, StringComparer.OrdinalIgnoreCase).Select(r =>
                {
                    rows.TryGetValue(r.StudentId, out var y);
                    return new YearStudentDto { StudentId = r.StudentId, RollNumber = r.RollNumber ?? "", FullName = LeaveRules.FullName(r.FirstName, r.MiddleName, r.LastName), PromotionDecision = y?.PromotionDecision, PromotedTo = y?.PromotedTo, NewSessionDate = y?.NewSessionDate == null ? null : DateOnly.FromDateTime(y.NewSessionDate.Value) };
                }).ToList()
            };
        }

        private static readonly HashSet<string> Decisions = new() { "PROMOTED", "NOT_PROMOTED", "DETAINED" };

        public async Task<ApiResponse<YearStudentsDto>> SaveYearStudentsAsync(string? schoolId, ReportCaller caller, SaveYearStudentsRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<YearStudentsDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var (c, fail) = await LoadAsync<YearStudentsDto>(schoolId, caller, null, req.SchemeId, req.SectionId!.Value, ct);
                if (c == null) return fail!;
                if (!c.CanEdit) return Result<YearStudentsDto>(403, WhyReadOnly(c, false)!, null);
                var roster = (await _repo.GetRosterAsync(schoolId, c.Scheme.AcademicYearId, c.Section.SectionId, c.Active, ct)).ToDictionary(r => r.StudentId);
                var problems = new List<string>(); var seen = new HashSet<string>();
                foreach (var s in req.Students)
                {
                    if (!roster.TryGetValue(s.StudentId, out var r)) { problems.Add($"{s.StudentId}: not on the active roster of this section."); continue; }
                    var who = LeaveRules.FullName(r.FirstName, r.MiddleName, r.LastName);
                    if (!seen.Add(s.StudentId)) problems.Add($"{who}: listed twice.");
                    var d = string.IsNullOrWhiteSpace(s.PromotionDecision) ? null : s.PromotionDecision.Trim().ToUpperInvariant();
                    if (d != null && !Decisions.Contains(d)) problems.Add($"{who}: the decision must be PROMOTED, NOT_PROMOTED or DETAINED.");
                    if (s.NewSessionDate != null && (s.NewSessionDate < DateOnly.FromDateTime(c.Year.StartDate.ToDateTime(TimeOnly.MinValue)) || s.NewSessionDate > c.Year.EndDate.AddYears(1)))
                        problems.Add($"{who}: the new session date must be between the start of this academic year and a year after its end.");
                }
                if (problems.Count > 0) return Result<YearStudentsDto>(400, string.Join(" ", problems.Take(10)) + (problems.Count > 10 ? $" … and {problems.Count - 10} more." : ""), null);

                await using var tx = await _repo.BeginTransactionAsync(ct);
                try
                {
                    var (_, last) = await YearLockedAsync(c, ct);
                    if (last != null)
                    {
                        await _repo.LockTermSectionKeyAsync(last.Value, c.Section.SectionId, ct);
                        if ((await _repo.GetTermSectionAsync(last.Value, c.Section.SectionId, false, ct))?.Status == "LOCKED") return Result<YearStudentsDto>(409, "The last term of this scheme is locked for the section, so promotion decisions can't change. A School Admin has to unlock it first.", null);
                    }
                    var existing = (await _repo.GetYearStudentsAsync(c.Scheme.SchemeId, c.Section.SectionId, true, ct)).ToDictionary(t => t.StudentId);
                    var now = DateTime.UtcNow;
                    foreach (var s in req.Students)
                    {
                        existing.TryGetValue(s.StudentId, out var row);
                        var decision = string.IsNullOrWhiteSpace(s.PromotionDecision) ? null : s.PromotionDecision.Trim().ToUpperInvariant();
                        var to = string.IsNullOrWhiteSpace(s.PromotedTo) ? null : s.PromotedTo.Trim();
                        if (decision == null && to == null && s.NewSessionDate == null) { if (row != null) _repo.Remove(row); continue; }
                        if (row == null) { row = new TbReportYearStudent { SchemeId = c.Scheme.SchemeId, StudentId = s.StudentId, SectionId = c.Section.SectionId, SchoolId = schoolId }; _repo.Add(row); }
                        row.PromotionDecision = decision; row.PromotedTo = to; row.NewSessionDate = s.NewSessionDate?.ToDateTime(TimeOnly.MinValue); row.UpdatedBy = caller.UserId; row.UpdatedAt = now;
                    }
                    await _repo.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                }
                catch (DbUpdateException ex) when ((ex.InnerException as PostgresException)?.SqlState == PostgresErrorCodes.UniqueViolation)
                { return Result<YearStudentsDto>(409, "A decision for one of these students was recorded under another section of the class. Ask the School Admin to look at it.", null); }
                _repo.ClearTracking();
                return Result(200, "Saved.", await BuildYearAsync(schoolId, c, ct));
            }
            catch (Exception ex) { return Error<YearStudentsDto>(ex, "saving promotion inputs"); }
        }

        // ============================================================================ attendance for the term dates
        public async Task<ApiResponse<TermAttendanceDto>> AttendanceAsync(string? schoolId, ReportCaller caller, long termId, long sectionId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TermAttendanceDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var (c, fail) = await LoadAsync<TermAttendanceDto>(schoolId, caller, termId, null, sectionId, ct);
                if (c == null) return fail!;
                var term = c.Term!; var today = SchoolClock.Today();
                var dto = new TermAttendanceDto { TermId = term.TermId, TermName = term.TermName, SectionId = sectionId, From = term.StartDate, To = term.EndDate, Today = today, Source = c.Scheme.AttendanceSource };

                if (c.Scheme.AttendanceSource == "MANUAL")
                {
                    var roster = await _repo.GetRosterAsync(schoolId, c.Scheme.AcademicYearId, sectionId, c.Active, ct);
                    var rows = (await _repo.GetTermStudentsAsync(term.TermId, sectionId, false, ct)).ToDictionary(r => r.StudentId);
                    foreach (var r in roster.OrderBy(r => r.RollNumber, RollComparer.Instance).ThenBy(r => r.FirstName, StringComparer.OrdinalIgnoreCase))
                    {
                        rows.TryGetValue(r.StudentId, out var t);
                        var w = t?.ManualWorkingDays; var p = t?.ManualPresentDays;
                        dto.Students.Add(new TermAttendanceStudentDto
                        {
                            StudentId = r.StudentId, RollNumber = r.RollNumber ?? "", FullName = LeaveRules.FullName(r.FirstName, r.MiddleName, r.LastName), WorkingDays = w, PresentDays = p,
                            AbsentDays = w != null && p != null ? w.Value - p.Value : 0, Percentage = w is > 0 && p != null ? Math.Round(p.Value * 100m / w.Value, 1, MidpointRounding.AwayFromZero) : null
                        });
                    }
                    var blank = dto.Students.Count(s => s.WorkingDays == null || s.PresentDays == null);
                    if (blank > 0) dto.Warning = $"{blank} student{(blank == 1 ? " has" : "s have")} no working or present days typed in yet.";
                    return Result(200, "Successfully fetched", dto);
                }

                // AUTO: the Attendance module's own rules (AttendanceDayRules), over the term's dates
                var year = c.Year;
                var from = term.StartDate; var to = term.EndDate;
                var ay = await _attendance.GetReportRosterAsync(schoolId, c.Scheme.AcademicYearId, sectionId, ct);
                var marks = await _attendance.GetMonthMarksAsync(schoolId, sectionId, from, to, ct);
                var holidays = await _attendance.GetHolidaysAsync(schoolId, from, to, c.Active, ct);
                var byStudent = marks.GroupBy(m => m.StudentId).ToDictionary(g => g.Key, g => g.GroupBy(m => m.Date).ToDictionary(x => x.Key, x => x.First().StatusCode));
                var markedPerDay = marks.GroupBy(m => m.Date).ToDictionary(g => g.Key, g => g.Select(m => m.StudentId).Distinct().Count());

                var days = new List<DayClass>();
                for (var d = from; d <= to; d = d.AddDays(1)) days.Add(AttendanceDayRules.Classify(d, year.StartDate, year.EndDate, holidays));
                var elapsed = days.Where(d => d.IsWorkingDay && d.Date <= today).ToList();
                dto.WorkingDays = elapsed.Count;
                dto.UnmarkedDates = elapsed.Where(d => markedPerDay.GetValueOrDefault(d.Date) == 0).Select(d => d.Date).ToList();
                dto.UnmarkedDays = dto.UnmarkedDates.Count;

                foreach (var r in ay)
                {
                    var activeStudent = r.EnrollmentStatusId == c.Active && r.StudentStatusId == c.Active;
                    var own = byStudent.GetValueOrDefault(r.StudentId);
                    if (!(activeStudent && r.EnrollmentDate <= to) && own == null) continue;
                    var s = new TermAttendanceStudentDto { StudentId = r.StudentId, RollNumber = r.RollNumber ?? "", FullName = LeaveRules.FullName(r.FirstName, r.MiddleName, r.LastName) };
                    var present = 0; var absent = 0; var notMarked = 0;
                    foreach (var day in days)
                    {
                        if (!AttendanceDayRules.CountsForStudent(day, r.EnrollmentDate)) continue;
                        var code = own != null && own.TryGetValue(day.Date, out var cc) ? cc : null;
                        if (code == AttendanceReportBuilder.PresentCode) present++;
                        else if (code == AttendanceReportBuilder.AbsentCode) absent++;
                        else if (day.Date <= today) notMarked++;
                    }
                    s.PresentDays = present; s.AbsentDays = absent; s.WorkingDays = present + absent; s.NotMarkedDays = notMarked;
                    s.Percentage = present + absent == 0 ? null : Math.Round(present * 100m / (present + absent), 1, MidpointRounding.AwayFromZero);
                    dto.Students.Add(s);
                }
                dto.Students = dto.Students.OrderBy(s => s.RollNumber, RollComparer.Instance).ThenBy(s => s.FullName, StringComparer.OrdinalIgnoreCase).ToList();
                var parts = new List<string>();
                if (dto.UnmarkedDays > 0)
                    parts.Add($"{dto.UnmarkedDays} working day{(dto.UnmarkedDays == 1 ? "" : "s")} so far {(dto.UnmarkedDays == 1 ? "has" : "have")} no attendance marked for anyone in this section ({string.Join(", ", dto.UnmarkedDates.Take(5).Select(d => d.ToString("dd MMM", CultureInfo.InvariantCulture)))}{(dto.UnmarkedDays > 5 ? ", …" : "")}). They are not counted: mark them, or confirm they were not school days, before publishing.");
                var some = dto.Students.Count(s => s.NotMarkedDays > 0);
                if (some > 0 && dto.UnmarkedDays == 0) parts.Add($"{some} student{(some == 1 ? " has" : "s have")} working days that were not marked for them.");
                dto.Warning = parts.Count == 0 ? null : string.Join(" ", parts);
                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex) { return Error<TermAttendanceDto>(ex, "building the term attendance"); }
        }

        // ============================================================================ lock / unlock
        public async Task<ApiResponse<LockResultDto>> LockAsync(string? schoolId, ReportCaller caller, LockRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<LockResultDto>(400, TenantMessages.NoSchool, null);
            try
            {
                if (!caller.IsAdmin) return Result<LockResultDto>(403, "Only a School Admin can lock a term.", null);
                var (c, fail) = await LoadAsync<LockResultDto>(schoolId, caller, req.TermId, null, req.SectionId!.Value, ct);
                if (c == null) return fail!;
                var term = c.Term!; var sectionId = c.Section.SectionId;
                var structure = (await _repo.GetStructureAsync(schoolId, c.Scheme.SchemeId, c.Section.ClassId, ct)).Where(s => s.TermId == term.TermId).ToList();
                if (structure.Count == 0) return Result<LockResultDto>(409, "This term has no exam subjects for the class, so there is nothing to lock.", null);
                var esIds = structure.Select(s => s.ExamSubjectId).ToList();

                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockTermSectionKeyAsync(term.TermId, sectionId, ct);                  // serialises against class-teacher input saves and a second lock
                var ts = await _repo.GetTermSectionAsync(term.TermId, sectionId, true, ct);
                if (ts?.Status == "LOCKED") return Result<LockResultDto>(409, "This term is already locked for the section.", null);
                var statuses = (await _repo.GetStatusesForUpdateAsync(schoolId, sectionId, esIds, ct)).ToDictionary(s => s.ExamSubjectId);   // row locks: no one can reopen while we check

                var notAccepted = structure.Where(s => !statuses.TryGetValue(s.ExamSubjectId, out var st) || st.Status != "ACCEPTED")
                    .Select(s => $"{s.LabelOverride ?? s.SubjectName} ({s.ExamName}) is {(statuses.TryGetValue(s.ExamSubjectId, out var st) ? st.Status.ToLowerInvariant() : "not started")}").ToList();
                if (notAccepted.Count > 0)
                    return Result<LockResultDto>(409, $"Every exam subject of the term must be accepted before it can be locked. Not yet: {string.Join("; ", notAccepted.Take(6))}{(notAccepted.Count > 6 ? $"; and {notAccepted.Count - 6} more" : "")}.", null);

                // A grid can only be ACCEPTED when every roster student had an explicit state, so a gap now means the student joined the section after that. Such a student must not block
                // the lock: they are listed, the admin confirms, and they are left out of this term's results (Stage 3 treats them as INCOMPLETE).
                var roster = await _repo.GetRosterAsync(schoolId, c.Scheme.AcademicYearId, sectionId, c.Active, ct);
                var perStudent = (await _repo.GetStudentEntryCountsAsync(schoolId, roster.Select(r => r.StudentId).ToList(), esIds, ct)).ToDictionary(x => (x.ExamSubjectId, x.StudentId), x => x.Count);
                var skillCounts = await _repo.GetSkillCountsAsync(schoolId, c.Scheme.AcademicYearId, ct);
                var late = roster.Where(r => structure.Any(s => perStudent.GetValueOrDefault((s.ExamSubjectId, r.StudentId)) < (s.EntryMode == "GRADE" ? Math.Max(1, skillCounts.GetValueOrDefault(s.SchemeSubjectId)) : 1)))
                    .OrderBy(r => r.RollNumber, RollComparer.Instance).ThenBy(r => r.FirstName, StringComparer.OrdinalIgnoreCase).ToList();
                var excluded = late.Select(r => new ExcludedStudentDto { StudentId = r.StudentId, Name = LeaveRules.FullName(r.FirstName, r.MiddleName, r.LastName), RollNumber = r.RollNumber ?? "" }).ToList();
                if (excluded.Count > 0 && !req.ConfirmExcluded)
                {
                    var names = string.Join(", ", excluded.Take(10).Select(e => e.Name)) + (excluded.Count > 10 ? $" and {excluded.Count - 10} more" : "");
                    return new ApiResponse<LockResultDto>
                    {
                        Status = false, StatusCode = 409,
                        Message = $"{excluded.Count} student{(excluded.Count == 1 ? " was" : "s were")} added to the section after the marks were accepted and {(excluded.Count == 1 ? "has" : "have")} no marks for this term: {names}. They will be left out of this term's results and included in a later version once marks are entered and the term is locked again. Lock anyway?",
                        Data = new LockResultDto { TermId = term.TermId, SectionId = sectionId, Locked = false, NeedsConfirmation = true, Excluded = excluded }
                    };
                }

                var now = DateTime.UtcNow;
                if (ts == null) { ts = new TbReportTermSection { TermId = term.TermId, SectionId = sectionId, SchemeId = c.Scheme.SchemeId, ClassId = c.Section.ClassId, SchoolId = schoolId }; _repo.Add(ts); }
                ts.Status = "LOCKED"; ts.LockedBy = caller.UserId; ts.LockedAt = now; ts.UpdatedAt = now;
                _repo.Add(new TbReportAudit { SchoolId = schoolId, Event = "LOCK", SchemeId = c.Scheme.SchemeId, TermId = term.TermId, SectionId = sectionId, FromStatus = "OPEN", ToStatus = "LOCKED", ActorUserId = caller.UserId, CreatedAt = now,
                    ExcludedStudents = excluded.Count == 0 ? null : string.Join(",", excluded.Select(e => e.StudentId)) });
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return Result(200, excluded.Count == 0 ? "Locked." : $"Locked. {excluded.Count} late student{(excluded.Count == 1 ? " was" : "s were")} left out of this term's results.", new LockResultDto { TermId = term.TermId, SectionId = sectionId, Locked = true, LockedAt = now, Excluded = excluded });
            }
            catch (DbUpdateException ex) when ((ex.InnerException as PostgresException)?.SqlState == PostgresErrorCodes.UniqueViolation)
            { return Result<LockResultDto>(409, "This term was just locked by someone else.", null); }
            catch (Exception ex) { return Error<LockResultDto>(ex, "locking a term"); }
        }

        public async Task<ApiResponse<LockResultDto>> UnlockAsync(string? schoolId, ReportCaller caller, LockRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<LockResultDto>(400, TenantMessages.NoSchool, null);
            try
            {
                if (!caller.IsAdmin) return Result<LockResultDto>(403, "Only a School Admin can unlock a term.", null);
                var reason = (req.Reason ?? "").Trim();
                if (reason.Length < 3) return Result<LockResultDto>(400, "Say why you are unlocking it (at least 3 characters). The reason is kept in the audit trail.", null);
                var (c, fail) = await LoadAsync<LockResultDto>(schoolId, caller, req.TermId, null, req.SectionId!.Value, ct);
                if (c == null) return fail!;
                var term = c.Term!; var sectionId = c.Section.SectionId;

                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockTermSectionKeyAsync(term.TermId, sectionId, ct);
                var ts = await _repo.GetTermSectionAsync(term.TermId, sectionId, true, ct);
                if (ts == null || ts.Status != "LOCKED") return Result<LockResultDto>(409, "This term is not locked for the section.", null);
                var now = DateTime.UtcNow;
                ts.Status = "OPEN"; ts.UnlockedBy = caller.UserId; ts.UnlockedAt = now; ts.UnlockReason = reason; ts.UpdatedAt = now;
                _repo.Add(new TbReportAudit { SchoolId = schoolId, Event = "UNLOCK", SchemeId = c.Scheme.SchemeId, TermId = term.TermId, SectionId = sectionId, FromStatus = "LOCKED", ToStatus = "OPEN", Reason = reason, ActorUserId = caller.UserId, CreatedAt = now });
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                // entries stay ACCEPTED: changing one still needs an audited reopen with a reason
                return Result(200, "Unlocked. The marks stay accepted; reopen a grid (with a reason) to change it.", new LockResultDto { TermId = term.TermId, SectionId = sectionId, Locked = false });
            }
            catch (Exception ex) { return Error<LockResultDto>(ex, "unlocking a term"); }
        }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) => new() { Status = code < 400, StatusCode = code, Message = message, Data = data };
        private ApiResponse<T> Error<T>(Exception ex, string action) { _logger.LogError(ex, "Error while {Action}", action); return Result<T>(500, "Something went wrong. Please try again later.", default); }
    }
}
