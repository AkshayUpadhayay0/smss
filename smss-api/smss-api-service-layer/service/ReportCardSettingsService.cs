using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.helper.reportcard;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class ReportCardSettingsService : IReportCardSettingsService
    {
        private readonly IReportCardSettingsRepository _repo;
        private readonly IStudentIdCardRepository _school;
        private readonly IReportMarksRepository _marks;
        private readonly ReportAccess _access;
        private readonly ReportResultBuilder _builder;
        private readonly IFileStorageService _storage;
        private readonly ReportCardImageReader _images;
        private readonly ILogger<ReportCardSettingsService> _logger;

        public ReportCardSettingsService(IReportCardSettingsRepository repo, IStudentIdCardRepository school, IReportMarksRepository marks, ReportAccess access, ReportResultBuilder builder,
            IFileStorageService storage, ReportCardImageReader images, ILogger<ReportCardSettingsService> logger)
        { _repo = repo; _school = school; _marks = marks; _access = access; _builder = builder; _storage = storage; _images = images; _logger = logger; }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) => new() { Status = code < 400, StatusCode = code, Message = message, Data = data };
        private ApiResponse<T> Error<T>(Exception ex, string action) { _logger.LogError(ex, "Error while {Action}", action); return Result<T>(500, "Something went wrong. Please try again later.", default); }

        // ============================================================================ read
        public async Task<ApiResponse<ReportCardSettingsDto>> GetAsync(string? schoolId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ReportCardSettingsDto>(400, TenantMessages.NoSchool, null);
            try { return Result(200, "Successfully fetched", await BuildDtoAsync(schoolId, await _repo.GetAsync(schoolId, ct), ct)); }
            catch (Exception ex) { return Error<ReportCardSettingsDto>(ex, "fetching report card settings"); }
        }

        private async Task<ReportCardSettingsDto> BuildDtoAsync(string schoolId, TbSchoolReportCardSettings? row, CancellationToken ct)
        {
            var school = await _school.GetSchoolAsync(schoolId, ct);
            var active = await _marks.ActiveStatusIdAsync(ct);
            var schemes = active == null ? new List<SchemeDesignRow>() : await _repo.GetSchemesAsync(schoolId, active.Value, ct);
            return new ReportCardSettingsDto
            {
                PrimaryColor = row?.PrimaryColor ?? ReportCardSettingsRules.DefaultPrimary, SecondaryColor = row?.SecondaryColor ?? ReportCardSettingsRules.DefaultSecondary,
                DefaultPrimaryColor = ReportCardSettingsRules.DefaultPrimary, DefaultSecondaryColor = ReportCardSettingsRules.DefaultSecondary,
                HeaderLine = row?.HeaderLine, RecognitionText = row?.RecognitionText, UdiseCode = row?.UdiseCode, FooterText = row?.FooterText, RulesText = row?.RulesText,
                HasSignature = !string.IsNullOrEmpty(row?.PrincipalSignaturePath), ShowCover = row?.ShowCover ?? false, ShowRules = row?.ShowRules ?? false,
                ShowAttendance = row?.ShowAttendance ?? true, ShowGradeLegend = row?.ShowGradeLegend ?? true, IsDefault = row == null, UpdatedAt = row?.UpdatedAt,
                SchoolName = school?.SchoolName ?? "", HasLogo = !string.IsNullOrWhiteSpace(school?.LogoUrl),
                Designs = ReportCardTemplates.All.Select(t => new ReportCardDesignDto { Code = t.Key, Name = t.Name, Description = t.Description, Modes = t.Modes.ToList(), Orientation = t.Orientation, Available = ReportCardDesigns.Find(t.Key) != null }).ToList(),
                Schemes = schemes.Select(s => new ReportCardSchemeChoiceDto { SchemeId = s.SchemeId, SchemeName = s.SchemeName, EvaluationMode = s.EvaluationMode, TemplateKey = s.TemplateKey, ClassNames = s.ClassNames }).ToList(),
            };
        }

        // ============================================================================ save
        private static TbSchoolReportCardSettings NewRow(string schoolId, DateTime now) => new() { SchoolId = schoolId, CreatedAt = now, UpdatedAt = now };

        public async Task<ApiResponse<ReportCardSettingsDto>> SaveAsync(string? schoolId, long userId, ReportCardSettingsRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ReportCardSettingsDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var (clean, error) = ReportCardSettingsRules.Validate(request);
                if (clean == null) return Result<ReportCardSettingsDto>(400, error!, null);
                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockSettingsKeyAsync(schoolId, ct);            // a publish in flight finishes with the old settings first
                var row = await _repo.GetForUpdateAsync(schoolId, ct); var now = DateTime.UtcNow;
                if (row == null) { row = NewRow(schoolId, now); _repo.Add(row); }
                row.PrimaryColor = clean.PrimaryColor; row.SecondaryColor = clean.SecondaryColor; row.HeaderLine = clean.HeaderLine; row.RecognitionText = clean.RecognitionText;
                row.UdiseCode = clean.UdiseCode; row.FooterText = clean.FooterText; row.RulesText = clean.RulesText;
                row.ShowCover = clean.ShowCover; row.ShowRules = clean.ShowRules; row.ShowAttendance = clean.ShowAttendance; row.ShowGradeLegend = clean.ShowGradeLegend;
                row.UpdatedBy = userId; row.UpdatedAt = now;
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return Result(200, "Report card settings saved.", await BuildDtoAsync(schoolId, row, ct));
            }
            catch (Exception ex) { return Error<ReportCardSettingsDto>(ex, "saving report card settings"); }
        }

        // ============================================================================ signature
        public async Task<ApiResponse<ReportCardSignatureDto>> UploadSignatureAsync(string? schoolId, long userId, Stream content, string fileName, long length, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ReportCardSignatureDto>(400, TenantMessages.NoSchool, null);
            string? savedKey = null; var committed = false;
            try
            {
                var (image, error) = await ImageUploadValidator.ValidateAsync(content, fileName, length, "Signature", ct);
                if (image == null) return Result<ReportCardSignatureDto>(400, error!, null);
                using var _ = image;
                var school = await _school.GetSchoolAsync(schoolId, ct);
                if (school == null) return Result<ReportCardSignatureDto>(404, "School not found.", null);
                if (!SchoolCodeRules.IsFolderSafe(school.SchoolCode)) return Result<ReportCardSignatureDto>(400, "This school's code can't be used as a folder name. Please contact support.", null);

                savedKey = await _storage.SaveReportCardSignatureAsync(school.SchoolCode, image.Content, image.Extension, ct);   // a NEW file name every time
                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockSettingsKeyAsync(schoolId, ct);
                var row = await _repo.GetForUpdateAsync(schoolId, ct); var now = DateTime.UtcNow;
                if (row == null) { row = NewRow(schoolId, now); _repo.Add(row); }
                var old = row.PrincipalSignaturePath;
                row.PrincipalSignaturePath = savedKey; row.UpdatedBy = userId; row.UpdatedAt = now;
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                committed = true;
                await DeleteIfUnreferencedAsync(schoolId, old, ct);
                return Result(200, "Signature uploaded.", new ReportCardSignatureDto { HasSignature = true });
            }
            catch (Exception ex)
            {
                if (savedKey != null && !committed) _storage.DeletePrivate(savedKey);    // not saved: nothing refers to it yet
                return Error<ReportCardSignatureDto>(ex, "uploading the report card signature");
            }
        }

        public async Task<ApiResponse<ReportCardSignatureDto>> RemoveSignatureAsync(string? schoolId, long userId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ReportCardSignatureDto>(400, TenantMessages.NoSchool, null);
            try
            {
                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockSettingsKeyAsync(schoolId, ct);
                var row = await _repo.GetForUpdateAsync(schoolId, ct);
                if (row?.PrincipalSignaturePath is not { Length: > 0 } old) return Result(200, "There is no signature on file.", new ReportCardSignatureDto { HasSignature = false });
                row.PrincipalSignaturePath = null; row.UpdatedBy = userId; row.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                await DeleteIfUnreferencedAsync(schoolId, old, ct);
                return Result(200, "Signature removed.", new ReportCardSignatureDto { HasSignature = false });
            }
            catch (Exception ex) { return Error<ReportCardSignatureDto>(ex, "removing the report card signature"); }
        }

        /// <summary>A signature file that a published card was drawn with is NEVER deleted: that card must keep drawing the same signature.</summary>
        private async Task DeleteIfUnreferencedAsync(string schoolId, string? key, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (await _repo.SignatureInUseAsync(schoolId, key, ct)) return;
            _storage.DeletePrivate(key);
        }

        // ============================================================================ the design of a scheme
        public async Task<ApiResponse<ReportCardSettingsDto>> SetSchemeDesignAsync(string? schoolId, long userId, SetSchemeDesignRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ReportCardSettingsDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var key = (request.TemplateKey ?? "").Trim();
                var info = ReportCardTemplates.Find(key);
                if (info == null) return Result<ReportCardSettingsDto>(400, "Unknown design.", null);
                var design = ReportCardDesigns.Find(key);
                if (design == null) return Result<ReportCardSettingsDto>(400, "Unknown design.", null);
                var active = await _marks.ActiveStatusIdAsync(ct);
                if (active == null) return Result<ReportCardSettingsDto>(500, "The system is not configured correctly. Please contact support.", null);
                var scheme = (await _repo.GetSchemesAsync(schoolId, active.Value, ct)).FirstOrDefault(s => s.SchemeId == request.SchemeId);
                if (scheme == null) return Result<ReportCardSettingsDto>(404, "Report scheme not found.", null);
                if (!design.SupportedModes.Contains(scheme.EvaluationMode)) return Result<ReportCardSettingsDto>(409, ReportCardTemplates.Incompatible(info, scheme.EvaluationMode), null);
                if (!await _repo.SetSchemeTemplateAsync(schoolId, request.SchemeId, key, userId, active.Value, ct)) return Result<ReportCardSettingsDto>(404, "Report scheme not found.", null);
                return Result(200, "Design changed. Cards already published keep their design.", await BuildDtoAsync(schoolId, await _repo.GetAsync(schoolId, ct), ct));
            }
            catch (Exception ex) { return Error<ReportCardSettingsDto>(ex, "choosing a report card design"); }
        }

        // ============================================================================ preview
        public async Task<ApiResponse<ReportCardPreviewDto>> PreviewAsync(string? schoolId, ReportCaller caller, ReportCardPreviewRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ReportCardPreviewDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var key = string.IsNullOrWhiteSpace(request.TemplateKey) ? "term-spread" : request.TemplateKey.Trim();
                if (ReportCardTemplates.Find(key) == null) return Result<ReportCardPreviewDto>(400, "Unknown design.", null);
                var design = ReportCardDesigns.Find(key);
                if (design == null) return Result<ReportCardPreviewDto>(400, "Unknown design.", null);

                var saved = await _repo.GetAsync(schoolId, ct);
                var (clean, error) = ReportCardSettingsRules.Validate(request.Settings ?? new ReportCardSettingsRequest
                {
                    PrimaryColor = saved?.PrimaryColor, SecondaryColor = saved?.SecondaryColor, HeaderLine = saved?.HeaderLine, RecognitionText = saved?.RecognitionText, UdiseCode = saved?.UdiseCode, FooterText = saved?.FooterText, RulesText = saved?.RulesText,
                    ShowCover = saved?.ShowCover ?? false, ShowRules = saved?.ShowRules ?? false, ShowAttendance = saved?.ShowAttendance ?? true, ShowGradeLegend = saved?.ShowGradeLegend ?? true
                });
                if (clean == null) return Result<ReportCardPreviewDto>(400, error!, null);
                var signatureKey = saved?.PrincipalSignaturePath;       // the saved signature file; uploads are saved at once, so the preview never needs an unsaved one
                var school = await _school.GetSchoolAsync(schoolId, ct);

                ReportCardModel model; var isSample = true;
                if (!string.IsNullOrWhiteSpace(request.StudentId))
                {
                    if (request.SchemeId == null || request.SectionId == null || string.IsNullOrWhiteSpace(request.Scope)) return Result<ReportCardPreviewDto>(400, "Choose a scheme, a section and a term to preview a real student.", null);
                    var active = await _marks.ActiveStatusIdAsync(ct);
                    if (active == null) return Result<ReportCardPreviewDto>(500, "The system is not configured correctly. Please contact support.", null);
                    var scheme = await _marks.GetSchemeAsync(schoolId, request.SchemeId.Value, ct);
                    var section = await _marks.GetSectionAsync(schoolId, request.SectionId.Value, active.Value, ct);
                    if (scheme == null || section == null) return Result<ReportCardPreviewDto>(404, "Report scheme or section not found.", null);
                    var rights = await _access.RightsAsync(schoolId, caller, scheme.AcademicYearId, section.ClassId, section.SectionId, null, active.Value, ct);
                    if (!(rights.IsAdmin || rights.IsClassTeacher)) return Result<ReportCardPreviewDto>(403, rights.IsPrincipal ? "The Principal can preview with sample data, not with a real student." : "You can only preview the cards of the section you are the class teacher of.", null);
                    var scope = request.Scope.Trim().ToUpperInvariant();
                    if (!(scope == "YEAR" ? request.TermId == null : scope == "TERM" && request.TermId != null)) return Result<ReportCardPreviewDto>(400, "Choose a term, or the whole year.", null);
                    var set = await _builder.BuildAsync(schoolId, request.SchemeId.Value, request.SectionId.Value, scope, request.TermId, active.Value, DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5)), ct);
                    if (set == null) return Result<ReportCardPreviewDto>(404, "Report scheme, section or term not found.", null);
                    if (!set.Models.TryGetValue(request.StudentId.Trim(), out model!)) return Result<ReportCardPreviewDto>(404, "Student not found in this section.", null);
                    if (!design.SupportedModes.Contains(scheme.EvaluationMode)) return Result<ReportCardPreviewDto>(409, ReportCardTemplates.Incompatible(ReportCardTemplates.Find(key), scheme.EvaluationMode), null);
                    model.TemplateKey = key; isSample = false;
                }
                else
                {
                    // the sample is drawn in the mode the design is for: a GRADES-only design gets grade data (skill grid: five skills; playful report: descriptive), a MARKS-only one marks data,
                    // and a design for both uses the requested mode (default MARKS)
                    var wantMode = design.SupportedModes.Count == 1 ? design.SupportedModes[0] : (string.Equals(request.Mode, "GRADES", StringComparison.OrdinalIgnoreCase) ? "GRADES" : "MARKS");
                    var wantSkills = wantMode != "GRADES" ? 0 : key == "skill-grid" ? 5 : key == "playful-report" ? 0 : Math.Clamp(request.Skills ?? 3, 0, 5);
                    model = ReportCardSamples.Build(new RcSampleOptions { TemplateKey = key, Mode = wantMode, Skills = wantSkills, Subjects = wantMode == "GRADES" && key == "skill-grid" ? 4 : wantMode == "GRADES" && key == "playful-report" ? 7 : 8, Terms = Math.Clamp(request.Terms, 1, 3), SchoolName = school?.SchoolName ?? "Sample Public School", LogoKey = school?.LogoUrl });
                    model.Header.AddressLine = string.IsNullOrWhiteSpace(school?.AddressLine1) ? model.Header.AddressLine : string.Join(", ", new[] { school?.AddressLine1, school?.CityName, school?.StateName }.Where(x => !string.IsNullOrWhiteSpace(x)));
                    if (!string.IsNullOrWhiteSpace(school?.MobileNumber)) model.Header.Phone = school!.MobileNumber!;
                }
                ReportCardBranding.Apply(model, clean, signatureKey);

                var pngs = await Task.Run(() => ReportCardPdf.RenderPng(_images.Context(model, draft: !isSample)), ct);
                return Result(200, "Successfully generated", new ReportCardPreviewDto { TemplateKey = key, IsSample = isSample, PageCount = pngs.Count, Pages = pngs.Take(4).Select(Convert.ToBase64String).ToList() });
            }
            catch (Exception ex) { return Error<ReportCardPreviewDto>(ex, "previewing a report card"); }
        }
    }
}
