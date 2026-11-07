using System.Globalization;
using ClosedXML.Excel;
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
    /// Marks entry. One grid per (exam subject, section). States: DRAFT (editable) -> SUBMITTED -> ACCEPTED; a reopen (with a reason, audited) goes back to DRAFT.
    /// "Locked" is NOT an entry state: it is derived from tb_report_term_section, the one lock authority, so the two can never contradict each other.
    /// <para>Concurrency: every grid has a version token. A save, submit, accept or reopen carries the version it read; the status row is locked FOR UPDATE for the check and the
    /// action is refused with 409 when the version has moved on, so two people saving the same grid never silently overwrite each other.</para>
    /// </summary>
    public class ReportMarksService : IReportMarksService
    {
        private const string Draft = "DRAFT", Submitted = "SUBMITTED", Accepted = "ACCEPTED";
        private const int MaxIssues = 10;
        private const string StaleMessage = "Someone else changed this grid after you opened it. Reload it to see their changes, then enter yours again.";
        private const string LockedMessage = "This term is locked for the section. A School Admin has to unlock it, and a grid that was accepted still has to be reopened (with a reason) before it can change.";

        private readonly IReportMarksRepository _repo;
        private readonly ReportAccess _access;
        private readonly ILogger<ReportMarksService> _logger;
        public ReportMarksService(IReportMarksRepository repo, ReportAccess access, ILogger<ReportMarksService> logger) { _repo = repo; _access = access; _logger = logger; }

        // ============================================================================ context: what may I enter
        public async Task<ApiResponse<MarksContextDto>> ContextAsync(string? schoolId, ReportCaller caller, long? academicYearId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<MarksContextDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await _repo.ActiveStatusIdAsync(ct);
                if (active == null) return Result<MarksContextDto>(500, "The system is not configured correctly. Please contact support.", null);
                var year = academicYearId != null ? await _repo.YearAsync(schoolId, academicYearId.Value, ct) : await _repo.CurrentYearAsync(schoolId, active.Value, ct);
                if (year == null) return Result<MarksContextDto>(academicYearId != null ? 404 : 409, academicYearId != null ? "The selected academic year was not found." : "The school has no current academic year.", null);

                var rows = await _repo.GetContextRowsAsync(schoolId, year.AcademicYearId, active.Value, ct);
                var statuses = (await _repo.GetStatusesForYearAsync(schoolId, year.AcademicYearId, ct)).ToDictionary(s => (s.ExamSubjectId, s.SectionId));
                var locks = (await _repo.GetTermSectionsForYearAsync(schoolId, year.AcademicYearId, ct)).Where(t => t.Status == "LOCKED").Select(t => (t.TermId, t.SectionId)).ToHashSet();
                var skillCounts = await _repo.GetSkillCountsAsync(schoolId, year.AcademicYearId, ct);
                var mapped = await _repo.GetMappedPairsAsync(schoolId, active.Value, ct);

                HashSet<long> classTeacherOf = new(); HashSet<(long SectionId, long SubjectId)> teaches = new();
                if (!caller.IsAdmin)
                {
                    var employeeId = await _access.EmployeeIdAsync(schoolId, caller, active.Value, ct);
                    if (employeeId != null)
                    {
                        classTeacherOf = await _repo.ClassTeacherSectionIdsAsync(schoolId, employeeId, year.AcademicYearId, active.Value, ct);
                        teaches = await _repo.TeachingPairsAsync(schoolId, employeeId, year.AcademicYearId, active.Value, ct);
                    }
                }

                var list = new List<MarksContextRowDto>();
                foreach (var r in rows.OrderBy(r => r.ClassOrder).ThenBy(r => r.ClassName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.SectionName, StringComparer.OrdinalIgnoreCase)
                                      .ThenBy(r => r.TermSequence).ThenBy(r => r.ExamSequence).ThenBy(r => r.DisplayOrder).ThenBy(r => r.SubjectName, StringComparer.OrdinalIgnoreCase))
                {
                    var isClassTeacher = classTeacherOf.Contains(r.SectionId);
                    var rights = new ReportRights(caller.IsAdmin, caller.IsPrincipal, isClassTeacher, teaches.Contains((r.SectionId, r.SubjectId)), mapped.Contains(new MappedPair(r.ClassId, r.SubjectId)));
                    if (!rights.CanView) continue;
                    statuses.TryGetValue((r.ExamSubjectId, r.SectionId), out var st);
                    list.Add(new MarksContextRowDto
                    {
                        SchemeId = r.SchemeId, SchemeName = r.SchemeName, EvaluationMode = r.EvaluationMode, TermId = r.TermId, TermName = r.TermName, ExamId = r.ExamId, ExamName = r.ExamName,
                        ExamSubjectId = r.ExamSubjectId, SubjectId = r.SubjectId, SubjectName = r.SubjectName, ClassId = r.ClassId, ClassName = r.ClassName, SectionId = r.SectionId, SectionName = r.SectionName,
                        EntryMode = r.EntryMode, MaxMarks = r.MaxMarks, SkillCount = r.EntryMode == "GRADE" ? skillCounts.GetValueOrDefault(r.SchemeSubjectId) : 0,
                        Status = st?.Status ?? "NOT_STARTED", Locked = locks.Contains((r.TermId, r.SectionId)), CanEnter = rights.CanEnter, CanReview = rights.CanReview
                    });
                }
                return Result(200, "Successfully fetched", new MarksContextDto { AcademicYearId = year.AcademicYearId, AcademicYearName = year.YearName, Rows = list });
            }
            catch (Exception ex) { return Error<MarksContextDto>(ex, "listing what a user may enter"); }
        }

        // ============================================================================ the grid
        private sealed class Ctx
        {
            public int Active;
            public ExamSubjectInfo Es = null!;
            public SectionInfo Section = null!;
            public ReportRights Rights = null!;
        }

        private async Task<(Ctx? Ctx, ApiResponse<T>? Failure)> LoadAsync<T>(string schoolId, ReportCaller caller, long examSubjectId, long sectionId, CancellationToken ct)
        {
            var active = await _repo.ActiveStatusIdAsync(ct);
            if (active == null) return (null, Result<T>(500, "The system is not configured correctly. Please contact support.", default));
            var es = await _repo.GetExamSubjectAsync(schoolId, examSubjectId, ct);
            if (es == null || es.SchemeStatusId != active.Value) return (null, Result<T>(404, "Exam subject not found.", default));
            var section = await _repo.GetSectionAsync(schoolId, sectionId, active.Value, ct);
            if (section == null || section.ClassId != es.ClassId) return (null, Result<T>(404, "Section not found for this exam subject.", default));
            var rights = await _access.RightsAsync(schoolId, caller, es.AcademicYearId, es.ClassId, section.SectionId, es.SubjectId, active.Value, ct);
            if (!rights.CanView) return (null, Result<T>(403, "You can't see the marks of this section and subject.", default));
            return (new Ctx { Active = active.Value, Es = es, Section = section, Rights = rights }, null);
        }

        public async Task<ApiResponse<MarksGridDto>> GetGridAsync(string? schoolId, ReportCaller caller, long examSubjectId, long sectionId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<MarksGridDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var (c, fail) = await LoadAsync<MarksGridDto>(schoolId, caller, examSubjectId, sectionId, ct);
                if (c == null) return fail!;
                return Result(200, "Successfully fetched", await BuildGridAsync(schoolId, caller, c, ct));
            }
            catch (Exception ex) { return Error<MarksGridDto>(ex, "fetching a marks grid"); }
        }

        private async Task<MarksGridDto> BuildGridAsync(string schoolId, ReportCaller caller, Ctx c, CancellationToken ct)
        {
            var es = c.Es;
            var roster = await _repo.GetRosterAsync(schoolId, es.AcademicYearId, c.Section.SectionId, c.Active, ct);
            var skills = es.EntryMode == "GRADE" ? await _repo.GetSkillsAsync(es.SchemeSubjectId, ct) : new List<SkillInfo>();
            var marks = await _repo.GetMarksAsync(schoolId, es.ExamSubjectId, c.Section.SectionId, roster.Select(r => r.StudentId).ToList(), false, ct);
            var status = await _repo.GetStatusAsync(es.ExamSubjectId, c.Section.SectionId, false, ct);
            var termSection = await _repo.GetTermSectionAsync(es.TermId, c.Section.SectionId, false, ct);
            var remarks = (await _repo.GetSubjectRemarksAsync(es.TermId, es.SchemeSubjectId, roster.Select(r => r.StudentId).ToList(), false, ct)).ToDictionary(r => r.StudentId, r => r.Remark);
            var bands = es.EntryMode == "GRADE" && es.GradeScaleId != null ? await _repo.GetBandsAsync(schoolId, new[] { es.GradeScaleId.Value }, ct) : new List<BandInfo>();

            var rosterIds = roster.Select(r => r.StudentId).ToHashSet();
            var leftIds = marks.Select(m => m.StudentId).Where(id => !rosterIds.Contains(id)).Distinct().ToList();
            var left = await _repo.GetStudentsAsync(schoolId, leftIds, ct);
            var byStudent = marks.GroupBy(m => m.StudentId).ToDictionary(g => g.Key, g => g.ToList());
            var cellSkills = skills.Count > 0 ? skills.Select(s => (long?)s.SkillId).ToList() : new List<long?> { null };

            GridStudentDto Row(RosterStudent s, bool onRoster)
            {
                var own = byStudent.GetValueOrDefault(s.StudentId) ?? new List<TbReportMark>();
                return new GridStudentDto
                {
                    StudentId = s.StudentId, AdmissionNumber = s.AdmissionNumber, RollNumber = s.RollNumber ?? "", FullName = LeaveRules.FullName(s.FirstName, s.MiddleName, s.LastName), OnRoster = onRoster,
                    Remark = remarks.GetValueOrDefault(s.StudentId),
                    Cells = cellSkills.Select(sk =>
                    {
                        var m = own.FirstOrDefault(x => x.SkillId == sk);
                        return new GridCellDto { SkillId = sk, State = m?.EntryState, Marks = m?.Marks, GradeCode = m?.GradeCode };
                    }).ToList()
                };
            }
            var students = roster.OrderBy(r => r.RollNumber, RollComparer.Instance).ThenBy(r => r.FirstName, StringComparer.OrdinalIgnoreCase).Select(r => Row(r, true)).ToList();
            students.AddRange(left.OrderBy(r => r.FirstName, StringComparer.OrdinalIgnoreCase).Select(r => Row(r, false)));

            var rosterCells = students.Where(s => s.OnRoster).SelectMany(s => s.Cells).ToList();
            var locked = termSection?.Status == "LOCKED";
            var st = status?.Status ?? Draft;
            var schemeLive = es.SchemeStatusId == c.Active;
            var canEdit = c.Rights.CanEnter && !locked && st == Draft && schemeLive;
            var canAccept = c.Rights.CanReview && !locked && st == Submitted;
            var canReopen = !locked && ((st == Submitted && (c.Rights.CanReview || (status?.SubmittedBy == caller.UserId && c.Rights.CanEnter))) || (st == Accepted && c.Rights.CanReview));
            string? why = null;
            if (locked) why = LockedMessage;
            else if (!c.Rights.CanEnter && !c.Rights.CanReview) why = caller.IsPrincipal ? "The Principal can read marks but not enter them." : "You can view this grid but you are not allowed to enter marks in it.";
            else if (st == Submitted) why = "Submitted and waiting to be accepted. Reopen it (with a reason) to change anything.";
            else if (st == Accepted) why = "Accepted. Reopen it (with a reason) to change anything.";
            else if (!c.Rights.CanEnter) why = "You can review this grid but not enter marks in it.";

            return new MarksGridDto
            {
                ExamSubjectId = es.ExamSubjectId, SectionId = c.Section.SectionId, SchemeId = es.SchemeId, SchemeName = es.SchemeName, TermName = es.TermName, ExamName = es.ExamName,
                SubjectName = es.LabelOverride ?? es.SubjectName, ClassName = c.Section.ClassName, SectionName = c.Section.SectionName, EntryMode = es.EntryMode, MaxMarks = es.MaxMarks,
                Skills = skills.Select(k => new GridSkillDto { SkillId = k.SkillId, Name = k.Name }).ToList(),
                GradeOptions = bands.Select(b => new GradeOptionDto { Code = b.Code, Label = b.Label }).ToList(),
                CanEditRemarks = (c.Rights.CanEnter || c.Rights.IsClassTeacher) && !locked && schemeLive,
                Status = st, Version = status?.Version ?? 0, Locked = locked, CanEdit = canEdit, CanSubmit = canEdit && status != null, CanAccept = canAccept, CanReopen = canReopen,
                ReadOnlyReason = canEdit ? null : why, SubmittedAt = status?.SubmittedAt, AcceptedAt = status?.AcceptedAt,
                Counts = new GridCountsDto
                {
                    Students = roster.Count, Cells = rosterCells.Count, Entered = rosterCells.Count(x => x.State == "ENTERED"), Absent = rosterCells.Count(x => x.State == "ABSENT"),
                    Exempt = rosterCells.Count(x => x.State == "EXEMPT"), Missing = rosterCells.Count(x => x.State == null)
                },
                Students = students
            };
        }

        // ============================================================================ draft save
        public async Task<ApiResponse<MarksGridDto>> SaveDraftAsync(string? schoolId, ReportCaller caller, SaveGridRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<MarksGridDto>(400, TenantMessages.NoSchool, null);
            try
            {
                // remarks are checked FIRST, so a bad remark never leaves the marks saved and the page showing an error
                if (req.Remarks is { Count: > 0 })
                {
                    var (c0, fail0) = await LoadAsync<MarksGridDto>(schoolId, caller, req.ExamSubjectId!.Value, req.SectionId!.Value, ct);
                    if (c0 == null) return fail0!;
                    var problem = await CheckRemarksAsync(schoolId, c0, req.Remarks, ct);
                    if (problem != null) return problem;
                }
                var saved = await SaveInternalAsync(schoolId, caller, req.ExamSubjectId!.Value, req.SectionId!.Value, req.Version, req.Entries, ct);
                if (!saved.Status || req.Remarks is not { Count: > 0 }) return saved;
                var again = await SaveRemarksAsync(schoolId, caller, new SaveRemarksRequest { ExamSubjectId = req.ExamSubjectId, SectionId = req.SectionId, Remarks = req.Remarks }, ct);
                return again;
            }
            catch (Exception ex) { return Error<MarksGridDto>(ex, "saving a marks grid"); }
        }

        // ============================================================================ per-subject remarks
        private const int MaxRemarkLength = 200;

        /// <summary>Null when the remarks may be saved now, else the failure to return. Same rule as marks entry (union rule), or the class teacher; never once the term-section is locked.</summary>
        private async Task<ApiResponse<MarksGridDto>?> CheckRemarksAsync(string schoolId, Ctx c, List<RemarkEntry> remarks, CancellationToken ct)
        {
            if (!(c.Rights.CanEnter || c.Rights.IsClassTeacher)) return Result<MarksGridDto>(403, c.Rights.IsPrincipal ? "The Principal can read remarks but not write them." : "You can't write remarks for this section and subject.", null);
            if (c.Es.SchemeStatusId != c.Active) return Result<MarksGridDto>(404, "Exam subject not found.", null);
            var ts = await _repo.GetTermSectionAsync(c.Es.TermId, c.Section.SectionId, false, ct);
            if (ts?.Status == "LOCKED") return Result<MarksGridDto>(409, LockedMessage, null);
            var roster = (await _repo.GetRosterAsync(schoolId, c.Es.AcademicYearId, c.Section.SectionId, c.Active, ct)).ToDictionary(r => r.StudentId, r => LeaveRules.FullName(r.FirstName, r.MiddleName, r.LastName));
            var problems = new List<string>();
            foreach (var r in remarks)
            {
                var id = (r.StudentId ?? "").Trim();
                if (!roster.TryGetValue(id, out var name)) { problems.Add($"{id}: this student is not on the active roster of the section."); continue; }
                if ((r.Remark ?? "").Trim().Length > MaxRemarkLength) problems.Add($"{name}: the remark is longer than {MaxRemarkLength} characters.");
            }
            if (remarks.Select(r => (r.StudentId ?? "").Trim()).Distinct().Count() != remarks.Count) problems.Add("A student is listed twice.");
            return problems.Count == 0 ? null : Result<MarksGridDto>(400, string.Join(" ", problems.Take(MaxIssues)) + (problems.Count > MaxIssues ? $" And {problems.Count - MaxIssues} more." : ""), null);
        }

        public async Task<ApiResponse<MarksGridDto>> SaveRemarksAsync(string? schoolId, ReportCaller caller, SaveRemarksRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<MarksGridDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var (c, fail) = await LoadAsync<MarksGridDto>(schoolId, caller, req.ExamSubjectId!.Value, req.SectionId!.Value, ct);
                if (c == null) return fail!;
                var problem = await CheckRemarksAsync(schoolId, c, req.Remarks, ct);
                if (problem != null) return problem;

                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockTermSectionKeyAsync(c.Es.TermId, c.Section.SectionId, ct);        // serialises with a lock: a remark never slips in after the term was locked
                var ts = await _repo.GetTermSectionAsync(c.Es.TermId, c.Section.SectionId, false, ct);
                if (ts?.Status == "LOCKED") return Result<MarksGridDto>(409, LockedMessage, null);
                var ids = req.Remarks.Select(r => r.StudentId.Trim()).ToList();
                var existing = (await _repo.GetSubjectRemarksAsync(c.Es.TermId, c.Es.SchemeSubjectId, ids, true, ct)).ToDictionary(r => r.StudentId);
                var now = DateTime.UtcNow;
                foreach (var r in req.Remarks)
                {
                    var id = r.StudentId.Trim(); var text = (r.Remark ?? "").Trim();
                    existing.TryGetValue(id, out var row);
                    if (text.Length == 0) { if (row != null) _repo.Remove(row); continue; }
                    if (row == null) _repo.Add(new TbReportSubjectRemark { TermId = c.Es.TermId, SchemeSubjectId = c.Es.SchemeSubjectId, StudentId = id, SchemeId = c.Es.SchemeId, SchoolId = schoolId, Remark = text, UpdatedBy = caller.UserId, CreatedAt = now, UpdatedAt = now });
                    else if (row.Remark != text) { row.Remark = text; row.UpdatedBy = caller.UserId; row.UpdatedAt = now; }
                }
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                _repo.ClearTracking();
                return Result(200, "Remarks saved.", await BuildGridAsync(schoolId, caller, c, ct));
            }
            catch (Exception ex) { return Error<MarksGridDto>(ex, "saving subject remarks"); }
        }

        private sealed class Validator
        {
            public string Mode = "MARKS";
            public decimal? Max;
            public Dictionary<long, string> SkillNames = new();
            public Dictionary<string, string> Codes = new(StringComparer.OrdinalIgnoreCase);   // typed code -> canonical code
            public Dictionary<string, string> Names = new();                                   // student id -> name (the roster)

            /// <summary>Null when the entry is fine, otherwise a sentence that names the student.</summary>
            public string? Check(GridEntryRequest e, out string state)
            {
                state = (e.State ?? "").Trim().ToUpperInvariant();
                var who = Names.TryGetValue(e.StudentId ?? "", out var n) ? n : e.StudentId ?? "?";
                if (!Names.ContainsKey(e.StudentId ?? "")) return $"{who}: this student is not on the active roster of the section for this academic year.";
                if (state is not ("ENTERED" or "ABSENT" or "EXEMPT" or "NONE")) return $"{who}: the state must be ENTERED, ABSENT, EXEMPT or NONE.";
                if (SkillNames.Count > 0) { if (e.SkillId == null || !SkillNames.ContainsKey(e.SkillId.Value)) return $"{who}: a skill is required and it must belong to this subject."; }
                else if (e.SkillId != null) return $"{who}: this subject has no skills.";
                var skill = e.SkillId != null ? $" ({SkillNames[e.SkillId.Value]})" : "";
                if (state == "NONE") return null;
                if (state is "ABSENT" or "EXEMPT")
                    return e.Marks != null || !string.IsNullOrWhiteSpace(e.GradeCode) ? $"{who}{skill}: an absent or exempt student has no marks or grade." : null;
                if (Mode == "MARKS")
                {
                    if (e.Marks == null) return $"{who}{skill}: enter the marks, or mark the student absent or exempt.";
                    if (!string.IsNullOrWhiteSpace(e.GradeCode)) return $"{who}{skill}: this exam subject takes marks, not a grade.";
                    if (e.Marks < 0) return $"{who}{skill}: marks cannot be negative.";
                    if (e.Marks > Max) return $"{who}{skill}: {e.Marks:0.##} is above the maximum of {Max:0.##}.";
                    if (decimal.Round(e.Marks.Value, 2) != e.Marks.Value) return $"{who}{skill}: marks can have at most two decimals.";
                    return null;
                }
                if (e.Marks != null) return $"{who}{skill}: this exam subject takes a grade, not marks.";
                if (string.IsNullOrWhiteSpace(e.GradeCode)) return $"{who}{skill}: choose a grade, or mark the student absent or exempt.";
                if (Codes.Count == 0) return $"{who}{skill}: the scheme has no grade scale to pick a grade from. Ask the School Admin to set one.";
                return Codes.ContainsKey(e.GradeCode.Trim()) ? null : $"{who}{skill}: '{e.GradeCode.Trim()}' is not a grade of the scheme's scale ({string.Join(", ", Codes.Values.Distinct())}).";
            }
        }

        private async Task<Validator> ValidatorAsync(string schoolId, Ctx c, List<RosterStudent> roster, List<SkillInfo> skills, CancellationToken ct)
        {
            var v = new Validator { Mode = c.Es.EntryMode, Max = c.Es.MaxMarks, Names = roster.ToDictionary(r => r.StudentId, r => LeaveRules.FullName(r.FirstName, r.MiddleName, r.LastName)), SkillNames = skills.ToDictionary(k => k.SkillId, k => k.Name) };
            if (c.Es.EntryMode == "GRADE" && c.Es.GradeScaleId != null)
                foreach (var b in await _repo.GetBandsAsync(schoolId, new[] { c.Es.GradeScaleId.Value }, ct)) v.Codes[b.Code] = b.Code;
            return v;
        }

        private async Task<ApiResponse<MarksGridDto>> SaveInternalAsync(string schoolId, ReportCaller caller, long examSubjectId, long sectionId, int version, List<GridEntryRequest> entries, CancellationToken ct)
        {
            var (c, fail) = await LoadAsync<MarksGridDto>(schoolId, caller, examSubjectId, sectionId, ct);
            if (c == null) return fail!;
            if (!c.Rights.CanEnter) return Result<MarksGridDto>(403, c.Rights.Mapped ? "You are not allowed to enter marks in this section and subject." : "This subject is not mapped to the class (Class-Subject Mapping), so marks can't be entered.", null);
            var es = c.Es;
            var roster = await _repo.GetRosterAsync(schoolId, es.AcademicYearId, sectionId, c.Active, ct);
            var skills = es.EntryMode == "GRADE" ? await _repo.GetSkillsAsync(es.SchemeSubjectId, ct) : new List<SkillInfo>();
            var v = await ValidatorAsync(schoolId, c, roster, skills, ct);

            var problems = new List<string>();
            var seen = new HashSet<(string, long?)>();
            var clean = new List<(GridEntryRequest E, string State)>();
            foreach (var e in entries)
            {
                var msg = v.Check(e, out var state);
                if (msg != null) { problems.Add(msg); continue; }
                if (!seen.Add((e.StudentId, e.SkillId))) { problems.Add($"{v.Names[e.StudentId]}: listed twice."); continue; }
                clean.Add((e, state));
            }
            if (problems.Count > 0) return Result<MarksGridDto>(400, Summarise(problems), null);

            await using var tx = await _repo.BeginTransactionAsync(ct);
            try
            {
                var status = await _repo.GetStatusAsync(es.ExamSubjectId, sectionId, true, ct);          // row lock: nobody else changes this grid until we finish
                var now = DateTime.UtcNow;
                if (status == null)
                {
                    if (version != 0) return Result<MarksGridDto>(409, StaleMessage, null);
                    if (clean.Count == 0) return Result(200, "Nothing to save.", await BuildGridAsync(schoolId, caller, c, ct));
                    status = new TbReportEntryStatus { ExamSubjectId = es.ExamSubjectId, SectionId = sectionId, ClassId = es.ClassId, SchoolId = schoolId, Status = Draft, Version = 1, CreatedAt = now, UpdatedAt = now };
                    _repo.Add(status);
                    await _repo.SaveChangesAsync(ct);                                                     // the grid row first: the entries point at it
                }
                else
                {
                    if (status.Version != version) return Result<MarksGridDto>(409, StaleMessage, null);
                    status.Version++; status.UpdatedAt = now;
                }
                // the lock is read AFTER the row lock, so a lock that committed while we waited is seen
                var ts = await _repo.GetTermSectionAsync(es.TermId, sectionId, false, ct);
                if (ts?.Status == "LOCKED") return Result<MarksGridDto>(409, LockedMessage, null);
                if (status.Status != Draft) return Result<MarksGridDto>(409, status.Status == Submitted ? "This grid was submitted. Reopen it (with a reason) before changing it." : "This grid was accepted. Reopen it (with a reason) before changing it.", null);

                var ids = clean.Select(x => x.E.StudentId).Distinct().ToList();
                var existing = (await _repo.GetMarksAsync(schoolId, es.ExamSubjectId, sectionId, ids, true, ct)).Where(m => ids.Contains(m.StudentId)).ToDictionary(m => (m.StudentId, m.SkillId));
                foreach (var (e, state) in clean)
                {
                    existing.TryGetValue((e.StudentId, e.SkillId), out var row);
                    if (state == "NONE") { if (row != null) _repo.Remove(row); continue; }
                    if (row == null) { row = new TbReportMark { SchoolId = schoolId, ExamSubjectId = es.ExamSubjectId, SchemeSubjectId = es.SchemeSubjectId, SkillId = e.SkillId, StudentId = e.StudentId, CreatedAt = now }; _repo.Add(row); }
                    row.SectionId = sectionId; row.EntryState = state; row.EnteredBy = caller.UserId; row.UpdatedAt = now;
                    row.Marks = state == "ENTERED" && es.EntryMode == "MARKS" ? decimal.Round(e.Marks!.Value, 2) : null;
                    row.GradeCode = state == "ENTERED" && es.EntryMode == "GRADE" ? v.Codes[e.GradeCode!.Trim()] : null;
                }
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when ((ex.InnerException as PostgresException)?.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                _logger.LogInformation(ex, "Two people created the same marks grid at once (exam subject {Es}, section {Sec}); the second is told to reload.", examSubjectId, sectionId);
                return Result<MarksGridDto>(409, StaleMessage, null);
            }
            _repo.ClearTracking();
            return Result(200, "Draft saved.", await BuildGridAsync(schoolId, caller, c, ct));
        }

        // ============================================================================ submit / accept / reopen
        public Task<ApiResponse<MarksGridDto>> SubmitAsync(string? schoolId, ReportCaller caller, GridActionRequest req, CancellationToken ct) => ActAsync(schoolId, caller, req, "SUBMIT", ct);
        public Task<ApiResponse<MarksGridDto>> AcceptAsync(string? schoolId, ReportCaller caller, GridActionRequest req, CancellationToken ct) => ActAsync(schoolId, caller, req, "ACCEPT", ct);
        public Task<ApiResponse<MarksGridDto>> ReopenAsync(string? schoolId, ReportCaller caller, GridActionRequest req, CancellationToken ct) => ActAsync(schoolId, caller, req, "REOPEN", ct);

        private async Task<ApiResponse<MarksGridDto>> ActAsync(string? schoolId, ReportCaller caller, GridActionRequest req, string action, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<MarksGridDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var reason = (req.Reason ?? "").Trim();
                if (action == "REOPEN" && reason.Length < 3) return Result<MarksGridDto>(400, "Say why you are reopening it (at least 3 characters). The reason is kept in the audit trail.", null);
                var (c, fail) = await LoadAsync<MarksGridDto>(schoolId, caller, req.ExamSubjectId!.Value, req.SectionId!.Value, ct);
                if (c == null) return fail!;
                var es = c.Es; var sectionId = c.Section.SectionId;

                await using var tx = await _repo.BeginTransactionAsync(ct);
                var status = await _repo.GetStatusAsync(es.ExamSubjectId, sectionId, true, ct);
                if (status == null) return Result<MarksGridDto>(409, action == "SUBMIT" ? "Nothing has been saved in this grid yet." : "This grid has not been started.", null);
                if (status.Version != req.Version) return Result<MarksGridDto>(409, StaleMessage, null);
                var ts = await _repo.GetTermSectionAsync(es.TermId, sectionId, false, ct);      // read after the row lock
                if (ts?.Status == "LOCKED") return Result<MarksGridDto>(409, LockedMessage, null);

                var from = status.Status; var now = DateTime.UtcNow;
                switch (action)
                {
                    case "SUBMIT":
                    {
                        if (!c.Rights.CanEnter) return Result<MarksGridDto>(403, "You are not allowed to submit marks for this section and subject.", null);
                        if (from != Draft) return Result<MarksGridDto>(409, "Only a draft can be submitted.", null);
                        var missing = await MissingAsync(schoolId, c, ct);
                        if (missing.Count > 0) return Result<MarksGridDto>(409, missing.Count == 1 && missing[0] == "(no students)" ? "This section has no students to enter marks for." : $"Every student needs marks, absent or exempt before this can be submitted. Still missing: {Names(missing)}.", null);
                        status.Status = Submitted; status.SubmittedBy = caller.UserId; status.SubmittedAt = now; status.AcceptedBy = null; status.AcceptedAt = null;
                        break;
                    }
                    case "ACCEPT":
                    {
                        if (!c.Rights.CanReview) return Result<MarksGridDto>(403, "Only the School Admin or the section's class teacher can accept a grid.", null);
                        if (from != Submitted) return Result<MarksGridDto>(409, "Only a submitted grid can be accepted.", null);
                        var missing = await MissingAsync(schoolId, c, ct);
                        if (missing.Count > 0) return Result<MarksGridDto>(409, $"Students were added to the section after this was submitted and still have nothing recorded: {Names(missing)}. Reopen it, fill them in and submit again.", null);
                        status.Status = Accepted; status.AcceptedBy = caller.UserId; status.AcceptedAt = now;
                        break;
                    }
                    default:
                    {
                        if (from == Draft) return Result<MarksGridDto>(409, "A draft is already open for changes.", null);
                        var allowed = from == Submitted ? (c.Rights.CanReview || (status.SubmittedBy == caller.UserId && c.Rights.CanEnter)) : c.Rights.CanReview;
                        if (!allowed) return Result<MarksGridDto>(403, from == Submitted ? "A submitted grid can be reopened by whoever submitted it, the class teacher or the School Admin." : "An accepted grid can only be reopened by the section's class teacher or the School Admin.", null);
                        status.Status = Draft; status.SubmittedBy = null; status.SubmittedAt = null; status.AcceptedBy = null; status.AcceptedAt = null;
                        break;
                    }
                }
                status.Version++; status.UpdatedAt = now;
                _repo.Add(new TbReportAudit { SchoolId = schoolId, Event = action, SchemeId = es.SchemeId, TermId = es.TermId, ExamSubjectId = es.ExamSubjectId, SectionId = sectionId, FromStatus = from, ToStatus = status.Status, Reason = action == "REOPEN" ? reason : null, ActorUserId = caller.UserId, CreatedAt = now });
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                _repo.ClearTracking();
                return Result(200, action == "SUBMIT" ? "Submitted." : action == "ACCEPT" ? "Accepted." : "Reopened.", await BuildGridAsync(schoolId, caller, c, ct));
            }
            catch (Exception ex) { return Error<MarksGridDto>(ex, $"{action.ToLowerInvariant()} on a marks grid"); }
        }

        /// <summary>Roster students (with a skill, per skill) that have nothing recorded.</summary>
        private async Task<List<string>> MissingAsync(string schoolId, Ctx c, CancellationToken ct)
        {
            var es = c.Es;
            var roster = await _repo.GetRosterAsync(schoolId, es.AcademicYearId, c.Section.SectionId, c.Active, ct);
            if (roster.Count == 0) return new List<string> { "(no students)" };
            var skills = es.EntryMode == "GRADE" ? await _repo.GetSkillsAsync(es.SchemeSubjectId, ct) : new List<SkillInfo>();
            var marks = await _repo.GetMarksAsync(schoolId, es.ExamSubjectId, c.Section.SectionId, roster.Select(r => r.StudentId).ToList(), false, ct);
            var have = marks.Select(m => (m.StudentId, m.SkillId)).ToHashSet();
            var need = skills.Count > 0 ? skills.Select(s => (long?)s.SkillId).ToList() : new List<long?> { null };
            return roster.Where(r => need.Any(sk => !have.Contains((r.StudentId, sk)))).OrderBy(r => r.RollNumber, RollComparer.Instance)
                .Select(r => LeaveRules.FullName(r.FirstName, r.MiddleName, r.LastName)).ToList();
        }

        // ============================================================================ Excel
        public async Task<ReportFileResult> TemplateAsync(string? schoolId, ReportCaller caller, long examSubjectId, long sectionId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return FileFail(400, TenantMessages.NoSchool);
            try
            {
                var (c, fail) = await LoadAsync<MarksGridDto>(schoolId, caller, examSubjectId, sectionId, ct);
                if (c == null) return FileFail(fail!.StatusCode, fail.Message);
                if (!c.Rights.CanEnter) return FileFail(403, "You are not allowed to enter marks here, so there is no template for you.");
                var es = c.Es;
                var roster = (await _repo.GetRosterAsync(schoolId, es.AcademicYearId, sectionId, c.Active, ct)).OrderBy(r => r.RollNumber, RollComparer.Instance).ThenBy(r => r.FirstName, StringComparer.OrdinalIgnoreCase).ToList();
                var skills = es.EntryMode == "GRADE" ? await _repo.GetSkillsAsync(es.SchemeSubjectId, ct) : new List<SkillInfo>();
                var bands = es.EntryMode == "GRADE" && es.GradeScaleId != null ? await _repo.GetBandsAsync(schoolId, new[] { es.GradeScaleId.Value }, ct) : new List<BandInfo>();

                using var wb = new XLWorkbook();
                var ins = wb.Worksheets.Add("Instructions");
                var ws = wb.Worksheets.Add(SheetName);
                var info = wb.Worksheets.Add("Info");
                var lists = wb.Worksheets.Add("Lists");

                var valueCols = skills.Count > 0 ? skills.Select(s => s.Name).ToList() : new List<string> { es.EntryMode == "MARKS" ? $"Marks (out of {es.MaxMarks:0.##})" : "Grade" };
                var heads = new List<string> { "Student ID", "Admission No", "Roll", "Name" }; heads.AddRange(valueCols);
                for (var i = 0; i < heads.Count; i++)
                {
                    var h = ws.Cell(1, i + 1); ExcelSafeText.Put(h, heads[i]);
                    h.Style.Font.Bold = true; h.Style.Font.FontColor = XLColor.White; h.Style.Fill.BackgroundColor = XLColor.FromHtml(i < 4 ? "#6B7280" : "#7C5CFC"); h.Style.Alignment.WrapText = true;
                }
                ws.Column(1).Width = 14; ws.Column(2).Width = 16; ws.Column(3).Width = 8; ws.Column(4).Width = 30;
                for (var i = 5; i <= heads.Count; i++) ws.Column(i).Width = 18;
                ws.SheetView.FreezeRows(1);
                for (var r = 0; r < roster.Count; r++)
                {
                    var s = roster[r];
                    ExcelSafeText.Put(ws.Cell(r + 2, 1), s.StudentId); ExcelSafeText.Put(ws.Cell(r + 2, 2), s.AdmissionNumber); ExcelSafeText.Put(ws.Cell(r + 2, 3), s.RollNumber ?? "");
                    ExcelSafeText.Put(ws.Cell(r + 2, 4), LeaveRules.FullName(s.FirstName, s.MiddleName, s.LastName));
                    for (var k = 5; k <= heads.Count; k++) ws.Cell(r + 2, k).Style.NumberFormat.Format = "@";
                }
                if (bands.Count > 0)
                {
                    lists.Cell(1, 1).Value = "Grades";
                    var codes = bands.Select(b => b.Code).Concat(new[] { "AB", "EX" }).ToList();
                    for (var i = 0; i < codes.Count; i++) lists.Cell(i + 2, 1).Value = codes[i];
                    wb.NamedRanges.Add("GradeList", lists.Range(2, 1, codes.Count + 1, 1));
                    var dv = ws.Range(2, 5, Math.Max(2, roster.Count + 1), heads.Count).CreateDataValidation();
                    dv.List("GradeList", true); dv.IgnoreBlanks = true; dv.ShowErrorMessage = true; dv.ErrorTitle = "Pick from the list"; dv.ErrorMessage = "Pick a grade, AB (absent) or EX (exempt).";
                }
                // the identity columns are read-only (sheet protection without a password: it stops accidents, not people; the server re-checks every id anyway)
                ws.Range(2, 5, Math.Max(2, roster.Count + 1), heads.Count).Style.Protection.SetLocked(false);
                ws.Protect();

                info.Cell(1, 1).Value = InfoTag; info.Cell(2, 1).Value = es.ExamSubjectId; info.Cell(3, 1).Value = sectionId; info.Cell(4, 1).Value = es.EntryMode;
                info.Cell(5, 1).Value = string.Join(",", skills.Select(s => s.SkillId));
                info.Visibility = XLWorksheetVisibility.VeryHidden;

                var lines = new List<(string, bool)>
                {
                    ($"{es.SchemeName} / {es.TermName} / {es.ExamName}", true),
                    ($"{es.LabelOverride ?? es.SubjectName}: {c.Section.ClassName} {c.Section.SectionName}", true), ("", false),
                    ("Fill only the purple columns of the Marks sheet. The grey columns (student, admission number, roll, name) are read-only and are how the system finds each student.", false),
                    (es.EntryMode == "MARKS" ? $"Type marks from 0 to {es.MaxMarks:0.##}, with at most two decimals." : "Pick a grade from the list.", false),
                    ("Type AB for a student who was absent and EX for a student who is exempt. Leave a cell empty if it is not known yet.", false),
                    ("Uploading shows you what would change; nothing is submitted. You review the grid, save the draft and submit it yourself.", false),
                    ("Do not rename the sheets or add formulas: formulas are not read.", false),
                };
                for (var i = 0; i < lines.Count; i++) { var cell = ins.Cell(i + 1, 1); ExcelSafeText.Put(cell, lines[i].Item1); cell.Style.Font.Bold = lines[i].Item2; cell.Style.Alignment.WrapText = true; }
                ins.Column(1).Width = 110;
                ws.SetTabActive();
                using var ms = new MemoryStream(); wb.SaveAs(ms);
                return new ReportFileResult { Content = ms.ToArray(), FileName = SafeFile($"marks-{c.Section.ClassName}-{c.Section.SectionName}-{es.ExamName}-{es.LabelOverride ?? es.SubjectName}") + ".xlsx" };
            }
            catch (Exception ex) { _logger.LogError(ex, "Error while building a marks template"); return FileFail(500, "Something went wrong. Please try again later."); }
        }

        private const string SheetName = "Marks", InfoTag = "smss-marks-v1";

        public async Task<ApiResponse<UploadPreviewDto>> UploadAsync(string? schoolId, ReportCaller caller, long examSubjectId, long sectionId, int version, bool apply, byte[] file, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<UploadPreviewDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var bad = ExcelFileGuard.Inspect(file);
                if (bad != null) return Result<UploadPreviewDto>(400, bad, null);
                var (c, fail) = await LoadAsync<UploadPreviewDto>(schoolId, caller, examSubjectId, sectionId, ct);
                if (c == null) return fail!;
                if (!c.Rights.CanEnter) return Result<UploadPreviewDto>(403, "You are not allowed to enter marks in this section and subject.", null);
                var es = c.Es;
                var roster = await _repo.GetRosterAsync(schoolId, es.AcademicYearId, sectionId, c.Active, ct);
                var skills = es.EntryMode == "GRADE" ? await _repo.GetSkillsAsync(es.SchemeSubjectId, ct) : new List<SkillInfo>();
                var v = await ValidatorAsync(schoolId, c, roster, skills, ct);
                var preview = new UploadPreviewDto();
                var admissionOf = roster.ToDictionary(r => r.StudentId, r => r.AdmissionNumber);

                XLWorkbook wb;
                try { wb = new XLWorkbook(new MemoryStream(file, false)); }
                catch (Exception) { return Result<UploadPreviewDto>(400, "The file could not be read as an Excel workbook. Download the template again and fill that in.", null); }
                using (wb)
                {
                    var info = wb.Worksheets.FirstOrDefault(w => w.Name.Equals("Info", StringComparison.OrdinalIgnoreCase));
                    var ws = wb.Worksheets.FirstOrDefault(w => w.Name.Equals(SheetName, StringComparison.OrdinalIgnoreCase));
                    if (info == null || ws == null || ReadText(info.Cell(1, 1)) != InfoTag) return Result<UploadPreviewDto>(400, "That is not a marks template. Download the template for this exam subject and section, and fill that in.", null);
                    if (ReadText(info.Cell(2, 1)) != es.ExamSubjectId.ToString(CultureInfo.InvariantCulture) || ReadText(info.Cell(3, 1)) != sectionId.ToString(CultureInfo.InvariantCulture))
                        return Result<UploadPreviewDto>(400, "This file belongs to a different exam subject or section. Download the template for the one you are working on.", null);
                    var colSkills = ReadText(info.Cell(5, 1)).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(long.Parse).ToList();
                    var expected = skills.Select(s => s.SkillId).ToList();
                    if (!colSkills.SequenceEqual(expected)) return Result<UploadPreviewDto>(400, "The skills of this subject have changed since the template was made. Download a fresh template.", null);

                    var lastRow = ws.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? 1;
                    if (lastRow > 2001) return Result<UploadPreviewDto>(400, "The sheet has too many rows.", null);
                    var seenIds = new HashSet<string>();
                    var width = Math.Max(1, expected.Count);
                    for (var r = 2; r <= lastRow; r++)
                    {
                        var idCell = ws.Cell(r, 1);
                        if (idCell.HasFormula) { preview.Issues.Add(new UploadIssueDto { Row = r, Message = "This row contains a formula. Formulas are not read: type the value." }); continue; }
                        var id = ExcelFileGuard.Unneutralize(ReadText(idCell));
                        var values = Enumerable.Range(0, width).Select(i => ws.Cell(r, 5 + i)).ToList();
                        if (id.Length == 0 && values.All(x => x.IsEmpty())) continue;
                        preview.Rows++;
                        if (id.Length == 0) { preview.Issues.Add(new UploadIssueDto { Row = r, Message = "The student ID is missing. Use the template and do not change the grey columns." }); continue; }
                        if (!admissionOf.TryGetValue(id, out var adm)) { preview.Issues.Add(new UploadIssueDto { Row = r, Message = $"Student {Clip(id)} is not on this section's active roster. Download a fresh template." }); continue; }
                        if (!seenIds.Add(id)) { preview.Issues.Add(new UploadIssueDto { Row = r, Message = $"{v.Names[id]} appears more than once." }); continue; }
                        var shownAdm = ExcelFileGuard.Unneutralize(ReadText(ws.Cell(r, 2)));
                        if (shownAdm.Length > 0 && !string.Equals(shownAdm, adm, StringComparison.Ordinal)) { preview.Issues.Add(new UploadIssueDto { Row = r, Message = $"{v.Names[id]}: the admission number in the sheet does not match this student. The grey columns were changed." }); continue; }

                        for (var i = 0; i < width; i++)
                        {
                            var cell = values[i];
                            if (cell.HasFormula) { preview.Issues.Add(new UploadIssueDto { Row = r, Message = $"{v.Names[id]}: a formula was found. Formulas are not read: type the value." }); continue; }
                            var text = ExcelFileGuard.Unneutralize(ReadText(cell)).Trim();
                            if (text.Length == 0) continue;
                            var skillId = expected.Count > 0 ? (long?)expected[i] : null;
                            var entry = new GridEntryRequest { StudentId = id, SkillId = skillId };
                            if (text.Equals("AB", StringComparison.OrdinalIgnoreCase) || text.Equals("ABSENT", StringComparison.OrdinalIgnoreCase)) entry.State = "ABSENT";
                            else if (text.Equals("EX", StringComparison.OrdinalIgnoreCase) || text.Equals("EXEMPT", StringComparison.OrdinalIgnoreCase)) entry.State = "EXEMPT";
                            else if (es.EntryMode == "MARKS")
                            {
                                if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var m))
                                { preview.Issues.Add(new UploadIssueDto { Row = r, Message = $"{v.Names[id]}: '{Clip(text)}' is not a number, AB or EX." }); continue; }
                                entry.State = "ENTERED"; entry.Marks = m;
                            }
                            else { entry.State = "ENTERED"; entry.GradeCode = text; }
                            var msg = v.Check(entry, out _);
                            if (msg != null) { preview.Issues.Add(new UploadIssueDto { Row = r, Message = msg }); continue; }
                            if (entry.GradeCode != null) entry.GradeCode = v.Codes[entry.GradeCode.Trim()];
                            preview.Entries.Add(entry); preview.Cells++;
                        }
                    }
                }
                if (preview.Rows == 0 && preview.Issues.Count == 0) preview.Issues.Add(new UploadIssueDto { Row = 0, Message = "The sheet has no rows to read." });
                if (!apply) return Result(200, preview.Issues.Count == 0 ? "The file is ready to fill into the grid." : "The file has problems to fix first.", preview);
                if (preview.Issues.Count > 0) return Result(422, $"The file has {preview.Issues.Count} problem(s). Nothing was saved.", preview);
                var saved = await SaveInternalAsync(schoolId, caller, examSubjectId, sectionId, version, preview.Entries, ct);
                if (!saved.Status) return Result<UploadPreviewDto>(saved.StatusCode, saved.Message, preview);
                preview.Applied = true; preview.Grid = saved.Data;
                return Result(200, "The grid was filled from the file as a draft. Review it, then submit.", preview);
            }
            catch (Exception ex) { return Error<UploadPreviewDto>(ex, "reading an uploaded marks sheet"); }
        }

        private static string ReadText(IXLCell cell)
        {
            if (cell.HasFormula) return "";
            var val = cell.Value;
            if (val.IsBlank) return "";
            if (val.IsNumber) { var n = val.GetNumber(); return n == Math.Floor(n) && Math.Abs(n) < 1e15 ? ((long)n).ToString(CultureInfo.InvariantCulture) : n.ToString("R", CultureInfo.InvariantCulture); }
            if (val.IsText) return val.GetText();
            if (val.IsBoolean) return val.GetBoolean() ? "TRUE" : "FALSE";
            return "";
        }

        // ============================================================================ small helpers
        private static string Summarise(List<string> problems) =>
            problems.Count <= MaxIssues ? string.Join(" ", problems) : string.Join(" ", problems.Take(MaxIssues)) + $" … and {problems.Count - MaxIssues} more.";
        private static string Names(List<string> names) => names.Count <= 8 ? string.Join(", ", names) : string.Join(", ", names.Take(8)) + $" and {names.Count - 8} more";
        private static string Clip(string s) => s.Length > 30 ? s[..30] + "…" : s;
        private static string SafeFile(string s) { var bad = Path.GetInvalidFileNameChars(); var t = new string(s.Select(ch => bad.Contains(ch) || ch == ' ' ? '-' : ch).ToArray()); return t.Length > 80 ? t[..80] : t; }
        private static ReportFileResult FileFail(int code, string message) => new() { StatusCode = code, Message = message };
        private static ApiResponse<T> Result<T>(int code, string message, T? data) => new() { Status = code < 400, StatusCode = code, Message = message, Data = data };
        private ApiResponse<T> Error<T>(Exception ex, string action) { _logger.LogError(ex, "Error while {Action}", action); return Result<T>(500, "Something went wrong. Please try again later.", default); }
    }
}
