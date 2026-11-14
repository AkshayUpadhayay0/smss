using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.helper.idcard;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class StudentIdCardService : IStudentIdCardService
    {
        /// <summary>The most cards one bulk request prints (12 A4 sheets of 9). A section larger than this is refused, not truncated.</summary>
        public const int MaxCardsPerRequest = 100;
        private const int MaxImageBytes = 5 * 1024 * 1024;   // uploads are capped at 2 MB; anything bigger on disk is not a photo we made

        private readonly IStudentIdCardRepository _repo;
        private readonly IIdCardSettingsService _settings;
        private readonly IFileStorageService _storage;
        private readonly IStudentRepository _students;
        private readonly ILogger<StudentIdCardService> _logger;

        public StudentIdCardService(IStudentIdCardRepository repo, IIdCardSettingsService settings, IFileStorageService storage, IStudentRepository students, ILogger<StudentIdCardService> logger)
        {
            _students = students;
            _repo = repo;
            _settings = settings;
            _storage = storage;
            _logger = logger;
        }

        public async Task<ApiResponse<IdCardFileDto>> GetStudentCardAsync(string? schoolId, string studentId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<IdCardFileDto>(400, TenantMessages.NoSchool, null);
            try
            {
                // 404 for another school's student too, so ids of other schools cannot be probed
                if (!await _repo.StudentExistsAsync(schoolId, studentId, ct)) return Result<IdCardFileDto>(404, "Student not found.", null);
                var school = await _repo.GetSchoolAsync(schoolId, ct);
                if (school == null) return Result<IdCardFileDto>(404, "School not found.", null);
                var year = await _repo.GetCurrentYearAsync(schoolId, ct);
                if (year == null) return Result<IdCardFileDto>(400, "No current academic year is set for this school, so no card can be issued.", null);
                var activeId = await _repo.GetActiveStatusIdAsync(ct);
                if (activeId == null) return Result<IdCardFileDto>(500, "Something went wrong. Please try again later.", null);

                var row = await _repo.GetStudentAsync(schoolId, year.AcademicYearId, studentId, ct);
                if (row == null) return Result<IdCardFileDto>(400, $"This student has no enrollment in the current academic year ({year.YearName}), so no card can be issued.", null);
                if (ExclusionReason(row, activeId.Value, year.YearName) is { } why) return Result<IdCardFileDto>(400, $"No card can be issued: {why}.", null);

                var brand = await _settings.GetBrandAsync(schoolId, ct);
                var (cards, _) = await BuildCardsAsync(school, year, new[] { row }, brand.Options, ct);
                var schoolModel = BuildSchool(school);

                var pdf = await Task.Run(() => IdCardPdf.RenderSingle(schoolModel, brand, cards[0]), ct);
                return Result(200, "Successfully generated", new IdCardFileDto { FileName = $"IDCard_{Safe(row.AdmissionNumber)}.pdf", Content = pdf });
            }
            catch (Exception ex) { return Error<IdCardFileDto>(ex, "generating a student ID card"); }
        }

        // ---- a student's own card -------------------------------------------------------------------------------------------------

        private const string NotAvailable = "Your ID card is not available right now. Please ask the school office.";

        /// <summary>The caller's own card inputs, or a calm 404: no client value picks the student (the login id IS the student id).</summary>
        private async Task<(ApiResponse<T>? Error, IdCardSchoolRow? School, IdCardBrand? Brand, IdCardModel? Card, IdCardStudentRow? Row)> PrepareMineAsync<T>(string? schoolId, long callerUserId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return (Result<T>(400, TenantMessages.NoSchool, default), null, null, null, null);
            var loginId = await _students.GetUserLoginIdAsync(schoolId, callerUserId, ct);
            if (string.IsNullOrEmpty(loginId) || !await _repo.StudentExistsAsync(schoolId, loginId, ct)) return (Result<T>(404, NotAvailable, default), null, null, null, null);
            var school = await _repo.GetSchoolAsync(schoolId, ct);
            var year = await _repo.GetCurrentYearAsync(schoolId, ct);
            var activeId = await _repo.GetActiveStatusIdAsync(ct);
            if (school == null || year == null || activeId == null) return (Result<T>(404, NotAvailable, default), null, null, null, null);
            var row = await _repo.GetStudentAsync(schoolId, year.AcademicYearId, loginId, ct);
            // inactive, left, or not enrolled this year: no card (the staff path says why; a student just gets the calm line)
            if (row == null || ExclusionReason(row, activeId.Value, year.YearName) != null) return (Result<T>(404, NotAvailable, default), null, null, null, null);
            var brand = await _settings.GetBrandAsync(schoolId, ct);
            var (cards, _) = await BuildCardsAsync(school, year, new[] { row }, brand.Options, ct);
            return (null, school, brand, cards[0], row);
        }

        public async Task<ApiResponse<IdCardFileDto>> GetMyCardAsync(string? schoolId, long callerUserId, CancellationToken ct)
        {
            try
            {
                var p = await PrepareMineAsync<IdCardFileDto>(schoolId, callerUserId, ct);
                if (p.Error != null) return p.Error;
                var schoolModel = BuildSchool(p.School!);
                var pdf = await Task.Run(() => IdCardPdf.RenderSingle(schoolModel, p.Brand!, p.Card!), ct);
                return Result(200, "Successfully generated", new IdCardFileDto { FileName = $"IDCard_{Safe(p.Row!.AdmissionNumber)}.pdf", Content = pdf });
            }
            catch (Exception ex) { return Error<IdCardFileDto>(ex, "generating a student's own ID card"); }
        }

        public async Task<ApiResponse<IdCardPreviewDto>> GetMyCardPreviewAsync(string? schoolId, long callerUserId, CancellationToken ct)
        {
            try
            {
                var p = await PrepareMineAsync<IdCardPreviewDto>(schoolId, callerUserId, ct);
                if (p.Error != null) return p.Error;
                var schoolModel = BuildSchool(p.School!);
                var (front, back) = await Task.Run(() => IdCardPdf.RenderPreview(schoolModel, p.Brand!, p.Card!), ct);
                return Result(200, "Successfully generated", new IdCardPreviewDto { TemplateCode = p.Brand!.TemplateCode, FrontPng = Convert.ToBase64String(front), BackPng = Convert.ToBase64String(back), IsSample = false });
            }
            catch (Exception ex) { return Error<IdCardPreviewDto>(ex, "rendering a student's own ID card preview"); }
        }

        public async Task<ApiResponse<IdCardBulkResultDto>> GetSectionCardsAsync(string? schoolId, long sectionId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<IdCardBulkResultDto>(400, TenantMessages.NoSchool, null);
            try
            {
                if (!await _repo.SectionBelongsToSchoolAsync(schoolId, sectionId, ct)) return Result<IdCardBulkResultDto>(404, "Section not found.", null);
                var school = await _repo.GetSchoolAsync(schoolId, ct);
                if (school == null) return Result<IdCardBulkResultDto>(404, "School not found.", null);
                var year = await _repo.GetCurrentYearAsync(schoolId, ct);
                if (year == null) return Result<IdCardBulkResultDto>(400, "No current academic year is set for this school, so no cards can be issued.", null);
                var activeId = await _repo.GetActiveStatusIdAsync(ct);
                if (activeId == null) return Result<IdCardBulkResultDto>(500, "Something went wrong. Please try again later.", null);

                var rows = (await _repo.GetSectionStudentsAsync(schoolId, year.AcademicYearId, sectionId, ct))
                    .OrderBy(r => r.RollNumber, RollOrder.Instance).ThenBy(r => r.FirstName, StringComparer.OrdinalIgnoreCase).ToList();

                var result = new IdCardBulkResultDto { MaxCardsPerRequest = MaxCardsPerRequest, AcademicYearName = year.YearName, FileName = "IDCards.pdf" };
                var eligible = new List<IdCardStudentRow>();
                foreach (var r in rows)
                {
                    if (ExclusionReason(r, activeId.Value, year.YearName) is { } why)
                        result.Excluded.Add(Ref(r, why));
                    else eligible.Add(r);
                }

                if (eligible.Count > MaxCardsPerRequest)
                    return Result<IdCardBulkResultDto>(400, $"This section has {eligible.Count} cards to print; one request prints at most {MaxCardsPerRequest}. Nothing was generated.", null);

                if (eligible.Count == 0)
                    return Result(200, "No student in this section can be issued a card.", result);

                var brand = await _settings.GetBrandAsync(schoolId, ct);
                var (cards, hadPhoto) = await BuildCardsAsync(school, year, eligible, brand.Options, ct);
                for (var i = 0; i < eligible.Count; i++)
                    if (!hadPhoto[i]) result.NoPhoto.Add(Ref(eligible[i], null));

                var schoolModel = BuildSchool(school);
                var (pdf, sheets) = await Task.Run(() => IdCardPdf.RenderSheets(schoolModel, brand, cards), ct);
                var first = eligible[0];
                result.FileName = $"IDCards_{Safe(first.ClassName)}_{Safe(first.SectionName)}_{Safe(year.YearName)}.pdf";
                result.PdfBase64 = Convert.ToBase64String(pdf);
                result.CardCount = cards.Count;
                result.SheetCount = sheets;
                return Result(200, $"Generated {cards.Count} card(s) on {sheets} sheet(s).", result);
            }
            catch (Exception ex) { return Error<IdCardBulkResultDto>(ex, "generating section ID cards"); }
        }

        public async Task<ApiResponse<IdCardPreviewDto>> PreviewAsync(string? schoolId, IdCardPreviewRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<IdCardPreviewDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var (clean, error) = IdCardSettingsRules.Validate(request);
                if (clean == null) return Result<IdCardPreviewDto>(400, error!, null);

                var school = await _repo.GetSchoolAsync(schoolId, ct);
                if (school == null) return Result<IdCardPreviewDto>(404, "School not found.", null);
                var year = await _repo.GetCurrentYearAsync(schoolId, ct) ?? new IdCardYearRow(0, "2026-27");

                // the screen's (possibly unsaved) choices, with the signature already on file
                var brand = new IdCardBrand(clean.TemplateCode, clean.PrimaryColor, clean.SecondaryColor, clean.Tagline, clean.Website, clean.Email, clean.TermsText,
                    await _settings.GetSignatureAsync(schoolId, ct), new IdCardOptions(clean.ShowDob, clean.ShowAddress, clean.ShowBloodGroup, clean.ShowMotherName));

                IdCardModel card;
                var isSample = string.IsNullOrWhiteSpace(request.StudentId);
                if (isSample) card = SampleCard(school, year);
                else
                {
                    // a real student of THIS school (404 for anyone else's); any status, so a school can preview before the year starts
                    if (!await _repo.StudentExistsAsync(schoolId, request.StudentId!, ct)) return Result<IdCardPreviewDto>(404, "Student not found.", null);
                    var row = await _repo.GetStudentAsync(schoolId, year.AcademicYearId, request.StudentId!, ct);
                    if (row == null) return Result<IdCardPreviewDto>(400, $"This student has no enrollment in the current academic year ({year.YearName}).", null);
                    card = (await BuildCardsAsync(school, year, new[] { row }, brand.Options, ct)).Cards[0];
                }

                var schoolModel = BuildSchool(school);
                var (front, back) = await Task.Run(() => IdCardPdf.RenderPreview(schoolModel, brand, card), ct);
                return Result(200, "Successfully generated", new IdCardPreviewDto
                {
                    TemplateCode = clean.TemplateCode, FrontPng = Convert.ToBase64String(front), BackPng = Convert.ToBase64String(back), IsSample = isSample
                });
            }
            catch (Exception ex) { return Error<IdCardPreviewDto>(ex, "rendering an ID card preview"); }
        }

        // ---- helpers ----------------------------------------------------------------------------------------------------------------

        /// <summary>Built-in dummy data so a school can see a template before it has any students.</summary>
        private static IdCardModel SampleCard(IdCardSchoolRow school, IdCardYearRow year) =>
            new("Aarav Sharma", "Class 5", "A", "12", "ADM2026001", year.YearName, null, "Rajesh Sharma", "Sunita Sharma", "Rajesh Sharma", "Father", "98765 43210",
                "14 Aug 2015", "B+", "12 Rajpur Road, Dehradun - 248001", IdCardQr.Payload(school.SchoolCode, "ADM2026001"), IsSample: true);

        private static string? ExclusionReason(IdCardStudentRow r, int activeId, string yearName)
        {
            var studentOff = r.StudentStatusId != activeId;
            var enrollOff = r.EnrollmentStatusId != activeId;
            if (studentOff && enrollOff) return $"the student is inactive and has no active enrollment for {yearName}";
            if (studentOff) return "the student is inactive";
            if (enrollOff) return $"the student has no active enrollment for {yearName}";
            return null;
        }

        private static IdCardStudentRefDto Ref(IdCardStudentRow r, string? reason) => new()
        {
            StudentId = r.StudentId, AdmissionNumber = r.AdmissionNumber, FullName = FullName(r), RollNumber = r.RollNumber,
            Reason = reason == null ? null : char.ToUpperInvariant(reason[0]) + reason[1..]
        };

        private static string FullName(IdCardStudentRow r) =>
            string.Join(' ', new[] { r.FirstName, r.MiddleName, r.LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));

        private IdCardSchoolModel BuildSchool(IdCardSchoolRow s)
        {
            var logo = IdCardPicture.TryCreateVerified(ReadUpload(s.LogoUrl));   // once per render run (single, preview or the whole bulk sheet), never per card
            var tail = string.Join(", ", new[] { s.CityName, s.StateName }.Where(p => !string.IsNullOrWhiteSpace(p)));
            if (!string.IsNullOrWhiteSpace(s.Pincode)) tail = tail.Length > 0 ? $"{tail} - {s.Pincode}" : s.Pincode!;
            var address = string.Join(", ", new[] { s.AddressLine1, s.AddressLine2, tail }.Where(p => !string.IsNullOrWhiteSpace(p)));
            return new IdCardSchoolModel(s.SchoolName, logo, address.Length > 0 ? address : null, string.IsNullOrWhiteSpace(s.MobileNumber) ? null : s.MobileNumber);
        }

        /// <summary>
        /// Card content for the given students, with the lookups done once for the whole batch (guardians, blood groups, cities) rather than once per card.
        /// The photo is read straight from the uploads folder on the server and embedded in the PDF; no photo URL is created or returned.
        /// </summary>
        private async Task<(List<IdCardModel> Cards, List<bool> HadPhoto)> BuildCardsAsync(IdCardSchoolRow school, IdCardYearRow year, IReadOnlyList<IdCardStudentRow> rows,
            IdCardOptions options, CancellationToken ct)
        {
            var ids = rows.Select(r => r.StudentId).ToList();
            var guardians = (await _repo.GetGuardiansAsync(school.SchoolId, ids, ct)).GroupBy(g => g.StudentId).ToDictionary(g => g.Key, g => g.ToList());
            var bloodNames = await _repo.GetBloodGroupNamesAsync(ct);
            var cities = await _repo.GetCityNamesAsync(rows.Where(r => r.CityId.HasValue).Select(r => r.CityId!.Value).Distinct().ToList(), ct);

            var cards = new List<IdCardModel>(rows.Count);
            var had = new List<bool>(rows.Count);
            foreach (var r in rows)
            {
                var photo = IdCardPicture.TryCreate(ReadPhoto(r.ProfilePhotoUrl));
                guardians.TryGetValue(r.StudentId, out var gs);
                gs ??= new();
                string? Parent(string relationship) => gs.FirstOrDefault(g => string.Equals(g.Relationship, relationship, StringComparison.OrdinalIgnoreCase))?.Name;
                var primary = gs.FirstOrDefault(g => g.IsPrimary) ?? gs.FirstOrDefault();

                string? city = r.CityId.HasValue && r.CountryId.HasValue && r.StateId.HasValue && r.DistrictId.HasValue
                    && cities.TryGetValue((r.CountryId.Value, r.StateId.Value, r.DistrictId.Value, r.CityId.Value), out var cn) ? cn : null;
                var tail = string.Join(", ", new[] { city }.Where(p => !string.IsNullOrWhiteSpace(p)));
                if (!string.IsNullOrWhiteSpace(r.Pincode)) tail = tail.Length > 0 ? $"{tail} - {r.Pincode}" : r.Pincode!;
                var address = string.Join(", ", new[] { r.AddressLine1, r.AddressLine2, tail }.Where(p => !string.IsNullOrWhiteSpace(p)));

                cards.Add(new IdCardModel(FullName(r), r.ClassName, r.SectionName, r.RollNumber, r.AdmissionNumber, year.YearName, photo,
                    Parent("FATHER"), Parent("MOTHER"), primary?.Name, primary?.Relationship,
                    primary?.Mobile ?? (string.IsNullOrWhiteSpace(r.MobileNumber) ? null : r.MobileNumber),
                    r.DateOfBirth == default ? null : r.DateOfBirth.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
                    r.BloodGroupId.HasValue && bloodNames.TryGetValue(r.BloodGroupId.Value, out var bg) ? bg : null,
                    address.Length > 0 ? address : null,
                    IdCardQr.Payload(school.SchoolCode, r.AdmissionNumber)));
                had.Add(photo != null);
            }
            return (cards, had);
        }

        /// <summary>A student's photo, read from PRIVATE storage (a not-yet-migrated public /uploads path is still understood).</summary>
        private byte[]? ReadPhoto(string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return null;
            try
            {
                using var stream = _storage.OpenPhoto(stored);
                if (stream == null) return null;
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.Length is > 0 and <= MaxImageBytes ? ms.ToArray() : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read a student photo for an ID card");
                return null;
            }
        }

        private byte[]? ReadUpload(string? relativeUrl)
        {
            if (string.IsNullOrWhiteSpace(relativeUrl)) return null;
            try
            {
                using var stream = _storage.OpenUpload(relativeUrl);
                if (stream == null) return null;
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.Length is > 0 and <= MaxImageBytes ? ms.ToArray() : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read upload for an ID card");
                return null;
            }
        }

        private static string Safe(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in s) sb.Append(char.IsAsciiLetterOrDigit(ch) || ch == '-' ? ch : '_');
            return sb.ToString().Trim('_');
        }

        // Numeric roll numbers sort as numbers (2 before 10), others as text, blanks last
        private sealed class RollOrder : IComparer<string?>
        {
            public static readonly RollOrder Instance = new();
            public int Compare(string? a, string? b)
            {
                if (string.IsNullOrWhiteSpace(a)) return string.IsNullOrWhiteSpace(b) ? 0 : 1;
                if (string.IsNullOrWhiteSpace(b)) return -1;
                return long.TryParse(a, out var x) && long.TryParse(b, out var y) ? x.CompareTo(y) : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        private ApiResponse<T> Error<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", default);
        }
    }
}
