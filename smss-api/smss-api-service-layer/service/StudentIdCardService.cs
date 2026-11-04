using System.Text;
using Microsoft.Extensions.Logging;
using QuestPDF.Infrastructure;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class StudentIdCardService : IStudentIdCardService
    {
        /// <summary>The most cards one bulk request prints (10 A4 sheets). A section larger than this is refused, not truncated.</summary>
        public const int MaxCardsPerRequest = 100;
        private const int MaxImageBytes = 5 * 1024 * 1024;   // uploads are capped at 2 MB; anything bigger on disk is not a photo we made

        private readonly IStudentIdCardRepository _repo;
        private readonly IFileStorageService _storage;
        private readonly ILogger<StudentIdCardService> _logger;

        public StudentIdCardService(IStudentIdCardRepository repo, IFileStorageService storage, ILogger<StudentIdCardService> logger)
        {
            _repo = repo;
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

                var guardians = await _repo.GetGuardiansAsync(schoolId, new[] { studentId }, ct);
                var (model, _) = BuildCard(school, year, row, guardians);
                var schoolModel = BuildSchool(school);

                var pdf = await Task.Run(() => IdCardPdf.RenderSingle(schoolModel, model), ct);
                return Result(200, "Successfully generated", new IdCardFileDto { FileName = $"IDCard_{Safe(row.AdmissionNumber)}.pdf", Content = pdf });
            }
            catch (Exception ex) { return Error<IdCardFileDto>(ex, "generating a student ID card"); }
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

                var guardians = await _repo.GetGuardiansAsync(schoolId, eligible.Select(e => e.StudentId).ToList(), ct);
                var schoolModel = BuildSchool(school);
                var cards = new List<IdCardModel>(eligible.Count);
                foreach (var r in eligible)
                {
                    var (model, hadPhoto) = BuildCard(school, year, r, guardians);
                    cards.Add(model);
                    if (!hadPhoto) result.NoPhoto.Add(Ref(r, null));
                }

                var (pdf, sheets) = await Task.Run(() => IdCardPdf.RenderSheets(schoolModel, cards), ct);
                var first = eligible[0];
                result.FileName = $"IDCards_{Safe(first.ClassName)}_{Safe(first.SectionName)}_{Safe(year.YearName)}.pdf";
                result.PdfBase64 = Convert.ToBase64String(pdf);
                result.CardCount = cards.Count;
                result.SheetCount = sheets;
                return Result(200, $"Generated {cards.Count} card(s) on {sheets} sheet(s).", result);
            }
            catch (Exception ex) { return Error<IdCardBulkResultDto>(ex, "generating section ID cards"); }
        }

        // ---- helpers ----------------------------------------------------------------------------------------------------------------

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
            var logo = IdCardPdf.TryLoadImage(ReadUpload(s.LogoUrl));
            var tail = string.Join(", ", new[] { s.CityName, s.StateName }.Where(p => !string.IsNullOrWhiteSpace(p)));
            if (!string.IsNullOrWhiteSpace(s.Pincode)) tail = tail.Length > 0 ? $"{tail} - {s.Pincode}" : s.Pincode!;
            var address = string.Join(", ", new[] { s.AddressLine1, s.AddressLine2, tail }.Where(p => !string.IsNullOrWhiteSpace(p)));
            return new IdCardSchoolModel(s.SchoolName, logo, address.Length > 0 ? address : null, string.IsNullOrWhiteSpace(s.MobileNumber) ? null : s.MobileNumber);
        }

        // The photo is read straight from the uploads folder on the server and embedded in the PDF; no photo URL is created or returned.
        private (IdCardModel Model, bool HadPhoto) BuildCard(IdCardSchoolRow school, IdCardYearRow year, IdCardStudentRow r,
            Dictionary<string, (string Name, string Relationship, string Mobile)> guardians)
        {
            var photo = IdCardPdf.TryLoadImage(ReadUpload(r.ProfilePhotoUrl));
            guardians.TryGetValue(r.StudentId, out var g);
            var model = new IdCardModel(FullName(r), $"{r.ClassName} - {r.SectionName}", r.RollNumber, r.AdmissionNumber, year.YearName, photo,
                g.Name, g.Relationship, g.Mobile, IdCardQr.Payload(school.SchoolCode, r.AdmissionNumber));
            return (model, photo != null);
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
