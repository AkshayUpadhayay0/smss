using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.helper.idcard;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class IdCardSettingsService : IIdCardSettingsService
    {
        private readonly IIdCardSettingsRepository _repo;
        private readonly IStudentIdCardRepository _cards;
        private readonly IFileStorageService _storage;
        private readonly ILogger<IdCardSettingsService> _logger;

        public IdCardSettingsService(IIdCardSettingsRepository repo, IStudentIdCardRepository cards, IFileStorageService storage, ILogger<IdCardSettingsService> logger)
        {
            _repo = repo;
            _cards = cards;
            _storage = storage;
            _logger = logger;
        }

        public async Task<ApiResponse<IdCardSettingsDto>> GetAsync(string? schoolId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<IdCardSettingsDto>(400, TenantMessages.NoSchool, null);
            try { return Result(200, "Successfully fetched", ToDto(await _repo.GetAsync(schoolId, ct))); }
            catch (Exception ex) { return Error<IdCardSettingsDto>(ex, "fetching ID card settings"); }
        }

        public async Task<ApiResponse<IdCardSettingsDto>> SaveAsync(string? schoolId, long userId, IdCardSettingsRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<IdCardSettingsDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var (clean, error) = IdCardSettingsRules.Validate(request);
                if (clean == null) return Result<IdCardSettingsDto>(400, error!, null);

                var row = await _repo.GetForUpdateAsync(schoolId, ct);
                var now = DateTime.UtcNow;
                if (row == null)
                {
                    row = new TbSchoolIdCardSettings { SchoolId = schoolId, CreatedAt = now };
                    _repo.Add(row);
                }
                row.TemplateCode = clean.TemplateCode;
                row.PrimaryColor = clean.PrimaryColor;
                row.SecondaryColor = clean.SecondaryColor;
                row.Tagline = clean.Tagline;
                row.Website = clean.Website;
                row.Email = clean.Email;
                row.TermsText = clean.TermsText;
                row.ShowDob = clean.ShowDob;
                row.ShowAddress = clean.ShowAddress;
                row.ShowBloodGroup = clean.ShowBloodGroup;
                row.ShowMotherName = clean.ShowMotherName;
                row.UpdatedBy = userId;
                row.UpdatedAt = now;
                await _repo.SaveChangesAsync(ct);
                return Result(200, "ID card settings saved.", ToDto(row));
            }
            catch (Exception ex) { return Error<IdCardSettingsDto>(ex, "saving ID card settings"); }
        }

        public async Task<ApiResponse<IdCardSignatureDto>> UploadSignatureAsync(string? schoolId, long userId, Stream content, string fileName, long length, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<IdCardSignatureDto>(400, TenantMessages.NoSchool, null);
            string? savedKey = null;
            try
            {
                // type, size and the file's real signature bytes, the same check the logo and photo uploads use
                var (image, error) = await ImageUploadValidator.ValidateAsync(content, fileName, length, "Signature", ct);
                if (image == null) return Result<IdCardSignatureDto>(400, error!, null);
                using var _ = image;

                var school = await _cards.GetSchoolAsync(schoolId, ct);
                if (school == null) return Result<IdCardSignatureDto>(404, "School not found.", null);
                if (!SchoolCodeRules.IsFolderSafe(school.SchoolCode))
                    return Result<IdCardSignatureDto>(400, "This school's code can't be used as a folder name. Please contact support.", null);

                savedKey = await _storage.SavePrincipalSignatureAsync(school.SchoolCode, image.Content, image.Extension, ct);

                var row = await _repo.GetForUpdateAsync(schoolId, ct);
                var now = DateTime.UtcNow;
                if (row == null)
                {
                    var t = IdCardTemplates.Default;
                    row = new TbSchoolIdCardSettings { SchoolId = schoolId, TemplateCode = t.Code, PrimaryColor = t.DefaultPrimary, SecondaryColor = t.DefaultSecondary, CreatedAt = now };
                    _repo.Add(row);
                }
                var old = row.PrincipalSignaturePath;
                row.PrincipalSignaturePath = savedKey;
                row.UpdatedBy = userId;
                row.UpdatedAt = now;
                await _repo.SaveChangesAsync(ct);
                if (!string.IsNullOrEmpty(old)) _storage.DeletePrivate(old);
                return Result(200, "Signature uploaded.", new IdCardSignatureDto { HasSignature = true });
            }
            catch (Exception ex)
            {
                if (savedKey != null) _storage.DeletePrivate(savedKey);   // the row was not saved: do not leave the file behind
                return Error<IdCardSignatureDto>(ex, "uploading the principal signature");
            }
        }

        public async Task<ApiResponse<IdCardSignatureDto>> RemoveSignatureAsync(string? schoolId, long userId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<IdCardSignatureDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var row = await _repo.GetForUpdateAsync(schoolId, ct);
                if (row?.PrincipalSignaturePath is not { Length: > 0 } old)
                    return Result(200, "There is no signature on file.", new IdCardSignatureDto { HasSignature = false });
                row.PrincipalSignaturePath = null;
                row.UpdatedBy = userId;
                row.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                _storage.DeletePrivate(old);
                return Result(200, "Signature removed.", new IdCardSignatureDto { HasSignature = false });
            }
            catch (Exception ex) { return Error<IdCardSignatureDto>(ex, "removing the principal signature"); }
        }

        public async Task<IdCardPicture?> GetSignatureAsync(string schoolId, CancellationToken ct)
        {
            var row = await _repo.GetAsync(schoolId, ct);
            return await ReadSignature(row);
        }

        public async Task<IdCardBrand> GetBrandAsync(string schoolId, CancellationToken ct)
        {
            var row = await _repo.GetAsync(schoolId, ct);
            var signature = await ReadSignature(row);
            if (row == null)
            {
                var t = IdCardTemplates.Default;
                return new IdCardBrand(t.Code, t.DefaultPrimary, t.DefaultSecondary, null, null, null, null, null, IdCardOptions.Defaults);
            }
            // a row saved before a template was retired (or a hand-edited one) still prints: fall back to the default design
            var template = IdCardTemplates.Find(row.TemplateCode) ?? IdCardTemplates.Default;
            var primary = IdCardColors.IsHex(row.PrimaryColor) ? row.PrimaryColor : template.DefaultPrimary;
            var secondary = IdCardColors.IsHex(row.SecondaryColor) ? row.SecondaryColor : template.DefaultSecondary;
            return new IdCardBrand(template.Code, primary, secondary, row.Tagline, row.Website, row.Email, row.TermsText, signature,
                new IdCardOptions(row.ShowDob, row.ShowAddress, row.ShowBloodGroup, row.ShowMotherName));
        }

        private async Task<IdCardPicture?> ReadSignature(TbSchoolIdCardSettings? row)
        {
            if (row?.PrincipalSignaturePath is not { Length: > 0 } key) return null;
            try
            {
                await using var stream = _storage.OpenPrivate(key);
                if (stream == null) return null;
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                return IdCardPicture.TryCreate(ms.ToArray());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read the principal signature");
                return null;
            }
        }

        public static IdCardSettingsDto ToDto(TbSchoolIdCardSettings? row)
        {
            var t = IdCardTemplates.Default;
            var dto = new IdCardSettingsDto
            {
                TemplateCode = row?.TemplateCode ?? t.Code,
                PrimaryColor = row?.PrimaryColor ?? t.DefaultPrimary,
                SecondaryColor = row?.SecondaryColor ?? t.DefaultSecondary,
                Tagline = row?.Tagline, Website = row?.Website, Email = row?.Email, TermsText = row?.TermsText,
                HasSignature = !string.IsNullOrEmpty(row?.PrincipalSignaturePath),
                ShowDob = row?.ShowDob ?? true, ShowAddress = row?.ShowAddress ?? true,
                ShowBloodGroup = row?.ShowBloodGroup ?? true, ShowMotherName = row?.ShowMotherName ?? true,
                IsDefault = row == null, UpdatedAt = row?.UpdatedAt,
                Templates = IdCardTemplates.All.Select(x => new IdCardTemplateInfoDto
                {
                    Code = x.Code, Name = x.Name, Description = x.Description, DefaultPrimaryColor = x.DefaultPrimary, DefaultSecondaryColor = x.DefaultSecondary
                }).ToList()
            };
            return dto;
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
