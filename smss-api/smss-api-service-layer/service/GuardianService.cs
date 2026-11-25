using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class GuardianService : IGuardianService
    {
        public const int MaxBulk = 200;
        private const string Edit = "EDIT";
        private const string Bulk = "BULK";

        private readonly IGuardianRepository _repo;
        private readonly ILogger<GuardianService> _logger;

        public GuardianService(IGuardianRepository repo, ILogger<GuardianService> logger)
        {
            _repo = repo; _logger = logger;
        }

        // ---------------------------------------------------------------- one guardian
        public async Task<ApiResponse<GuardianDto>> GetAsync(string? schoolId, long guardianId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<GuardianDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var guardian = await _repo.GetGuardianAsync(schoolId, guardianId, false, ct);
                if (guardian == null) return Result<GuardianDto>(404, "Guardian not found.", null);
                return Result(200, "Successfully fetched", await ToDtoAsync(schoolId, guardian, ct));
            }
            catch (Exception ex) { return Error<GuardianDto>(ex, "reading a guardian"); }
        }

        public async Task<ApiResponse<GuardianDto>> UpdateAsync(string? schoolId, long guardianId, GuardianUpdateRequestDto request, long userId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<GuardianDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await _repo.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                if (activeId == null) return ConfigError<GuardianDto>("Active status is missing in lut_status");

                var name = request.Name?.Trim() ?? "";
                if (name.Length == 0) return Result<GuardianDto>(400, "The name is required.", null);
                if (name.Length > 100) return Result<GuardianDto>(400, "The name can be at most 100 characters.", null);
                var mobile = request.MobileNumber?.Trim() ?? "";
                if (PhoneNumbers.NationalKey(mobile) != mobile) return Result<GuardianDto>(400, "Enter a valid 10-digit mobile number.", null);
                var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
                if (email != null) { var e = PhoneNumbers.EmailError(email, "The"); if (e != null) return Result<GuardianDto>(400, e, null); }
                string? whatsapp = null;
                if (request.WhatsAppSameAsMobile || !string.IsNullOrWhiteSpace(request.WhatsAppNumber))
                {
                    var w = PhoneNumbers.WhatsAppError(request.WhatsAppNumber, request.WhatsAppSameAsMobile, mobile, "The");
                    if (w != null) return Result<GuardianDto>(400, w, null);
                    whatsapp = PhoneNumbers.WhatsAppFor(request.WhatsAppNumber, request.WhatsAppSameAsMobile, mobile);
                }
                var occupation = string.IsNullOrWhiteSpace(request.Occupation) ? null : request.Occupation.Trim();
                if (occupation != null && occupation.Length > 150) return Result<GuardianDto>(400, "The occupation can be at most 150 characters.", null);

                await using var tx = await _repo.BeginTransactionAsync(ct);
                var guardian = await _repo.GetGuardianAsync(schoolId, guardianId, true, ct);
                if (guardian == null) return Result<GuardianDto>(404, "Guardian not found.", null);
                if (guardian.StatusId != activeId.Value || guardian.MergedIntoGuardianId != null)
                    return Result<GuardianDto>(409, guardian.MergedIntoGuardianId != null ? "This guardian was merged into another one. Edit the other guardian." : "This guardian is inactive, so it cannot be edited.", null);

                // the primary guardian of an active student must have both: the school can always reach the family
                var students = await _repo.GetLinkedStudentsAsync(schoolId, guardianId, await ActiveStudentStatusAsync(ct), ct);
                var required = students.Any(s => s.IsPrimary && s.StudentActive);
                if (required && whatsapp == null) return Result<GuardianDto>(400, "The WhatsApp number is required for the primary guardian of a student (tick \"same as mobile number\" if it is the same).", null);
                if (required && email == null) return Result<GuardianDto>(400, "The email is required for the primary guardian of a student.", null);

                var now = DateTime.UtcNow;
                var changes = new List<TbGuardianChange>();
                void Log(string field, string? oldValue, string? newValue)
                {
                    if (string.Equals(oldValue, newValue, StringComparison.Ordinal)) return;
                    changes.Add(new TbGuardianChange { SchoolId = schoolId, GuardianId = guardianId, FieldName = field, OldValue = oldValue, NewValue = newValue, Source = Edit, ChangedBy = userId, ChangedAt = now });
                }
                Log("mobile_number", guardian.MobileNumber, mobile);
                Log("whatsapp_number", guardian.WhatsAppNumber, whatsapp);
                Log("email", guardian.Email, email);

                guardian.FirstName = name;
                guardian.MobileNumber = mobile;
                guardian.WhatsAppNumber = whatsapp;
                guardian.Email = email;
                guardian.Occupation = occupation;
                guardian.UpdatedAt = now;
                if (changes.Count > 0) await _repo.AddChangesAsync(changes, ct);
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return Result(200, "Guardian updated.", await ToDtoAsync(schoolId, guardian, ct));
            }
            catch (Exception ex) { return Error<GuardianDto>(ex, "updating a guardian"); }
        }

        private async Task<int> ActiveStudentStatusAsync(CancellationToken ct) => await _repo.GetGeneralStatusIdAsync(StatusNames.Active, ct) ?? -1;

        private async Task<GuardianDto> ToDtoAsync(string schoolId, TbGuardian g, CancellationToken ct)
        {
            var activeId = await _repo.GetGeneralStatusIdAsync(StatusNames.Active, ct) ?? -1;
            var students = await _repo.GetLinkedStudentsAsync(schoolId, g.GuardianId, activeId, ct);
            return new GuardianDto
            {
                GuardianId = g.GuardianId, Name = Display(g), Relationship = g.Relationship, MobileNumber = g.MobileNumber, WhatsAppNumber = g.WhatsAppNumber, Email = g.Email, Occupation = g.Occupation,
                IsActive = g.StatusId == activeId && g.MergedIntoGuardianId == null, LinkedStudentCount = students.Count,
                Students = students.Select(s => new GuardianStudentDto { StudentId = s.StudentId, StudentName = s.StudentName, IsPrimary = s.IsPrimary }).ToList(),
                ContactRequired = students.Any(s => s.IsPrimary && s.StudentActive),
            };
        }

        private static string Display(TbGuardian g) => string.Join(' ', new[] { g.FirstName, g.LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));

        // ---------------------------------------------------------------- the sibling pick
        public const int MaxSiblingResults = 30;

        public async Task<ApiResponse<List<SiblingStudentDto>>> SearchSiblingsAsync(string? schoolId, long? classId, string? search, int limit, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<SiblingStudentDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var term = search?.Trim();
                if (classId == null && (term == null || term.Length < 2)) return Result<List<SiblingStudentDto>>(400, "Choose a class, or type at least 2 letters of the student's name.", null);
                var activeId = await _repo.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                if (activeId == null) return ConfigError<List<SiblingStudentDto>>("Active status is missing in lut_status");
                var year = await _repo.GetViewedYearIdAsync(schoolId, SchoolClock.Today(), ct);
                if (year == null) return Result(200, "Successfully fetched", new List<SiblingStudentDto>());   // no academic year: no class lists to pick from
                limit = Math.Clamp(limit <= 0 ? 20 : limit, 1, MaxSiblingResults);
                var rows = await _repo.SearchSiblingStudentsAsync(schoolId, activeId.Value, year.Value, classId, term, limit, ct);
                return Result(200, "Successfully fetched", rows.Select(s => new SiblingStudentDto
                {
                    StudentId = s.StudentId, StudentName = s.StudentName, ClassName = s.ClassName, SectionName = s.SectionName,
                    Guardians = s.Guardians.Select(g => new SiblingGuardianDto { GuardianId = g.GuardianId, Name = g.Name, Relationship = g.Relationship, MaskedMobile = PhoneNumbers.Mask(g.Mobile), HasWhatsApp = g.HasWhatsApp, HasEmail = g.HasEmail, IsPrimary = g.IsPrimary }).ToList(),
                }).ToList());
            }
            catch (Exception ex) { return Error<List<SiblingStudentDto>>(ex, "searching students for the sibling pick"); }
        }

        // ---------------------------------------------------------------- the "Complete parent details" list
        public async Task<ApiResponse<IncompleteGuardiansDto>> ListIncompleteAsync(string? schoolId, long? classId, long? sectionId, string? search, int page, int pageSize, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<IncompleteGuardiansDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await _repo.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                if (activeId == null) return ConfigError<IncompleteGuardiansDto>("Active status is missing in lut_status");
                var year = await _repo.GetViewedYearIdAsync(schoolId, SchoolClock.Today(), ct);
                var all = await _repo.ListIncompletePrimaryAsync(schoolId, activeId.Value, year, ct);

                IEnumerable<IncompleteGuardianRow> rows = all;
                // class and section come from the enrolment in the viewed (current-flagged) year; a student not enrolled in it never matches a class or section filter
                if (classId != null) rows = rows.Where(r => r.Students.Any(s => s.ClassId == classId));
                if (sectionId != null) rows = rows.Where(r => r.Students.Any(s => s.SectionId == sectionId));
                var term = search?.Trim();
                if (!string.IsNullOrEmpty(term))
                {
                    var digits = new string(term.Where(char.IsDigit).ToArray());
                    rows = rows.Where(r => r.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                                        || r.Students.Any(s => s.StudentName.Contains(term, StringComparison.OrdinalIgnoreCase) || s.StudentId.Contains(term, StringComparison.OrdinalIgnoreCase))
                                        || (digits.Length >= 3 && r.Mobile.Contains(digits)));
                }
                var matched = rows.ToList();
                page = Math.Max(1, page);
                pageSize = Math.Clamp(pageSize <= 0 ? 25 : pageSize, 1, 100);
                return Result(200, "Successfully fetched", new IncompleteGuardiansDto
                {
                    Total = matched.Count, TotalIncomplete = all.Count, Page = page, PageSize = pageSize, ViewedYearId = year,
                    Items = matched.Skip((page - 1) * pageSize).Take(pageSize).Select(r => new IncompleteGuardianDto
                    {
                        GuardianId = r.GuardianId, Name = r.Name, Relationship = r.Relationship, MobileNumber = r.Mobile, WhatsAppNumber = r.WhatsApp, Email = r.Email,
                        Missing = Missing(r.WhatsApp, r.Email),
                        Students = r.Students.Select(s => new IncompleteGuardianStudentDto { StudentId = s.StudentId, StudentName = s.StudentName, ClassId = s.ClassId, ClassName = s.ClassName, SectionId = s.SectionId, SectionName = s.SectionName }).ToList(),
                    }).ToList(),
                });
            }
            catch (Exception ex) { return Error<IncompleteGuardiansDto>(ex, "listing guardians to complete"); }
        }

        private static List<string> Missing(string? whatsapp, string? email)
        {
            var m = new List<string>();
            if (string.IsNullOrWhiteSpace(whatsapp)) m.Add("WhatsApp");
            if (string.IsNullOrWhiteSpace(email)) m.Add("Email");
            return m;
        }

        public async Task<ApiResponse<int>> CountIncompleteAsync(string? schoolId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<int>(400, TenantMessages.NoSchool, 0);
            try
            {
                var activeId = await _repo.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                if (activeId == null) return ConfigError<int>("Active status is missing in lut_status");
                return Result(200, "Successfully fetched", await _repo.CountIncompletePrimaryAsync(schoolId, activeId.Value, ct));
            }
            catch (Exception ex) { return Error<int>(ex, "counting guardians to complete"); }
        }

        // ---------------------------------------------------------------- the bulk action
        public async Task<ApiResponse<BulkWhatsAppResultDto>> UseMobileAsWhatsAppAsync(string? schoolId, IReadOnlyCollection<long> guardianIds, long userId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<BulkWhatsAppResultDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var ids = guardianIds.Distinct().ToList();
                if (ids.Count == 0) return Result<BulkWhatsAppResultDto>(400, "Select at least one guardian.", null);
                if (ids.Count > MaxBulk) return Result<BulkWhatsAppResultDto>(400, $"Select at most {MaxBulk} guardians at a time.", null);
                var activeId = await _repo.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                if (activeId == null) return ConfigError<BulkWhatsAppResultDto>("Active status is missing in lut_status");

                await using var tx = await _repo.BeginTransactionAsync(ct);   // all or nothing
                var guardians = await _repo.GetGuardiansAsync(schoolId, ids, true, ct);
                if (guardians.Count != ids.Count) return Result<BulkWhatsAppResultDto>(404, "One or more of the selected guardians were not found. Nothing was changed.", null);
                var inactive = guardians.Where(g => g.StatusId != activeId.Value || g.MergedIntoGuardianId != null).Select(Display).ToList();
                if (inactive.Count > 0) return Result<BulkWhatsAppResultDto>(409, $"These guardians are inactive: {string.Join(", ", inactive)}. Nothing was changed.", null);

                var toChange = guardians.Where(g => string.IsNullOrWhiteSpace(g.WhatsAppNumber)).ToList();
                var invalid = toChange.Where(g => PhoneNumbers.Normalise(g.MobileNumber) == null).Select(Display).ToList();
                if (invalid.Count > 0) return Result<BulkWhatsAppResultDto>(400, $"The mobile number of {string.Join(", ", invalid)} is not a valid number, so it cannot be used as the WhatsApp number. Nothing was changed.", null);

                var now = DateTime.UtcNow;
                var changes = new List<TbGuardianChange>();
                foreach (var g in toChange)
                {
                    var number = PhoneNumbers.Normalise(g.MobileNumber)!;
                    changes.Add(new TbGuardianChange { SchoolId = schoolId, GuardianId = g.GuardianId, FieldName = "whatsapp_number", OldValue = g.WhatsAppNumber, NewValue = number, Source = Bulk, ChangedBy = userId, ChangedAt = now });
                    g.WhatsAppNumber = number;   // the email is never touched
                    g.UpdatedAt = now;
                }
                if (changes.Count > 0) await _repo.AddChangesAsync(changes, ct);
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                var result = new BulkWhatsAppResultDto { Selected = ids.Count, Updated = toChange.Count, Skipped = ids.Count - toChange.Count };
                return Result(200, result.Updated == 0 ? "Every selected guardian already has a WhatsApp number. Nothing was changed." : $"{result.Updated} guardian(s) now use their mobile number as the WhatsApp number.", result);
            }
            catch (Exception ex) { return Error<BulkWhatsAppResultDto>(ex, "applying the bulk WhatsApp change"); }
        }

        // ---------------------------------------------------------------- helpers
        private ApiResponse<T> ConfigError<T>(string detail)
        {
            _logger.LogError("Configuration problem: {Detail}", detail);
            return Result<T>(500, "The system is not configured correctly. Please contact support.", default);
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
