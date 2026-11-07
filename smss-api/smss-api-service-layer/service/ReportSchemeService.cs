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
    /// <summary>The real guard (Stage 2): asks the database what has been entered, so only the parts that have entries are protected.</summary>
    public class ReportEntryGuard : IReportEntryGuard
    {
        private readonly IReportMarksRepository _repo;
        public ReportEntryGuard(IReportMarksRepository repo) { _repo = repo; }
        public async Task<bool> HasEntriesAsync(string schoolId, long schemeId, CancellationToken ct) => (await _repo.GetFootprintAsync(schoolId, schemeId, ct)).Any;
        public Task<EntryFootprint> FootprintAsync(string schoolId, long schemeId, CancellationToken ct) => _repo.GetFootprintAsync(schoolId, schemeId, ct);
    }

    /// <summary>Report schemes: header, classes, terms and exams, subjects, the exam x subject matrix, skills, presets and clone. Stage 1: setup only.</summary>
    public class ReportSchemeService : IReportSchemeService
    {
        private const int Gap = 1000;   // sequence numbers are moved out of the way first so a reorder never trips the unique keys halfway
        private readonly IReportSetupRepository _repo;
        private readonly IReportEntryGuard _guard;
        private readonly ILogger<ReportSchemeService> _logger;

        public ReportSchemeService(IReportSetupRepository repo, IReportEntryGuard guard, ILogger<ReportSchemeService> logger) { _repo = repo; _guard = guard; _logger = logger; }

        // ================================================================== reads
        public async Task<ApiResponse<List<SchemeListDto>>> ListAsync(string? schoolId, long? academicYearId, bool includeInactive, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<SchemeListDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await ActiveAsync(ct);
                if (active == null) return ConfigError<List<SchemeListDto>>();
                var rows = await _repo.ListSchemesAsync(schoolId, academicYearId, includeInactive, active.Value, ct);
                var classRows = new Dictionary<long, List<long>>();
                foreach (var r in rows)
                {
                    var g = await _repo.LoadGraphAsync(schoolId, r.Scheme.SchemeId, false, ct);   // a handful of schemes per school: one small read each
                    classRows[r.Scheme.SchemeId] = g!.Classes.Select(c => c.ClassId).ToList();
                }
                var names = await _repo.ClassNamesAsync(schoolId, classRows.Values.SelectMany(x => x).Distinct().ToList(), active.Value, ct);
                return Result(200, "Successfully fetched", rows.Select(r => ToList(r, classRows[r.Scheme.SchemeId], names)).ToList());
            }
            catch (Exception ex) { return Error<List<SchemeListDto>>(ex, "listing report schemes"); }
        }

        public async Task<ApiResponse<SchemeDto>> GetAsync(string? schoolId, long schemeId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<SchemeDto>(400, TenantMessages.NoSchool, null);
            try { return await DetailAsync(schoolId, schemeId, 200, "Successfully fetched", null, ct); }
            catch (Exception ex) { return Error<SchemeDto>(ex, "fetching a report scheme"); }
        }

        public ApiResponse<List<ReportTemplateDto>> Templates() => Result(200, "Successfully fetched", ReportCardTemplates.All.Select(ReportCardTemplates.ToDto).ToList())!;

        public ApiResponse<List<SchemePresetDto>> Presets() => Result(200, "Successfully fetched", ReportPresets.Schemes.Select(p => new SchemePresetDto
        {
            Code = p.Code, Name = p.Name, Description = p.Description, EvaluationMode = p.Mode, TemplateKey = p.TemplateKey, ScalePresetCode = p.ScalePresetCode,
            ScaleName = ReportPresets.FindScale(p.ScalePresetCode)!.Name, ShowRank = p.ShowRank, EntryMode = p.EntryMode, Skills = p.Skills.ToList(),
            Terms = p.Terms.Select(t => new PresetTermDto { Name = t.Name, Exams = t.Exams.Select(e => new PresetExamDto { Name = e.Name, MaxMarks = e.Max, Weight = e.Weight }).ToList() }).ToList()
        }).ToList())!;

        // ================================================================== header
        public async Task<ApiResponse<SchemeDto>> CreateAsync(string? schoolId, long userId, SaveSchemeRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<SchemeDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await ActiveAsync(ct);
                if (active == null) return ConfigError<SchemeDto>();
                var bad = await ValidateHeaderAsync(schoolId, req, null, active.Value, ct);
                if (bad.Error != null) return bad.Error;
                var now = DateTime.UtcNow;
                var s = new TbReportScheme { SchoolId = schoolId, StatusId = active.Value, CreatedBy = userId, CreatedAt = now };
                Apply(s, req, userId, now);
                _repo.Add(s);
                await _repo.SaveChangesAsync(ct);
                return await DetailAsync(schoolId, s.SchemeId, 201, "Report scheme created.", null, ct);
            }
            catch (DbUpdateException ex) when (IsName(ex)) { return Result<SchemeDto>(409, "A scheme with that name already exists for this academic year.", null); }
            catch (Exception ex) { return Error<SchemeDto>(ex, "creating a report scheme"); }
        }

        public async Task<ApiResponse<SchemeDto>> UpdateAsync(string? schoolId, long userId, long schemeId, SaveSchemeRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<SchemeDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await ActiveAsync(ct);
                if (active == null) return ConfigError<SchemeDto>();
                var g = await _repo.LoadGraphAsync(schoolId, schemeId, true, ct);
                if (g == null || g.Scheme.StatusId != active.Value) return Result<SchemeDto>(404, "Report scheme not found.", null);
                var bad = await ValidateHeaderAsync(schoolId, req, schemeId, active.Value, ct);
                if (bad.Error != null) return bad.Error;
                if (req.AcademicYearId != g.Scheme.AcademicYearId && (g.Classes.Count > 0 || g.Terms.Count > 0))
                    return Result<SchemeDto>(409, "The academic year can't change once the scheme has classes or terms. Clone the scheme into the other year instead.", null);
                if (req.EvaluationMode != g.Scheme.EvaluationMode && g.Cells.Count > 0)
                    return Result<SchemeDto>(409, "The evaluation mode can't change once the subject matrix is filled in. Clear the matrix first, or create a new scheme.", null);
                if (await _guard.HasEntriesAsync(schoolId, schemeId, ct) && (req.EvaluationMode != g.Scheme.EvaluationMode || req.GradeScaleId != g.Scheme.GradeScaleId || req.PercentageRounding != g.Scheme.PercentageRounding))
                    return Result<SchemeDto>(409, "Marks have been entered, so the mode, grade scale and rounding can't change.", null);
                Apply(g.Scheme, req, userId, DateTime.UtcNow);
                await _repo.SaveChangesAsync(ct);
                return await DetailAsync(schoolId, schemeId, 200, "Report scheme saved.", null, ct);
            }
            catch (DbUpdateException ex) when (IsName(ex)) { return Result<SchemeDto>(409, "A scheme with that name already exists for this academic year.", null); }
            catch (Exception ex) { return Error<SchemeDto>(ex, "updating a report scheme"); }
        }

        public async Task<ApiResponse<SchemeDto>> DeleteAsync(string? schoolId, long schemeId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<SchemeDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await ActiveAsync(ct);
                var inactive = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType, ct);
                if (active == null || inactive == null) return ConfigError<SchemeDto>();
                var g = await _repo.LoadGraphAsync(schoolId, schemeId, true, ct);
                if (g == null || g.Scheme.StatusId != active.Value) return Result<SchemeDto>(404, "Report scheme not found.", null);
                if (await _guard.HasEntriesAsync(schoolId, schemeId, ct)) return Result<SchemeDto>(409, "Marks have been entered against this scheme, so it can't be removed.", null);
                _repo.RemoveRange(g.Classes.Cast<object>());       // frees the classes for another scheme (the subjects, matrix and skills go with them)
                g.Scheme.StatusId = inactive.Value; g.Scheme.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                return await DetailAsync(schoolId, schemeId, 200, "Report scheme removed.", null, ct);
            }
            catch (Exception ex) { return Error<SchemeDto>(ex, "removing a report scheme"); }
        }

        // ================================================================== classes
        public async Task<ApiResponse<SchemeDto>> SetClassesAsync(string? schoolId, long schemeId, SetSchemeClassesRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<SchemeDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await ActiveAsync(ct);
                if (active == null) return ConfigError<SchemeDto>();
                var g = await _repo.LoadGraphAsync(schoolId, schemeId, true, ct);
                if (g == null || g.Scheme.StatusId != active.Value) return Result<SchemeDto>(404, "Report scheme not found.", null);
                var wanted = req.ClassIds!.Distinct().ToList();
                if (wanted.Count == 0) return Result<SchemeDto>(400, "Choose at least one class.", null);
                var names = await _repo.ClassNamesAsync(schoolId, wanted, active.Value, ct);
                if (wanted.Any(id => !names.ContainsKey(id))) return Result<SchemeDto>(400, "One of the classes was not found for your school (or is inactive).", null);
                var conflicts = await _repo.ClassConflictsAsync(schoolId, g.Scheme.AcademicYearId, wanted, schemeId, active.Value, ct);
                if (conflicts.Count > 0)
                    return Result<SchemeDto>(409, string.Join(" ", conflicts.Select(c => $"{names[c.ClassId]} is already in \"{c.SchemeName}\" for this academic year.")) + " A class can be in one scheme per year.", null);

                var removed = g.Classes.Where(c => !wanted.Contains(c.ClassId)).ToList();
                if (removed.Count > 0)
                {
                    var fp = await _guard.FootprintAsync(schoolId, schemeId, ct);
                    var blocked = removed.Where(c => fp.ClassIds.Contains(c.ClassId)).Select(c => c.ClassId).ToList();
                    if (blocked.Count > 0)
                    {
                        var blockedNames = await _repo.ClassNamesAsync(schoolId, blocked, active.Value, ct);
                        return Result<SchemeDto>(409, $"Marks or class-teacher inputs have been entered for {string.Join(", ", blocked.Select(id => blockedNames.GetValueOrDefault(id, "a class")))}, so it can't be taken out of this scheme.", null);
                    }
                }
                _repo.RemoveRange(removed.Cast<object>());
                var now = DateTime.UtcNow;
                _repo.AddRange(wanted.Where(id => g.Classes.All(c => c.ClassId != id)).Select(id => (object)new TbReportSchemeClass { SchemeId = schemeId, SchoolId = schoolId, AcademicYearId = g.Scheme.AcademicYearId, ClassId = id, CreatedAt = now }));
                await _repo.SaveChangesAsync(ct);
                return await DetailAsync(schoolId, schemeId, 200, "Classes saved.", null, ct);
            }
            catch (DbUpdateException ex) when ((ex.InnerException as PostgresException)?.ConstraintName == "uq_tb_report_scheme_classes_year_class") { return Result<SchemeDto>(409, "One of those classes was just added to another scheme.", null); }
            catch (Exception ex) { return Error<SchemeDto>(ex, "saving a scheme's classes"); }
        }

        // ================================================================== terms and exams
        public async Task<ApiResponse<SchemeDto>> SaveTermsAsync(string? schoolId, long schemeId, SaveTermsRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<SchemeDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await ActiveAsync(ct);
                if (active == null) return ConfigError<SchemeDto>();
                var g = await _repo.LoadGraphAsync(schoolId, schemeId, true, ct);
                if (g == null || g.Scheme.StatusId != active.Value) return Result<SchemeDto>(404, "Report scheme not found.", null);
                var year = await _repo.GetYearAsync(schoolId, g.Scheme.AcademicYearId, ct);
                if (year == null) return Result<SchemeDto>(500, "Something went wrong. Please try again later.", null);

                var terms = req.Terms!;
                if (terms.Count == 0 || terms.Count > ReportSetup.MaxTerms) return Result<SchemeDto>(400, $"A scheme needs 1 to {ReportSetup.MaxTerms} terms.", null);
                var weighted = g.Scheme.YearCombine == "WEIGHTED";
                DateOnly? previousEnd = null;
                foreach (var (t, i) in terms.Select((t, i) => (t, i)))
                {
                    var label = $"Term {i + 1}";
                    if (string.IsNullOrWhiteSpace(t.Name)) return Result<SchemeDto>(400, $"{label} needs a name.", null);
                    if (t.EndDate < t.StartDate) return Result<SchemeDto>(400, $"{t.Name}: the end date can't be before the start date.", null);
                    if (t.StartDate < year.StartDate || t.EndDate > year.EndDate) return Result<SchemeDto>(400, $"{t.Name}: the dates must be inside the academic year ({year.StartDate:dd MMM yyyy} to {year.EndDate:dd MMM yyyy}).", null);
                    if (previousEnd != null && t.StartDate <= previousEnd) return Result<SchemeDto>(400, $"{t.Name} starts before the previous term ends. Terms can't overlap.", null);
                    previousEnd = t.EndDate;
                    if (weighted && t.YearWeight == null) return Result<SchemeDto>(400, $"{t.Name}: this scheme combines the year by weight, so every term needs a weight.", null);
                    if (t.TermId != null && g.Terms.All(x => x.TermId != t.TermId)) return Result<SchemeDto>(400, $"{t.Name}: that term does not belong to this scheme.", null);
                    var exams = t.Exams!;
                    if (exams.Count == 0 || exams.Count > ReportSetup.MaxExamsPerTerm) return Result<SchemeDto>(400, $"{t.Name} needs 1 to {ReportSetup.MaxExamsPerTerm} exams.", null);
                    if (exams.Select(e => (e.Name ?? "").Trim().ToLowerInvariant()).Distinct().Count() != exams.Count) return Result<SchemeDto>(400, $"{t.Name}: exam names must be different.", null);
                    foreach (var e in exams)
                    {
                        if (string.IsNullOrWhiteSpace(e.Name)) return Result<SchemeDto>(400, $"{t.Name}: every exam needs a name.", null);
                        if (e.ExamId != null && !g.Exams.Any(x => x.ExamId == e.ExamId && x.TermId == t.TermId)) return Result<SchemeDto>(400, $"{t.Name} / {e.Name}: that exam does not belong to this term.", null);
                    }
                }
                if (terms.Select(t => t.Name!.Trim().ToLowerInvariant()).Distinct().Count() != terms.Count) return Result<SchemeDto>(400, "Term names must be different.", null);
                if (terms.Where(t => t.TermId != null).Select(t => t.TermId).Distinct().Count() != terms.Count(t => t.TermId != null)) return Result<SchemeDto>(400, "A term was listed twice.", null);

                var keepTerms = terms.Where(t => t.TermId != null).Select(t => t.TermId!.Value).ToHashSet();
                var keepExams = terms.SelectMany(t => t.Exams!).Where(e => e.ExamId != null).Select(e => e.ExamId!.Value).ToHashSet();
                var droppedTerms = g.Terms.Where(t => !keepTerms.Contains(t.TermId)).ToList();
                var droppedExams = g.Exams.Where(e => !keepExams.Contains(e.ExamId)).ToList();
                var fp = await _guard.FootprintAsync(schoolId, schemeId, ct);
                if (droppedTerms.Any(t => fp.TermIds.Contains(t.TermId)))
                    return Result<SchemeDto>(409, $"Entries exist for {string.Join(", ", droppedTerms.Where(t => fp.TermIds.Contains(t.TermId)).Select(t => t.TermName))}, so that term can't be removed.", null);
                if (droppedExams.Any(e => fp.ExamIds.Contains(e.ExamId)))
                    return Result<SchemeDto>(409, $"Marks have been entered for {string.Join(", ", droppedExams.Where(e => fp.ExamIds.Contains(e.ExamId)).Select(e => e.ExamName))}, so that exam can't be removed.", null);
                foreach (var t in terms.Where(t => t.TermId != null))
                {
                    var cur = g.Terms.First(x => x.TermId == t.TermId);
                    if (fp.LockedTermIds.Contains(cur.TermId) && (cur.StartDate != t.StartDate || cur.EndDate != t.EndDate))
                        return Result<SchemeDto>(409, $"{cur.TermName} is locked for a section, so its dates can't change (they decide the attendance on the report card). Unlock it first.", null);
                    foreach (var e in t.Exams!.Where(e => e.ExamId != null))
                    {
                        var ex = g.Exams.First(x => x.ExamId == e.ExamId);
                        if (fp.ExamIds.Contains(ex.ExamId) && (ex.DefaultMaxMarks != e.DefaultMaxMarks || ex.WeightPoints != (e.WeightPoints ?? e.DefaultMaxMarks)))
                            return Result<SchemeDto>(409, $"Marks have been entered for {ex.ExamName}, so its default maximum marks and weight can't change. The name and the order still can.", null);
                    }
                }

                await using var tx = await _repo.BeginTransactionAsync(ct);
                foreach (var t in g.Terms) t.Sequence = (short)(t.Sequence + Gap);
                foreach (var e in g.Exams) e.Sequence = (short)(e.Sequence + Gap);
                await _repo.SaveChangesAsync(ct);
                _repo.RemoveRange(droppedExams.Cast<object>());
                _repo.RemoveRange(droppedTerms.Cast<object>());
                await _repo.SaveChangesAsync(ct);

                var termRows = new List<(TbReportTerm Row, SaveTermRequest Req)>();
                foreach (var (t, i) in terms.Select((t, i) => (t, i)))
                {
                    var row = t.TermId == null ? new TbReportTerm { SchemeId = schemeId, SchoolId = schoolId } : g.Terms.First(x => x.TermId == t.TermId);
                    row.TermName = t.Name!.Trim(); row.Sequence = (short)(i + 1); row.StartDate = t.StartDate!.Value; row.EndDate = t.EndDate!.Value; row.YearWeight = weighted ? t.YearWeight : null;
                    if (t.TermId == null) _repo.Add(row);
                    termRows.Add((row, t));
                }
                await _repo.SaveChangesAsync(ct);
                foreach (var (term, t) in termRows)
                    foreach (var (e, j) in t.Exams!.Select((e, j) => (e, j)))
                    {
                        var row = e.ExamId == null ? new TbReportExam { TermId = term.TermId, SchemeId = schemeId, SchoolId = schoolId } : g.Exams.First(x => x.ExamId == e.ExamId);
                        row.ExamName = e.Name!.Trim(); row.Sequence = (short)(j + 1); row.DefaultMaxMarks = e.DefaultMaxMarks!.Value; row.WeightPoints = e.WeightPoints ?? e.DefaultMaxMarks.Value;
                        if (e.ExamId == null) _repo.Add(row);
                    }
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return await DetailAsync(schoolId, schemeId, 200, "Terms and exams saved.", null, ct);
            }
            catch (Exception ex) { return Error<SchemeDto>(ex, "saving a scheme's terms and exams"); }
        }

        // ================================================================== subjects
        public async Task<ApiResponse<SchemeDto>> SaveSubjectsAsync(string? schoolId, long schemeId, SaveSubjectsRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<SchemeDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await ActiveAsync(ct);
                if (active == null) return ConfigError<SchemeDto>();
                var g = await _repo.LoadGraphAsync(schoolId, schemeId, true, ct);
                if (g == null || g.Scheme.StatusId != active.Value) return Result<SchemeDto>(404, "Report scheme not found.", null);
                var items = req.Subjects!;
                var classIds = g.Classes.Select(c => c.ClassId).ToList();
                var mapped = (await _repo.MappedSubjectsAsync(schoolId, classIds, active.Value, ct)).Select(m => (m.ClassId, m.SubjectId)).ToHashSet();
                var classNames = await _repo.ClassNamesAsync(schoolId, classIds, active.Value, ct);
                var subjectNames = await _repo.SubjectNamesAsync(schoolId, items.Select(i => i.SubjectId!.Value).Distinct().ToList(), ct);
                foreach (var i in items)
                {
                    if (!classIds.Contains(i.ClassId!.Value)) return Result<SchemeDto>(400, "A subject was given for a class that is not in this scheme. Add the class to the scheme first.", null);
                    if (!mapped.Contains((i.ClassId.Value, i.SubjectId!.Value)))
                        return Result<SchemeDto>(400, $"{subjectNames.GetValueOrDefault(i.SubjectId.Value, "That subject")} is not taught in {classNames.GetValueOrDefault(i.ClassId.Value, "that class")}. Add it under Class-Subject Mapping first.", null);
                    if (i.LabelOverride != null && i.LabelOverride.Trim().Length > 60) return Result<SchemeDto>(400, "A subject label can be at most 60 characters.", null);
                }
                if (items.Select(i => (i.ClassId, i.SubjectId)).Distinct().Count() != items.Count) return Result<SchemeDto>(400, "A subject was listed twice for the same class.", null);

                var keep = items.Select(i => (i.ClassId!.Value, i.SubjectId!.Value)).ToHashSet();
                var dropped = g.Subjects.Where(s => !keep.Contains((s.ClassId, s.SubjectId))).ToList();
                if (dropped.Count > 0)
                {
                    var fp = await _guard.FootprintAsync(schoolId, schemeId, ct);
                    var blocked = dropped.Where(s => fp.SchemeSubjectIds.Contains(s.SchemeSubjectId)).ToList();
                    if (blocked.Count > 0)
                    {
                        var blockedNames = await _repo.SubjectNamesAsync(schoolId, blocked.Select(b => b.SubjectId).Distinct().ToList(), ct);
                        return Result<SchemeDto>(409, $"Marks have been entered for {string.Join(", ", blocked.Select(b => blockedNames.GetValueOrDefault(b.SubjectId, "a subject")))}, so it can't be removed from this scheme. Its label and order can still change.", null);
                    }
                }
                _repo.RemoveRange(dropped.Cast<object>());      // its matrix cells and skills go with it
                foreach (var i in items)
                {
                    var row = g.Subjects.FirstOrDefault(s => s.ClassId == i.ClassId && s.SubjectId == i.SubjectId);
                    if (row == null) { row = new TbReportSchemeSubject { SchemeId = schemeId, SchoolId = schoolId, ClassId = i.ClassId!.Value, SubjectId = i.SubjectId!.Value }; _repo.Add(row); }
                    row.DisplayOrder = (short)Math.Clamp(i.DisplayOrder, 0, short.MaxValue);
                    row.LabelOverride = string.IsNullOrWhiteSpace(i.LabelOverride) ? null : i.LabelOverride.Trim();
                    row.IncludeInTotal = i.IncludeInTotal;
                }
                await _repo.SaveChangesAsync(ct);
                return await DetailAsync(schoolId, schemeId, 200, "Subjects saved.", null, ct);
            }
            catch (Exception ex) { return Error<SchemeDto>(ex, "saving a scheme's subjects"); }
        }

        // ================================================================== exam x subject matrix
        public async Task<ApiResponse<SchemeDto>> SaveMatrixAsync(string? schoolId, long schemeId, SaveMatrixRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<SchemeDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await ActiveAsync(ct);
                if (active == null) return ConfigError<SchemeDto>();
                var g = await _repo.LoadGraphAsync(schoolId, schemeId, true, ct);
                if (g == null || g.Scheme.StatusId != active.Value) return Result<SchemeDto>(404, "Report scheme not found.", null);
                var cells = req.Cells!;
                var marksMode = g.Scheme.EvaluationMode == ReportSetup.Marks;
                foreach (var c in cells)
                {
                    if (g.Exams.All(e => e.ExamId != c.ExamId)) return Result<SchemeDto>(400, "A cell refers to an exam that is not in this scheme.", null);
                    if (g.Subjects.All(s => s.SchemeSubjectId != c.SchemeSubjectId)) return Result<SchemeDto>(400, "A cell refers to a subject that is not in this scheme.", null);
                    if (c.EntryMode != "MARKS" && c.EntryMode != ReportSetup.Grade) return Result<SchemeDto>(400, "Entry mode must be MARKS or GRADE.", null);
                    if (!marksMode && c.EntryMode != ReportSetup.Grade) return Result<SchemeDto>(400, "A grades-only scheme takes grades in every cell.", null);
                    if (c.EntryMode == ReportSetup.Grade && (c.MaxMarks != null || c.WeightPoints != null)) return Result<SchemeDto>(400, "A grade cell has no maximum marks or weight.", null);
                }
                if (cells.Select(c => (c.ExamId, c.SchemeSubjectId)).Distinct().Count() != cells.Count) return Result<SchemeDto>(400, "A cell was listed twice.", null);

                var keep = cells.Select(c => (c.ExamId!.Value, c.SchemeSubjectId!.Value)).ToHashSet();
                var dropped = g.Cells.Where(c => !keep.Contains((c.ExamId, c.SchemeSubjectId))).ToList();
                var fp = await _guard.FootprintAsync(schoolId, schemeId, ct);
                if (dropped.Any(d => fp.ExamSubjectIds.Contains(d.ExamSubjectId)))
                    return Result<SchemeDto>(409, "Marks have been entered for a subject you are taking out of an exam, so it can't be removed.", null);
                foreach (var c in cells)
                {
                    var cur = g.Cells.FirstOrDefault(x => x.ExamId == c.ExamId && x.SchemeSubjectId == c.SchemeSubjectId);
                    if (cur != null && fp.ExamSubjectIds.Contains(cur.ExamSubjectId) && (cur.EntryMode != c.EntryMode || cur.MaxMarks != c.MaxMarks || cur.WeightPoints != c.WeightPoints))
                        return Result<SchemeDto>(409, "Marks have been entered for this subject in this exam, so its maximum marks, weight and entry mode can't change.", null);
                }
                _repo.RemoveRange(dropped.Cast<object>());
                foreach (var c in cells)
                {
                    var subject = g.Subjects.First(s => s.SchemeSubjectId == c.SchemeSubjectId);
                    var row = g.Cells.FirstOrDefault(x => x.ExamId == c.ExamId && x.SchemeSubjectId == c.SchemeSubjectId);
                    if (row == null) { row = new TbReportExamSubject { ExamId = c.ExamId!.Value, SchemeId = schemeId, SchoolId = schoolId, SchemeSubjectId = c.SchemeSubjectId!.Value, ClassId = subject.ClassId }; _repo.Add(row); }
                    row.EntryMode = c.EntryMode!; row.MaxMarks = c.MaxMarks; row.WeightPoints = c.WeightPoints;
                }
                await _repo.SaveChangesAsync(ct);
                return await DetailAsync(schoolId, schemeId, 200, "Subject matrix saved.", null, ct);
            }
            catch (Exception ex) { return Error<SchemeDto>(ex, "saving a scheme's subject matrix"); }
        }

        // ================================================================== skills
        public async Task<ApiResponse<SchemeDto>> SaveSkillsAsync(string? schoolId, long schemeId, SaveSkillsRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<SchemeDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await ActiveAsync(ct);
                if (active == null) return ConfigError<SchemeDto>();
                var g = await _repo.LoadGraphAsync(schoolId, schemeId, true, ct);
                if (g == null || g.Scheme.StatusId != active.Value) return Result<SchemeDto>(404, "Report scheme not found.", null);
                foreach (var s in req.Subjects!)
                {
                    if (g.Subjects.All(x => x.SchemeSubjectId != s.SchemeSubjectId)) return Result<SchemeDto>(400, "Skills were given for a subject that is not in this scheme.", null);
                    var names = (s.Skills ?? new()).Select(n => (n ?? "").Trim()).ToList();
                    if (names.Count > ReportSetup.MaxSkills) return Result<SchemeDto>(400, $"A subject can have at most {ReportSetup.MaxSkills} skills.", null);
                    if (names.Any(n => n.Length == 0 || n.Length > 60)) return Result<SchemeDto>(400, "Every skill needs a name of up to 60 characters.", null);
                    if (names.Select(n => n.ToLowerInvariant()).Distinct().Count() != names.Count) return Result<SchemeDto>(400, "Skill names must be different within a subject.", null);
                }
                if (req.Subjects!.Select(s => s.SchemeSubjectId).Distinct().Count() != req.Subjects!.Count) return Result<SchemeDto>(400, "A subject was listed twice.", null);
                var touched = req.Subjects!.Select(s => s.SchemeSubjectId!.Value).ToHashSet();
                var fp = await _guard.FootprintAsync(schoolId, schemeId, ct);
                // a subject whose skills would come out exactly the same is left alone; one that has entries and would change is refused
                var unchanged = new HashSet<long>();
                foreach (var s in req.Subjects!)
                {
                    var current = g.Skills.Where(k => k.SchemeSubjectId == s.SchemeSubjectId).OrderBy(k => k.Sequence).Select(k => k.SkillName).ToList();
                    var wanted = (s.Skills ?? new()).Select(n => (n ?? "").Trim()).ToList();
                    if (current.SequenceEqual(wanted)) { unchanged.Add(s.SchemeSubjectId!.Value); continue; }
                    if (fp.SchemeSubjectIds.Contains(s.SchemeSubjectId!.Value))
                        return Result<SchemeDto>(409, "Marks have been entered for this subject, so its skills can't be added, removed or renamed.", null);
                }
                touched.ExceptWith(unchanged);
                var existing = g.Skills.Where(k => touched.Contains(k.SchemeSubjectId)).ToList();

                await using var tx = await _repo.BeginTransactionAsync(ct);
                _repo.RemoveRange(existing.Cast<object>());
                await _repo.SaveChangesAsync(ct);                 // replaced as a set: delete first, then insert (the names and sequence are unique per subject)
                foreach (var s in req.Subjects!.Where(s => touched.Contains(s.SchemeSubjectId!.Value)))
                    _repo.AddRange((s.Skills ?? new()).Select((n, i) => (object)new TbReportSubjectSkill { SchemeSubjectId = s.SchemeSubjectId!.Value, SchemeId = schemeId, SchoolId = schoolId, SkillName = n.Trim(), Sequence = (short)(i + 1) }));
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return await DetailAsync(schoolId, schemeId, 200, "Skills saved.", null, ct);
            }
            catch (Exception ex) { return Error<SchemeDto>(ex, "saving a scheme's skills"); }
        }

        // ================================================================== from a preset (the three-step path)
        public async Task<ApiResponse<SchemeDto>> FromPresetAsync(string? schoolId, long userId, SchemeFromPresetRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<SchemeDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await ActiveAsync(ct);
                if (active == null) return ConfigError<SchemeDto>();
                var preset = ReportPresets.FindScheme(req.PresetCode);
                if (preset == null) return Result<SchemeDto>(400, "That preset does not exist.", null);
                var year = await _repo.GetYearAsync(schoolId, req.AcademicYearId!.Value, ct);
                if (year == null) return Result<SchemeDto>(400, "The selected academic year was not found for your school.", null);
                var classIds = req.ClassIds!.Distinct().ToList();
                if (classIds.Count == 0) return Result<SchemeDto>(400, "Choose at least one class.", null);
                var classNames = await _repo.ClassNamesAsync(schoolId, classIds, active.Value, ct);
                if (classIds.Any(id => !classNames.ContainsKey(id))) return Result<SchemeDto>(400, "One of the classes was not found for your school (or is inactive).", null);
                var conflicts = await _repo.ClassConflictsAsync(schoolId, year.AcademicYearId, classIds, null, active.Value, ct);
                if (conflicts.Count > 0) return Result<SchemeDto>(409, string.Join(" ", conflicts.Select(c => $"{classNames[c.ClassId]} is already in \"{c.SchemeName}\" for {year.YearName}.")), null);

                var name = string.IsNullOrWhiteSpace(req.SchemeName) ? Fit($"{preset.Name} {year.YearName}", 80) : req.SchemeName.Trim();
                var template = string.IsNullOrWhiteSpace(req.TemplateKey) ? preset.TemplateKey : req.TemplateKey.Trim();
                var tpl = ReportCardTemplates.Find(template);
                if (tpl == null || !tpl.Modes.Contains(preset.Mode)) return Result<SchemeDto>(400, CompatMessage(tpl, preset.Mode), null);
                var rounding = req.PercentageRounding ?? "ROUND_HALF_UP";
                if (!ReportSetup.Roundings.Contains(rounding)) return Result<SchemeDto>(400, "Unknown rounding mode.", null);
                if (req.SubjectPassPercent is < 0 or > 100 || req.OverallPassPercent is < 0 or > 100) return Result<SchemeDto>(400, "Pass marks must be between 0 and 100.", null);

                var ranges = ReportPresets.SplitYear(year.StartDate, year.EndDate, preset.Terms.Length);
                if (req.TermDates is { Count: > 0 })
                {
                    if (req.TermDates.Count != preset.Terms.Length) return Result<SchemeDto>(400, $"This preset has {preset.Terms.Length} term(s): give {preset.Terms.Length} date range(s) or none.", null);
                    ranges = req.TermDates.Select(d => (d.StartDate!.Value, d.EndDate!.Value)).ToList();
                    DateOnly? prev = null;
                    foreach (var (s, e) in ranges)
                    {
                        if (e < s || s < year.StartDate || e > year.EndDate) return Result<SchemeDto>(400, $"Term dates must be inside the academic year ({year.StartDate:dd MMM yyyy} to {year.EndDate:dd MMM yyyy}).", null);
                        if (prev != null && s <= prev) return Result<SchemeDto>(400, "Terms can't overlap.", null);
                        prev = e;
                    }
                }

                if (await _repo.SchemeNameTakenAsync(schoolId, year.AcademicYearId, name, null, active.Value, ct)) return Result<SchemeDto>(409, $"A scheme named \"{name}\" already exists for {year.YearName}. Give this one another name.", null);

                await using var tx = await _repo.BeginTransactionAsync(ct);
                var now = DateTime.UtcNow;
                // the grade scale: the school's own copy of the preset's scale, made once and then reused
                var scalePreset = ReportPresets.FindScale(preset.ScalePresetCode)!;
                var scale = await _repo.FindScaleByPresetAsync(schoolId, scalePreset.Code, active.Value, ct);
                if (scale == null)
                {
                    var scaleName = scalePreset.Name;
                    if (await _repo.ScaleNameTakenAsync(schoolId, scaleName, null, active.Value, ct)) scaleName = Fit($"{scalePreset.Name} (copy)", 80);
                    scale = new TbGradeScale { SchoolId = schoolId, ScaleName = scaleName, ScaleKind = scalePreset.Kind, SourcePresetCode = scalePreset.Code, StatusId = active.Value, CreatedAt = now, UpdatedAt = now };
                    _repo.Add(scale);
                    await _repo.SaveChangesAsync(ct);
                    _repo.AddRange(scalePreset.Bands.OrderBy(b => b.Min).Select((b, i) => (object)new TbGradeScaleBand
                    {
                        GradeScaleId = scale.GradeScaleId, SchoolId = schoolId, Sequence = (short)(i + 1), GradeCode = b.Code, Label = b.Label, MinPercent = b.Min, IsFail = b.IsFail, Description = b.Description
                    }));
                    await _repo.SaveChangesAsync(ct);
                }

                var scheme = new TbReportScheme
                {
                    SchoolId = schoolId, AcademicYearId = year.AcademicYearId, SchemeName = name, EvaluationMode = preset.Mode, GradeScaleId = scale.GradeScaleId, YearCombine = "SUM_OF_TERMS",
                    SubjectPassPercent = req.SubjectPassPercent ?? 33, OverallPassPercent = req.OverallPassPercent ?? 33, ShowRank = req.ShowRank ?? preset.ShowRank, RankScope = "SECTION",
                    AttendanceSource = "AUTO", PercentageRounding = rounding, TemplateKey = template, StatusId = active.Value, CreatedBy = userId, UpdatedBy = userId, CreatedAt = now, UpdatedAt = now
                };
                _repo.Add(scheme);
                await _repo.SaveChangesAsync(ct);
                var sid = scheme.SchemeId;
                _repo.AddRange(classIds.Select(c => (object)new TbReportSchemeClass { SchemeId = sid, SchoolId = schoolId, AcademicYearId = year.AcademicYearId, ClassId = c, CreatedAt = now }));

                var termRows = preset.Terms.Select((t, i) => new TbReportTerm { SchemeId = sid, SchoolId = schoolId, TermName = t.Name, Sequence = (short)(i + 1), StartDate = ranges[i].Item1, EndDate = ranges[i].Item2 }).ToList();
                _repo.AddRange(termRows);
                await _repo.SaveChangesAsync(ct);
                var examRows = new List<TbReportExam>();
                for (var i = 0; i < preset.Terms.Length; i++)
                    examRows.AddRange(preset.Terms[i].Exams.Select((e, j) => new TbReportExam { TermId = termRows[i].TermId, SchemeId = sid, SchoolId = schoolId, ExamName = e.Name, Sequence = (short)(j + 1), DefaultMaxMarks = e.Max, WeightPoints = e.Weight }));
                _repo.AddRange(examRows);

                var mapped = await _repo.MappedSubjectsAsync(schoolId, classIds, active.Value, ct);
                var subjectRows = new List<TbReportSchemeSubject>();
                var warnings = new List<string>();
                foreach (var c in classIds)
                {
                    var list = mapped.Where(m => m.ClassId == c).OrderBy(m => m.SubjectName, StringComparer.OrdinalIgnoreCase).ToList();
                    if (list.Count == 0) warnings.Add($"{classNames[c]} has no subjects yet. Add them under Class-Subject Mapping, then use \"Refresh subjects\" on the scheme.");
                    subjectRows.AddRange(list.Select((m, i) => new TbReportSchemeSubject { SchemeId = sid, SchoolId = schoolId, ClassId = c, SubjectId = m.SubjectId, DisplayOrder = (short)(i + 1), IncludeInTotal = true }));
                }
                _repo.AddRange(subjectRows);
                await _repo.SaveChangesAsync(ct);

                _repo.AddRange(subjectRows.SelectMany(s => examRows.Select(e => (object)new TbReportExamSubject { ExamId = e.ExamId, SchemeId = sid, SchoolId = schoolId, SchemeSubjectId = s.SchemeSubjectId, ClassId = s.ClassId, EntryMode = preset.EntryMode })));
                if (preset.Skills.Length > 0)
                    _repo.AddRange(subjectRows.SelectMany(s => preset.Skills.Select((k, i) => (object)new TbReportSubjectSkill { SchemeSubjectId = s.SchemeSubjectId, SchemeId = sid, SchoolId = schoolId, SkillName = k, Sequence = (short)(i + 1) })));
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return await DetailAsync(schoolId, sid, 201, "Report scheme created from the preset.", warnings, ct);
            }
            catch (DbUpdateException ex) when (IsName(ex)) { return Result<SchemeDto>(409, "A scheme with that name already exists for this academic year.", null); }
            catch (DbUpdateException ex) when ((ex.InnerException as PostgresException)?.ConstraintName == "uq_tb_report_scheme_classes_year_class") { return Result<SchemeDto>(409, "One of those classes was just added to another scheme.", null); }
            catch (Exception ex) { return Error<SchemeDto>(ex, "creating a report scheme from a preset"); }
        }

        // ================================================================== clone
        public async Task<ApiResponse<SchemeDto>> CloneAsync(string? schoolId, long userId, long schemeId, CloneSchemeRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<SchemeDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await ActiveAsync(ct);
                if (active == null) return ConfigError<SchemeDto>();
                var src = await _repo.LoadGraphAsync(schoolId, schemeId, false, ct);
                if (src == null || src.Scheme.StatusId != active.Value) return Result<SchemeDto>(404, "Report scheme not found.", null);
                var srcYear = await _repo.GetYearAsync(schoolId, src.Scheme.AcademicYearId, ct);
                var year = await _repo.GetYearAsync(schoolId, req.AcademicYearId ?? src.Scheme.AcademicYearId, ct);
                if (year == null || srcYear == null) return Result<SchemeDto>(400, "The selected academic year was not found for your school.", null);
                var sameYear = year.AcademicYearId == srcYear.AcademicYearId;
                var name = string.IsNullOrWhiteSpace(req.Name) ? Fit($"{src.Scheme.SchemeName} (copy)", 80) : req.Name.Trim();
                if (await _repo.SchemeNameTakenAsync(schoolId, year.AcademicYearId, name, null, active.Value, ct)) return Result<SchemeDto>(409, $"A scheme named \"{name}\" already exists for {year.YearName}.", null);

                var warnings = new List<string>();
                var classIds = new List<long>();
                if (sameYear) warnings.Add("The copy has no classes: a class can be in only one scheme per academic year. Choose its classes next.");
                else
                {
                    var conflicts = await _repo.ClassConflictsAsync(schoolId, year.AcademicYearId, src.Classes.Select(c => c.ClassId).ToList(), null, active.Value, ct);
                    var taken = conflicts.Select(c => c.ClassId).ToHashSet();
                    classIds = src.Classes.Select(c => c.ClassId).Where(c => !taken.Contains(c)).ToList();
                    if (taken.Count > 0) warnings.Add($"{taken.Count} class(es) were not copied because they already belong to another scheme in {year.YearName}.");
                }
                var yearShift = year.StartDate.Year - srcYear.StartDate.Year;   // whole calendar years, so 31 March stays 31 March (a leap year would move it by a day)
                DateOnly Move(DateOnly d) { var m = sameYear ? d : d.AddYears(yearShift); return m < year.StartDate ? year.StartDate : m > year.EndDate ? year.EndDate : m; }

                var mapped = (await _repo.MappedSubjectsAsync(schoolId, classIds, active.Value, ct)).Select(m => (m.ClassId, m.SubjectId)).ToHashSet();
                await using var tx = await _repo.BeginTransactionAsync(ct);
                var now = DateTime.UtcNow;
                var scheme = new TbReportScheme
                {
                    SchoolId = schoolId, AcademicYearId = year.AcademicYearId, SchemeName = name, EvaluationMode = src.Scheme.EvaluationMode, GradeScaleId = src.Scheme.GradeScaleId, YearCombine = src.Scheme.YearCombine,
                    SubjectPassPercent = src.Scheme.SubjectPassPercent, OverallPassPercent = src.Scheme.OverallPassPercent, ShowRank = src.Scheme.ShowRank, RankScope = src.Scheme.RankScope,
                    AttendanceSource = src.Scheme.AttendanceSource, PercentageRounding = src.Scheme.PercentageRounding, TemplateKey = src.Scheme.TemplateKey, StatusId = active.Value,
                    CreatedBy = userId, UpdatedBy = userId, CreatedAt = now, UpdatedAt = now
                };
                _repo.Add(scheme);
                await _repo.SaveChangesAsync(ct);
                var sid = scheme.SchemeId;
                _repo.AddRange(classIds.Select(c => (object)new TbReportSchemeClass { SchemeId = sid, SchoolId = schoolId, AcademicYearId = year.AcademicYearId, ClassId = c, CreatedAt = now }));

                var termMap = new Dictionary<long, TbReportTerm>();
                foreach (var t in src.Terms) termMap[t.TermId] = new TbReportTerm { SchemeId = sid, SchoolId = schoolId, TermName = t.TermName, Sequence = t.Sequence, StartDate = Move(t.StartDate), EndDate = Move(t.EndDate), YearWeight = t.YearWeight };
                _repo.AddRange(termMap.Values);
                await _repo.SaveChangesAsync(ct);
                var examMap = new Dictionary<long, TbReportExam>();
                foreach (var e in src.Exams) examMap[e.ExamId] = new TbReportExam { TermId = termMap[e.TermId].TermId, SchemeId = sid, SchoolId = schoolId, ExamName = e.ExamName, Sequence = e.Sequence, DefaultMaxMarks = e.DefaultMaxMarks, WeightPoints = e.WeightPoints };
                _repo.AddRange(examMap.Values);

                var subjectMap = new Dictionary<long, TbReportSchemeSubject>();
                var skipped = 0;
                foreach (var s in src.Subjects.Where(s => classIds.Contains(s.ClassId)))
                {
                    if (!mapped.Contains((s.ClassId, s.SubjectId))) { skipped++; continue; }
                    subjectMap[s.SchemeSubjectId] = new TbReportSchemeSubject { SchemeId = sid, SchoolId = schoolId, ClassId = s.ClassId, SubjectId = s.SubjectId, DisplayOrder = s.DisplayOrder, LabelOverride = s.LabelOverride, IncludeInTotal = s.IncludeInTotal };
                }
                if (skipped > 0) warnings.Add($"{skipped} subject(s) were not copied because they are no longer taught in their class (Class-Subject Mapping).");
                _repo.AddRange(subjectMap.Values);
                await _repo.SaveChangesAsync(ct);

                _repo.AddRange(src.Cells.Where(c => subjectMap.ContainsKey(c.SchemeSubjectId)).Select(c => (object)new TbReportExamSubject
                {
                    ExamId = examMap[c.ExamId].ExamId, SchemeId = sid, SchoolId = schoolId, SchemeSubjectId = subjectMap[c.SchemeSubjectId].SchemeSubjectId, ClassId = c.ClassId, MaxMarks = c.MaxMarks, WeightPoints = c.WeightPoints, EntryMode = c.EntryMode
                }));
                _repo.AddRange(src.Skills.Where(k => subjectMap.ContainsKey(k.SchemeSubjectId)).Select(k => (object)new TbReportSubjectSkill { SchemeSubjectId = subjectMap[k.SchemeSubjectId].SchemeSubjectId, SchemeId = sid, SchoolId = schoolId, SkillName = k.SkillName, Sequence = k.Sequence }));
                // exam dates are NOT copied: they belong to the year being planned
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return await DetailAsync(schoolId, sid, 201, "Report scheme copied.", warnings, ct);
            }
            catch (DbUpdateException ex) when (IsName(ex)) { return Result<SchemeDto>(409, "A scheme with that name already exists for that academic year.", null); }
            catch (Exception ex) { return Error<SchemeDto>(ex, "cloning a report scheme"); }
        }

        // ================================================================== validation + mapping helpers
        private sealed record HeaderCheck(ApiResponse<SchemeDto>? Error);

        private async Task<HeaderCheck> ValidateHeaderAsync(string schoolId, SaveSchemeRequest req, long? exceptId, int activeId, CancellationToken ct)
        {
            ApiResponse<SchemeDto> Bad(int code, string msg) => Result<SchemeDto>(code, msg, null);
            var name = (req.Name ?? "").Trim();
            if (name.Length == 0 || name.Length > 80) return new(Bad(400, "A scheme needs a name of up to 80 characters."));
            if (await _repo.GetYearAsync(schoolId, req.AcademicYearId!.Value, ct) == null) return new(Bad(400, "The selected academic year was not found for your school."));
            if (req.EvaluationMode != ReportSetup.Marks && req.EvaluationMode != ReportSetup.Grades) return new(Bad(400, "Evaluation mode must be MARKS or GRADES."));
            if (req.GradeScaleId != null)
            {
                var scale = await _repo.GetScaleAsync(schoolId, req.GradeScaleId.Value, false, ct);
                if (scale == null || scale.StatusId != activeId) return new(Bad(400, "The selected grade scale was not found for your school."));
                if (req.EvaluationMode == ReportSetup.Marks && scale.ScaleKind != ReportSetup.PercentBands) return new(Bad(400, "A marks scheme needs a percentage-band grade scale."));
                if (req.EvaluationMode == ReportSetup.Grades && scale.ScaleKind != ReportSetup.Rating) return new(Bad(400, "A grades-only scheme needs a rating grade scale."));
            }
            else if (req.EvaluationMode == ReportSetup.Grades) return new(Bad(400, "A grades-only scheme needs a rating grade scale."));
            if (req.YearCombine != null && !ReportSetup.YearCombines.Contains(req.YearCombine)) return new(Bad(400, "Unknown way of combining the terms."));
            if (req.RankScope != null && !ReportSetup.RankScopes.Contains(req.RankScope)) return new(Bad(400, "Rank scope must be SECTION or CLASS."));
            if (req.AttendanceSource != null && !ReportSetup.AttendanceSources.Contains(req.AttendanceSource)) return new(Bad(400, "Attendance source must be AUTO or MANUAL."));
            if (req.PercentageRounding != null && !ReportSetup.Roundings.Contains(req.PercentageRounding)) return new(Bad(400, "Percentage rounding must be ROUND_HALF_UP or TRUNCATE."));
            var tpl = ReportCardTemplates.Find(req.TemplateKey);
            if (tpl == null) return new(Bad(400, "Unknown card layout."));
            if (!tpl.Modes.Contains(req.EvaluationMode!)) return new(Bad(400, CompatMessage(tpl, req.EvaluationMode!)));
            if (await _repo.SchemeNameTakenAsync(schoolId, req.AcademicYearId.Value, name, exceptId, activeId, ct)) return new(Bad(409, $"A scheme named \"{name}\" already exists for this academic year."));
            return new(null);
        }

        private static void Apply(TbReportScheme s, SaveSchemeRequest r, long userId, DateTime now)
        {
            s.SchemeName = r.Name!.Trim(); s.AcademicYearId = r.AcademicYearId!.Value; s.EvaluationMode = r.EvaluationMode!; s.GradeScaleId = r.GradeScaleId;
            s.YearCombine = r.YearCombine ?? "SUM_OF_TERMS"; s.SubjectPassPercent = r.SubjectPassPercent ?? 33; s.OverallPassPercent = r.OverallPassPercent ?? 33;
            s.ShowRank = r.ShowRank; s.RankScope = r.RankScope ?? "SECTION"; s.AttendanceSource = r.AttendanceSource ?? "AUTO"; s.PercentageRounding = r.PercentageRounding ?? "ROUND_HALF_UP";
            s.TemplateKey = r.TemplateKey!.Trim(); s.UpdatedBy = userId; s.UpdatedAt = now;
        }

        private async Task<ApiResponse<SchemeDto>> DetailAsync(string schoolId, long schemeId, int code, string message, List<string>? extraWarnings, CancellationToken ct)
        {
            var active = await ActiveAsync(ct);
            if (active == null) return ConfigError<SchemeDto>();
            var g = await _repo.LoadGraphAsync(schoolId, schemeId, false, ct);
            if (g == null) return Result<SchemeDto>(404, "Report scheme not found.", null);
            var year = await _repo.GetYearAsync(schoolId, g.Scheme.AcademicYearId, ct);
            var statusRow = (await _repo.ListSchemesAsync(schoolId, g.Scheme.AcademicYearId, true, active.Value, ct)).FirstOrDefault(r => r.Scheme.SchemeId == schemeId);
            var classNames = await _repo.ClassNamesAsync(schoolId, g.Classes.Select(c => c.ClassId).Union(g.Subjects.Select(s => s.ClassId)).Distinct().ToList(), active.Value, ct);
            var subjectNames = await _repo.SubjectNamesAsync(schoolId, g.Subjects.Select(s => s.SubjectId).Distinct().ToList(), ct);
            var scaleNames = g.Scheme.GradeScaleId == null ? new() : await _repo.ScaleNamesAsync(schoolId, new[] { g.Scheme.GradeScaleId.Value }, ct);
            string Cls(long id) => classNames.GetValueOrDefault(id, $"Class #{id}");

            var dto = new SchemeDto
            {
                SchemeId = g.Scheme.SchemeId, SchemeName = g.Scheme.SchemeName, AcademicYearId = g.Scheme.AcademicYearId, AcademicYearName = year?.YearName ?? "", EvaluationMode = g.Scheme.EvaluationMode,
                TemplateKey = g.Scheme.TemplateKey, ClassNames = g.Classes.Select(c => Cls(c.ClassId)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(), TermCount = g.Terms.Count, ExamCount = g.Exams.Count, SubjectCount = g.Subjects.Count,
                StatusName = statusRow?.StatusName ?? "Active", GradeScaleId = g.Scheme.GradeScaleId, GradeScaleName = g.Scheme.GradeScaleId == null ? null : scaleNames.GetValueOrDefault(g.Scheme.GradeScaleId.Value),
                YearCombine = g.Scheme.YearCombine, SubjectPassPercent = g.Scheme.SubjectPassPercent, OverallPassPercent = g.Scheme.OverallPassPercent, ShowRank = g.Scheme.ShowRank, RankScope = g.Scheme.RankScope,
                AttendanceSource = g.Scheme.AttendanceSource, PercentageRounding = g.Scheme.PercentageRounding,
                Classes = g.Classes.Select(c => new SchemeClassDto { ClassId = c.ClassId, ClassName = Cls(c.ClassId) }).OrderBy(c => c.ClassName, StringComparer.OrdinalIgnoreCase).ToList(),
                Terms = g.Terms.Select(t => new SchemeTermDto
                {
                    TermId = t.TermId, Name = t.TermName, Sequence = t.Sequence, StartDate = t.StartDate, EndDate = t.EndDate, YearWeight = t.YearWeight,
                    Exams = g.Exams.Where(e => e.TermId == t.TermId).OrderBy(e => e.Sequence).Select(e => new SchemeExamDto { ExamId = e.ExamId, Name = e.ExamName, Sequence = e.Sequence, DefaultMaxMarks = e.DefaultMaxMarks, WeightPoints = e.WeightPoints, ExamDate = e.ExamDate }).ToList()
                }).ToList(),
                Subjects = g.Subjects.Select(s => new SchemeSubjectDto
                {
                    SchemeSubjectId = s.SchemeSubjectId, ClassId = s.ClassId, ClassName = Cls(s.ClassId), SubjectId = s.SubjectId, SubjectName = subjectNames.GetValueOrDefault(s.SubjectId, "Subject"), DisplayOrder = s.DisplayOrder,
                    LabelOverride = s.LabelOverride, IncludeInTotal = s.IncludeInTotal,
                    Skills = g.Skills.Where(k => k.SchemeSubjectId == s.SchemeSubjectId).OrderBy(k => k.Sequence).Select(k => new SchemeSkillDto { SkillId = k.SkillId, Name = k.SkillName, Sequence = k.Sequence }).ToList()
                }).OrderBy(s => s.ClassName, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.DisplayOrder).ThenBy(s => s.SubjectName, StringComparer.OrdinalIgnoreCase).ToList(),
                Cells = g.Cells.Select(c =>
                {
                    var exam = g.Exams.First(e => e.ExamId == c.ExamId);
                    var marks = c.EntryMode == "MARKS";
                    return new SchemeCellDto
                    {
                        ExamSubjectId = c.ExamSubjectId, ExamId = c.ExamId, SchemeSubjectId = c.SchemeSubjectId, ClassId = c.ClassId, MaxMarks = c.MaxMarks, WeightPoints = c.WeightPoints, EntryMode = c.EntryMode,
                        EffectiveMax = marks ? c.MaxMarks ?? exam.DefaultMaxMarks : null, EffectiveWeight = marks ? c.WeightPoints ?? exam.WeightPoints : null
                    };
                }).ToList()
            };
            await AddWarningsAsync(schoolId, g, dto, active.Value, Cls, ct);
            if (extraWarnings != null) dto.Warnings.AddRange(extraWarnings);
            return Result(code, message, dto);
        }

        private async Task AddWarningsAsync(string schoolId, SchemeGraph g, SchemeDto dto, int activeId, Func<long, string> cls, CancellationToken ct)
        {
            var w = dto.Warnings;
            if (g.Classes.Count == 0) w.Add("No classes yet: choose the classes this scheme is for.");
            if (g.Terms.Count == 0) w.Add("No terms or exams yet.");
            var mapped = (await _repo.MappedSubjectsAsync(schoolId, g.Classes.Select(c => c.ClassId).ToList(), activeId, ct)).Select(m => (m.ClassId, m.SubjectId)).ToHashSet();
            foreach (var c in g.Classes)
            {
                var subs = g.Subjects.Where(s => s.ClassId == c.ClassId).ToList();
                if (subs.Count == 0 && g.Terms.Count > 0) w.Add($"{cls(c.ClassId)} has no subjects in this scheme yet.");
                foreach (var s in subs.Where(s => !mapped.Contains((s.ClassId, s.SubjectId))))
                    w.Add($"{dto.Subjects.FirstOrDefault(x => x.SchemeSubjectId == s.SchemeSubjectId)?.SubjectName ?? "A subject"} is no longer taught in {cls(c.ClassId)} (Class-Subject Mapping). It stays on this scheme until you remove it.");
            }
            foreach (var e in g.Exams.Where(e => g.Subjects.Count > 0 && g.Cells.All(c => c.ExamId != e.ExamId)))
                w.Add($"{e.ExamName} has no subjects: tick the subjects it covers in the subject matrix.");
            if (g.Scheme.EvaluationMode == ReportSetup.Marks)
                foreach (var t in g.Terms.Where(t => g.Exams.Any(e => e.TermId == t.TermId) && g.Exams.Where(e => e.TermId == t.TermId).Sum(e => e.WeightPoints) != 100))
                    w.Add($"{t.TermName} adds up to {g.Exams.Where(e => e.TermId == t.TermId).Sum(e => e.WeightPoints):0.##} marks per subject, not 100. That is fine if it is intended.");
        }

        private static SchemeListDto ToList(SchemeListRow r, List<long> classIds, Dictionary<long, string> names) => new()
        {
            SchemeId = r.Scheme.SchemeId, SchemeName = r.Scheme.SchemeName, AcademicYearId = r.Scheme.AcademicYearId, AcademicYearName = r.YearName, EvaluationMode = r.Scheme.EvaluationMode,
            TemplateKey = r.Scheme.TemplateKey, ClassNames = classIds.Select(id => names.GetValueOrDefault(id, $"Class #{id}")).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
            TermCount = r.TermCount, ExamCount = r.ExamCount, SubjectCount = r.SubjectCount, StatusName = r.StatusName
        };

        private static bool IsName(DbUpdateException ex) => (ex.InnerException as PostgresException)?.ConstraintName == "uq_tb_report_schemes_year_name";
        private static string CompatMessage(ReportCardTemplates.Info? tpl, string mode) => ReportCardTemplates.Incompatible(tpl, mode);
        private static string Fit(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd();
        private Task<int?> ActiveAsync(CancellationToken ct) => _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
        private ApiResponse<T> ConfigError<T>() { _logger.LogError("Configuration problem: Active / Inactive statuses are missing in lut_status"); return Result<T>(500, "The system is not configured correctly. Please contact support.", default); }
        private static ApiResponse<T> Result<T>(int code, string message, T? data) => new() { Status = code < 400, StatusCode = code, Message = message, Data = data };
        private ApiResponse<T> Error<T>(Exception ex, string action) { _logger.LogError(ex, "Error while {Action}", action); return Result<T>(500, "Something went wrong. Please try again later.", default); }
    }
}
