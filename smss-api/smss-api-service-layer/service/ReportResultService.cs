using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.helper.reportcard;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>
    /// Results and publishing. The preview, a draft card and a publish all come from the same ReportResultBuilder, so they cannot disagree.
    /// Who: the School Admin previews, publishes and downloads; the Principal reads and downloads PUBLISHED cards (never drafts); the section's class teacher previews and downloads
    /// for their own section; a Student sees only their own current PUBLISHED cards.
    /// </summary>
    public class ReportResultService : IReportResultService
    {
        private readonly ReportResultBuilder _builder;
        private readonly IReportResultRepository _repo;
        private readonly IReportMarksRepository _marks;
        private readonly IStudentRepository _students;
        private readonly ReportAccess _access;
        private readonly IReportCardSettingsRepository _settings;
        private readonly ReportCardImageReader _images;
        private readonly ILogger<ReportResultService> _logger;

        public ReportResultService(ReportResultBuilder builder, IReportResultRepository repo, IReportMarksRepository marks, IStudentRepository students, ReportAccess access, IReportCardSettingsRepository settings, ReportCardImageReader images, ILogger<ReportResultService> logger)
        { _builder = builder; _repo = repo; _marks = marks; _students = students; _access = access; _settings = settings; _images = images; _logger = logger; }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) => new() { Status = code < 400, StatusCode = code, Message = message, Data = data };
        private ApiResponse<T> Error<T>(Exception ex, string action) { _logger.LogError(ex, "Error while {Action}", action); return Result<T>(500, "Something went wrong. Please try again later.", default); }
        private static ReportFileResult Fail(int code, string message) => new() { StatusCode = code, Message = message };
        private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5));      // the school day in India

        private static bool ValidScope(string scope, long? termId) => scope == "YEAR" ? termId == null : scope == "TERM" && termId != null;

        private static PublicationDto ToDto(PublicationRow r) => new()
        {
            PublicationId = r.PublicationId, SchemeId = r.SchemeId, SchemeName = r.SchemeName, SectionId = r.SectionId, ClassName = r.ClassName, SectionName = r.SectionName, Scope = r.Scope, TermId = r.TermId,
            TermName = r.TermName, Version = r.Version, Status = r.Status, PublishedAt = r.PublishedAt, Note = r.Note, StudentCount = r.StudentCount,
            ExcludedCount = string.IsNullOrWhiteSpace(r.ExcludedStudents) ? 0 : r.ExcludedStudents.Split(',', StringSplitOptions.RemoveEmptyEntries).Length
        };

        private async Task<ReportRights?> RightsAsync(string schoolId, ReportCaller caller, long schemeId, long sectionId, int active, CancellationToken ct)
        {
            var scheme = await _marks.GetSchemeAsync(schoolId, schemeId, ct);
            var section = await _marks.GetSectionAsync(schoolId, sectionId, active, ct);
            if (scheme == null || section == null) return null;
            return await _access.RightsAsync(schoolId, caller, scheme.AcademicYearId, section.ClassId, section.SectionId, null, active, ct);
        }

        // ============================================================================ preview
        public async Task<ApiResponse<ResultPreviewDto>> PreviewAsync(string? schoolId, ReportCaller caller, long schemeId, long sectionId, string scope, long? termId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ResultPreviewDto>(400, TenantMessages.NoSchool, null);
            try
            {
                if (!ValidScope(scope, termId)) return Result<ResultPreviewDto>(400, "Choose a term, or the whole year.", null);
                var active = await _marks.ActiveStatusIdAsync(ct);
                if (active == null) return Result<ResultPreviewDto>(500, "The system is not configured correctly. Please contact support.", null);
                var rights = await RightsAsync(schoolId, caller, schemeId, sectionId, active.Value, ct);
                if (rights == null) return Result<ResultPreviewDto>(404, "Report scheme or section not found.", null);
                if (!(rights.IsAdmin || rights.IsPrincipal || rights.IsClassTeacher)) return Result<ResultPreviewDto>(403, "You can only see the results of the section you are the class teacher of.", null);
                var set = await _builder.BuildAsync(schoolId, schemeId, sectionId, scope, termId, active.Value, Today(), ct);
                if (set == null) return Result<ResultPreviewDto>(404, "Report scheme, section or term not found.", null);
                var current = await _repo.GetCurrentAsync(schoolId, schemeId, sectionId, scope, termId, false, ct);
                PublicationDto? currentDto = null;
                if (current != null) { var row = await _repo.GetPublicationAsync(schoolId, current.PublicationId, ct); if (row != null) currentDto = ToDto(row); }

                var dto = new ResultPreviewDto
                {
                    SchemeId = set.SchemeId, SchemeName = set.SchemeName, SectionId = set.SectionId, ClassName = set.ClassName, SectionName = set.SectionName, Scope = scope, TermId = set.TermId, TermName = set.TermName,
                    Mode = set.Mode, TemplateKey = set.TemplateKey, TemplateAvailable = set.TemplateAvailable, TemplateMessage = set.TemplateMessage, ShowRank = set.Models.Values.FirstOrDefault()?.ShowRank ?? false,
                    UnlockedTerms = set.UnlockedTerms, AttendanceSource = set.AttendanceSource, UnmarkedDates = set.UnmarkedDates.Select(d => d.ToString("yyyy-MM-dd")).ToList(), Current = currentDto,
                    CanPublish = rights.IsAdmin && set.UnlockedTerms.Count == 0 && set.TemplateAvailable, CanDownloadDraft = (rights.IsAdmin || rights.IsClassTeacher) && set.TemplateAvailable
                };
                foreach (var s in set.Students)
                {
                    var o = set.Models[s.StudentId].Overall;
                    dto.Students.Add(new ResultStudentDto
                    {
                        StudentId = s.StudentId, Name = LeaveRules.FullName(s.FirstName, s.MiddleName, s.LastName), RollNumber = s.RollNumber ?? "", AdmissionNumber = s.AdmissionNumber, Result = set.StatusOf(s.StudentId),
                        Obtained = o.Obtained, OutOf = o.OutOf, Percent = o.Percent, Grade = o.Grade, Rank = o.Rank, FailedSubjects = o.FailedSubjects
                    });
                }
                dto.IncompleteCount = dto.Students.Count(s => s.Result == "INCOMPLETE");
                return Result(200, "Successfully retrieved", dto);
            }
            catch (Exception ex) { return Error<ResultPreviewDto>(ex, "previewing report card results"); }
        }

        // ============================================================================ publish
        public async Task<ApiResponse<PublishResultDto>> PublishAsync(string? schoolId, ReportCaller caller, PublishRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<PublishResultDto>(400, TenantMessages.NoSchool, null);
            try
            {
                if (!caller.IsAdmin) return Result<PublishResultDto>(403, "Only a School Admin can publish report cards.", null);
                var scope = req.Scope ?? "";
                if (!ValidScope(scope, req.TermId)) return Result<PublishResultDto>(400, "Choose a term, or the whole year.", null);
                var note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
                if (note != null && note.Length > 300) return Result<PublishResultDto>(400, "The note can be at most 300 characters.", null);
                var active = await _marks.ActiveStatusIdAsync(ct);
                if (active == null) return Result<PublishResultDto>(500, "The system is not configured correctly. Please contact support.", null);

                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _settings.LockSettingsKeyAsync(schoolId, ct);       // the settings (and the signature file) cannot change while this card set is being frozen
                await _repo.LockPublicationKeyAsync(req.SchemeId, req.SectionId, scope, req.TermId, ct);       // two admins publishing the same card set queue up; the second sees the first one's version
                var set = await _builder.BuildAsync(schoolId, req.SchemeId, req.SectionId, scope, req.TermId, active.Value, Today(), ct);       // recomputed INSIDE the lock: what is stored is what is true now
                if (set == null) return Result<PublishResultDto>(404, "Report scheme, section or term not found.", null);
                if (!set.TemplateAvailable) return Result<PublishResultDto>(409, set.TemplateMessage ?? "This report card uses a design that no longer exists. Choose a design for the scheme on Report Card Settings.", null);
                if (set.UnlockedTerms.Count > 0)
                    return Result<PublishResultDto>(409, $"Lock {(set.UnlockedTerms.Count == 1 ? "the term" : "every term")} first. Not locked for this section: {string.Join(", ", set.UnlockedTerms)}.", null);

                var included = set.Students.Where(s => set.StatusOf(s.StudentId) != "INCOMPLETE").ToList();
                var excluded = set.Students.Where(s => set.StatusOf(s.StudentId) == "INCOMPLETE").ToList();
                if (included.Count == 0) return Result<PublishResultDto>(409, "No student in this section has a complete result yet, so there is nothing to publish.", null);

                var excludedDtos = excluded.Select(s => new ExcludedStudentDto { StudentId = s.StudentId, Name = LeaveRules.FullName(s.FirstName, s.MiddleName, s.LastName), RollNumber = s.RollNumber ?? "" }).ToList();
                if (excluded.Count > 0 && !req.ConfirmExcluded)
                {
                    var names = string.Join(", ", excludedDtos.Take(10).Select(e => e.Name)) + (excludedDtos.Count > 10 ? $" and {excludedDtos.Count - 10} more" : "");
                    return new ApiResponse<PublishResultDto>
                    {
                        Status = false, StatusCode = 409,
                        Message = $"{excluded.Count} student{(excluded.Count == 1 ? "" : "s")} will be left out because the result is incomplete: {names}. {(excluded.Count == 1 ? "They" : "They")} can be included in a later version once the marks are in and the term is locked again. Publish anyway?",
                        Data = new PublishResultDto { NeedsConfirmation = true, Reason = "EXCLUDED", Excluded = excludedDtos }
                    };
                }
                if (set.UnmarkedDates.Count > 0 && !req.AcknowledgeAttendance)
                    return new ApiResponse<PublishResultDto>
                    {
                        Status = false, StatusCode = 409,
                        Message = $"Attendance was not marked on {set.UnmarkedDates.Count} school day{(set.UnmarkedDates.Count == 1 ? "" : "s")} in this period, so those days are not counted. Publish anyway?",
                        Data = new PublishResultDto { NeedsConfirmation = true, Reason = "ATTENDANCE", UnmarkedDates = set.UnmarkedDates.Select(d => d.ToString("yyyy-MM-dd")).ToList() }
                    };

                var now = DateTime.UtcNow;
                var previous = await _repo.GetCurrentAsync(schoolId, req.SchemeId, req.SectionId, scope, req.TermId, true, ct);
                var version = await _repo.MaxVersionAsync(schoolId, req.SchemeId, req.SectionId, scope, req.TermId, ct) + 1;
                if (previous != null) { previous.Status = "SUPERSEDED"; await _repo.SaveChangesAsync(ct); }      // first, so the one-current-version index is never violated

                set.Config.Excluded = excludedDtos.Select(e => new RcExcluded { StudentId = e.StudentId, Name = e.Name, Roll = e.RollNumber }).ToList();
                var publication = new TbReportPublication
                {
                    SchoolId = schoolId, SchemeId = set.SchemeId, SectionId = set.SectionId, ClassId = set.ClassId, Scope = scope, TermId = set.TermId, Version = version, Status = "PUBLISHED", PublishedBy = caller.UserId,
                    PublishedAt = now, Note = note, ConfigSnapshot = JsonSerializer.Serialize(set.Config, ReportCardModel.Json), ExcludedStudents = excluded.Count == 0 ? null : string.Join(",", excluded.Select(s => s.StudentId)), StudentCount = included.Count
                };
                _repo.Add(publication);
                await _repo.SaveChangesAsync(ct);

                var stamp = now.ToString("dd MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
                _repo.AddRange(included.Select(s =>
                {
                    var model = set.Models[s.StudentId]; model.Version = version; model.PublishedAt = stamp; var o = model.Overall;
                    return (object)new TbReportPublicationStudent
                    {
                        PublicationId = publication.PublicationId, SchoolId = schoolId, StudentId = s.StudentId, StudentName = model.Student.Name, AdmissionNumber = s.AdmissionNumber, RollNumber = s.RollNumber,
                        ResultStatus = o.Result, TotalObtained = o.Obtained, TotalMax = o.OutOf, Percentage = o.Percent, GradeCode = o.Grade, RankPosition = o.Rank, CardModel = model.ToJson()
                    };
                }));
                _repo.Add(new TbReportAudit
                {
                    SchoolId = schoolId, Event = "PUBLISH", SchemeId = set.SchemeId, TermId = set.TermId, SectionId = set.SectionId, FromStatus = previous == null ? null : "PUBLISHED", ToStatus = "PUBLISHED", Reason = note,
                    ExcludedStudents = publication.ExcludedStudents, PublicationId = publication.PublicationId, ActorUserId = caller.UserId, CreatedAt = now
                });
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                var row = await _repo.GetPublicationAsync(schoolId, publication.PublicationId, ct);
                return Result(200, excluded.Count == 0 ? $"Published version {version}." : $"Published version {version}. {excluded.Count} student{(excluded.Count == 1 ? " was" : "s were")} left out.",
                    new PublishResultDto { Published = true, Excluded = excludedDtos, Publication = row == null ? null : ToDto(row) });
            }
            catch (DbUpdateException ex) when ((ex.InnerException as PostgresException)?.SqlState == PostgresErrorCodes.UniqueViolation)
            { return Result<PublishResultDto>(409, "Someone else just published this. Refresh and check the versions.", null); }
            catch (Exception ex) { return Error<PublishResultDto>(ex, "publishing report cards"); }
        }

        // ============================================================================ versions
        public async Task<ApiResponse<List<PublicationDto>>> ListAsync(string? schoolId, ReportCaller caller, long? sectionId, long? schemeId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<PublicationDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await _marks.ActiveStatusIdAsync(ct);
                if (active == null) return Result<List<PublicationDto>>(500, "The system is not configured correctly. Please contact support.", null);
                if (!caller.IsAdmin && !caller.IsPrincipal)
                {
                    // a class teacher sees the versions of their own section only, and has to say which
                    if (sectionId == null || schemeId == null) return Result<List<PublicationDto>>(403, "Choose a section you are the class teacher of.", null);
                    var rights = await RightsAsync(schoolId, caller, schemeId.Value, sectionId.Value, active.Value, ct);
                    if (rights == null) return Result<List<PublicationDto>>(404, "Report scheme or section not found.", null);
                    if (!rights.IsClassTeacher) return Result<List<PublicationDto>>(403, "You can only see the published cards of the section you are the class teacher of.", null);
                }
                var rows = await _repo.ListPublicationsAsync(schoolId, sectionId, schemeId, ct);
                return Result(200, "Successfully retrieved", rows.Select(ToDto).ToList());
            }
            catch (Exception ex) { return Error<List<PublicationDto>>(ex, "listing report card versions"); }
        }

        /// <summary>The publication, when the caller may read it: School Admin and Principal for any section of the school, a teacher for their own section only. Null + failure otherwise.</summary>
        private async Task<(PublicationRow? Row, int Code, string Message)> AuthorisePublicationAsync(string schoolId, ReportCaller caller, long publicationId, CancellationToken ct)
        {
            var row = await _repo.GetPublicationAsync(schoolId, publicationId, ct);
            if (row == null) return (null, 404, "Published report cards not found.");
            if (caller.IsAdmin || caller.IsPrincipal) return (row, 200, "");
            var active = await _marks.ActiveStatusIdAsync(ct);
            if (active == null) return (null, 500, "The system is not configured correctly. Please contact support.");
            var rights = await RightsAsync(schoolId, caller, row.SchemeId, row.SectionId, active.Value, ct);
            return rights is { IsClassTeacher: true } ? (row, 200, "") : (null, 403, "You can only see the published cards of the section you are the class teacher of.");
        }

        public async Task<ApiResponse<PublicationDetailDto>> GetAsync(string? schoolId, ReportCaller caller, long publicationId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<PublicationDetailDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var (row, code, message) = await AuthorisePublicationAsync(schoolId, caller, publicationId, ct);
                if (row == null) return Result<PublicationDetailDto>(code, message, null);
                var cards = await _repo.GetCardRowsAsync(publicationId, ct);
                var entity = await _repo.GetPublicationEntityAsync(schoolId, publicationId, ct);
                var excluded = new List<ExcludedStudentDto>();
                if (entity != null)
                {
                    var cfg = JsonSerializer.Deserialize<RcConfigSnapshot>(entity.ConfigSnapshot, ReportCardModel.Json);
                    excluded = cfg?.Excluded.Select(e => new ExcludedStudentDto { StudentId = e.StudentId, Name = e.Name, RollNumber = e.Roll }).ToList() ?? new();
                }
                return Result(200, "Successfully retrieved", new PublicationDetailDto
                {
                    Publication = ToDto(row), Excluded = excluded,
                    Cards = cards.Select(c => new PublicationCardDto { StudentId = c.StudentId, Name = c.StudentName, AdmissionNumber = c.AdmissionNumber, RollNumber = c.RollNumber ?? "", Result = c.ResultStatus, Obtained = c.TotalObtained, OutOf = c.TotalMax, Percent = c.Percentage, Grade = c.GradeCode, Rank = c.RankPosition }).ToList()
                });
            }
            catch (Exception ex) { return Error<PublicationDetailDto>(ex, "reading a publication"); }
        }

        // ============================================================================ PDFs
        private async Task<byte[]> RenderAsync(ReportCardModel model, bool draft, string? signatureKeyFallback, CancellationToken ct)
        {
            var ctx = _images.Context(model, draft, signatureKeyFallback);
            return await Task.Run(() => ReportCardPdf.Render(ctx), ct);
        }

        private static string FileName(string name, string suffix)
        {
            var safe = new string(name.Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_').ToArray());
            return $"ReportCard_{(safe.Length == 0 ? "student" : safe)}_{suffix}.pdf";
        }

        private async Task<ReportFileResult> StoredPdfAsync(string schoolId, TbReportPublicationStudent card, TbReportPublication pub, CancellationToken ct)
        {
            var model = ReportCardModel.FromJson(card.CardModel);
            var cfg = JsonSerializer.Deserialize<RcConfigSnapshot>(pub.ConfigSnapshot, ReportCardModel.Json);
            if (ReportCardDesigns.Find(model.TemplateKey) == null) return Fail(409, "This report card uses a design that no longer exists. Choose a design for the scheme on Report Card Settings.");
            var pdf = await RenderAsync(model, false, cfg?.SignatureKey, ct);
            return new ReportFileResult { Content = pdf, FileName = FileName(card.AdmissionNumber, $"v{pub.Version}") };
        }

        public async Task<ReportFileResult> CardPdfAsync(string? schoolId, ReportCaller caller, long publicationId, string studentId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var (row, code, message) = await AuthorisePublicationAsync(schoolId, caller, publicationId, ct);
                if (row == null) return Fail(code, message);
                var card = await _repo.GetCardAsync(schoolId, publicationId, studentId, ct);
                var pub = await _repo.GetPublicationEntityAsync(schoolId, publicationId, ct);
                if (card == null || pub == null) return Fail(404, "That student has no card in this version.");
                return await StoredPdfAsync(schoolId, card, pub, ct);
            }
            catch (Exception ex) { _logger.LogError(ex, "Error while drawing a published report card"); return Fail(500, "Something went wrong. Please try again later."); }
        }

        public async Task<ReportFileResult> DraftPdfAsync(string? schoolId, ReportCaller caller, long schemeId, long sectionId, string scope, long? termId, string studentId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                if (!ValidScope(scope, termId)) return Fail(400, "Choose a term, or the whole year.");
                var active = await _marks.ActiveStatusIdAsync(ct);
                if (active == null) return Fail(500, "The system is not configured correctly. Please contact support.");
                var rights = await RightsAsync(schoolId, caller, schemeId, sectionId, active.Value, ct);
                if (rights == null) return Fail(404, "Report scheme or section not found.");
                if (!(rights.IsAdmin || rights.IsClassTeacher)) return Fail(403, rights.IsPrincipal ? "The Principal can download published report cards, not drafts." : "You can only preview the cards of the section you are the class teacher of.");
                var set = await _builder.BuildAsync(schoolId, schemeId, sectionId, scope, termId, active.Value, Today(), ct);
                if (set == null) return Fail(404, "Report scheme, section or term not found.");
                if (!set.TemplateAvailable) return Fail(409, set.TemplateMessage ?? "This report card uses a design that no longer exists. Choose a design for the scheme on Report Card Settings.");
                if (!set.Models.TryGetValue(studentId, out var model)) return Fail(404, "Student not found in this section.");
                var pdf = await RenderAsync(model, true, set.Config.SignatureKey, ct);
                return new ReportFileResult { Content = pdf, FileName = FileName(set.Students.First(s => s.StudentId == studentId).AdmissionNumber, "DRAFT") };
            }
            catch (Exception ex) { _logger.LogError(ex, "Error while drawing a draft report card"); return Fail(500, "Something went wrong. Please try again later."); }
        }

        // ============================================================================ bulk PDF
        // at most ReportCards:BulkMaxConcurrent bulk renders at a time per process (default 2): a small server stays responsive. Created on first use, after configuration has been read.
        private static readonly Lazy<SemaphoreSlim> BulkGateLazy = new(() => new SemaphoreSlim(ReportCardBulk.MaxConcurrent, ReportCardBulk.MaxConcurrent));
        private static SemaphoreSlim BulkGate => BulkGateLazy.Value;

        public async Task<BulkPdfResult> BulkPdfAsync(string? schoolId, ReportCaller caller, long publicationId, BulkRequest? request, CancellationToken ct)
        {
            static BulkPdfResult Fail(int code, string message) => new() { StatusCode = code, Message = message };
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var (row, code, message) = await AuthorisePublicationAsync(schoolId, caller, publicationId, ct);
                if (row == null) return Fail(code, message);
                var pub = await _repo.GetPublicationEntityAsync(schoolId, publicationId, ct);
                if (pub == null) return Fail(404, "Published report cards not found.");
                var rows = (await _repo.GetBulkCardsAsync(publicationId, ct))
                    .OrderBy(r => r.RollNumber, RollComparer.Instance).ThenBy(r => r.StudentName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.AdmissionNumber, StringComparer.Ordinal).ToList();
                if (rows.Count == 0) return Fail(404, "This version has no cards.");
                static string Key(BulkCardRow r) => string.IsNullOrWhiteSpace(r.RollNumber) ? r.AdmissionNumber : r.RollNumber!;

                var selected = rows; var from = request?.FromRoll?.Trim(); var to = request?.ToRoll?.Trim();
                if (!string.IsNullOrEmpty(from) || !string.IsNullOrEmpty(to))
                {
                    var i = string.IsNullOrEmpty(from) ? 0 : rows.FindIndex(r => string.Equals(Key(r), from, StringComparison.OrdinalIgnoreCase));
                    var j = string.IsNullOrEmpty(to) ? rows.Count - 1 : rows.FindLastIndex(r => string.Equals(Key(r), to, StringComparison.OrdinalIgnoreCase));
                    if (i < 0 || j < 0) return Fail(400, $"There is no card with roll number {(i < 0 ? from : to)} in this version.");
                    if (j < i) return Fail(400, "The last roll number comes before the first one.");
                    selected = rows.GetRange(i, j - i + 1);
                }
                if (selected.Count > ReportCardBulk.MaxStudentsPerRequest)
                {
                    var parts = new List<BulkPartDto>();
                    for (var k = 0; k < selected.Count; k += ReportCardBulk.MaxStudentsPerRequest)
                    {
                        var chunk = selected.Skip(k).Take(ReportCardBulk.MaxStudentsPerRequest).ToList();
                        parts.Add(new BulkPartDto { FromRoll = Key(chunk[0]), ToRoll = Key(chunk[^1]), Count = chunk.Count });
                    }
                    return new BulkPdfResult
                    {
                        StatusCode = 409, TooLarge = new BulkTooLargeDto { Total = selected.Count, MaxPerRequest = ReportCardBulk.MaxStudentsPerRequest, Parts = parts },
                        Message = $"This version has {selected.Count} cards and one file can hold at most {ReportCardBulk.MaxStudentsPerRequest}. Download it in parts: {string.Join("; ", parts.Select(p => $"roll {p.FromRoll} to {p.ToRoll} ({p.Count} cards)"))}."
                    };
                }

                if (!await BulkGate.WaitAsync(TimeSpan.FromSeconds(15), ct)) return Fail(503, "Other report card files are being prepared right now. Please try again in a minute.");
                try
                {
                    var cfg = JsonSerializer.Deserialize<RcConfigSnapshot>(pub.ConfigSnapshot, ReportCardModel.Json);
                    // the images are the same for every card of one publication (the keys are in the stored models): read each ONCE
                    var logoMemo = new Dictionary<string, byte[]?>(); var sigMemo = new Dictionary<string, byte[]?>();
                    byte[]? Logo(string? key) => string.IsNullOrWhiteSpace(key) ? null : logoMemo.TryGetValue(key, out var b) ? b : logoMemo[key] = _images.Logo(key);
                    (string? Key, byte[]? Bytes) lastPhoto = (null, null);       // only the last photo is kept: every student has their own, and holding 100 of them is what costs memory
                    byte[]? Photo(ReportCardModel mm) { if (!mm.ShowPhoto || string.IsNullOrWhiteSpace(mm.Student.PhotoKey)) return null; if (lastPhoto.Key != mm.Student.PhotoKey) lastPhoto = (mm.Student.PhotoKey, _images.Photo(mm.Student.PhotoKey)); return lastPhoto.Bytes; }
                    byte[]? Sig(string? key) => string.IsNullOrWhiteSpace(key) ? null : sigMemo.TryGetValue(key, out var b) ? b : sigMemo[key] = _images.Signature(key);
                    var contexts = selected.Select(r =>
                    {
                        var model = ReportCardModel.FromJson(r.CardModel);
                        return new RcContext { Model = model, Logo = Logo(model.Header.LogoKey), Signature = Sig(model.SignatureKey ?? cfg?.SignatureKey), Photo = Photo(model), Draft = false };
                    });
                    var first = ReportCardModel.FromJson(selected[0].CardModel);
                    if (ReportCardDesigns.Find(first.TemplateKey) == null) return Fail(409, "This report card uses a design that no longer exists. Choose a design for the scheme on Report Card Settings.");
                    var pdf = await Task.Run(() => ReportCardPdf.RenderMany(contexts), ct);
                    var label = (pub.Scope == "TERM" ? row.TermName ?? "Term" : "Year");
                    var range = selected.Count == rows.Count ? "" : $"_rolls-{Key(selected[0])}-to-{Key(selected[^1])}";
                    return new BulkPdfResult { Content = pdf, FileName = FileName($"{row.ClassName}{row.SectionName}_{label}", $"v{pub.Version}{range}").Replace("ReportCard_", "ReportCards_"), Cards = selected.Count };
                }
                finally { BulkGate.Release(); }
            }
            catch (OperationCanceledException) { return Fail(499, "Cancelled."); }
            catch (Exception ex) { _logger.LogError(ex, "Error while drawing a bulk report card PDF"); return Fail(500, "Something went wrong. Please try again later."); }
        }

        // ============================================================================ the student's own
        private async Task<string?> MyStudentIdAsync(string schoolId, long userId, CancellationToken ct) => await _students.GetUserLoginIdAsync(schoolId, userId, ct);

        private static MyReportCardDto ToDto(MyCardRow r) => new()
        {
            PublicationId = r.PublicationId, SchemeName = r.SchemeName, ClassName = r.ClassName, SectionName = r.SectionName, Scope = r.Scope, TermName = r.TermName, Version = r.Version, PublishedAt = r.PublishedAt,
            Result = r.ResultStatus, Percent = r.Percentage, Grade = r.GradeCode, Rank = r.RankPosition
        };

        public async Task<ApiResponse<List<MyReportCardDto>>> MyCardsAsync(string? schoolId, long userId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<MyReportCardDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var me = await MyStudentIdAsync(schoolId, userId, ct);
                if (string.IsNullOrEmpty(me)) return Result<List<MyReportCardDto>>(404, "No student record found for this login.", null);
                return Result(200, "Successfully retrieved", (await _repo.GetMyCardsAsync(schoolId, me, ct)).Select(ToDto).ToList());
            }
            catch (Exception ex) { return Error<List<MyReportCardDto>>(ex, "listing a student's own report cards"); }
        }

        public async Task<ApiResponse<MyReportCardDetailDto>> MyCardAsync(string? schoolId, long userId, long publicationId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<MyReportCardDetailDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var me = await MyStudentIdAsync(schoolId, userId, ct);
                if (string.IsNullOrEmpty(me)) return Result<MyReportCardDetailDto>(404, "No student record found for this login.", null);
                var row = await _repo.GetMyCardAsync(schoolId, me, publicationId, ct);
                var card = row == null ? null : await _repo.GetCardAsync(schoolId, publicationId, me, ct);
                if (row == null || card == null) return Result<MyReportCardDetailDto>(404, "Report card not found.", null);
                return Result(200, "Successfully retrieved", new MyReportCardDetailDto { Card = ToDto(row), Model = ReportCardModel.FromJson(card.CardModel) });
            }
            catch (Exception ex) { return Error<MyReportCardDetailDto>(ex, "reading a student's own report card"); }
        }

        public async Task<ReportFileResult> MyCardPdfAsync(string? schoolId, long userId, long publicationId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var me = await MyStudentIdAsync(schoolId, userId, ct);
                if (string.IsNullOrEmpty(me)) return Fail(404, "No student record found for this login.");
                var row = await _repo.GetMyCardAsync(schoolId, me, publicationId, ct);       // current PUBLISHED version of their own card only
                var card = row == null ? null : await _repo.GetCardAsync(schoolId, publicationId, me, ct);
                var pub = card == null ? null : await _repo.GetPublicationEntityAsync(schoolId, publicationId, ct);
                if (card == null || pub == null) return Fail(404, "Report card not found.");
                return await StoredPdfAsync(schoolId, card, pub, ct);
            }
            catch (Exception ex) { _logger.LogError(ex, "Error while drawing a student's own report card"); return Fail(500, "Something went wrong. Please try again later."); }
        }
    }
}
