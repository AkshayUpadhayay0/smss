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
    public class SchoolRegistrationService : ISchoolRegistrationService
    {
        private const string InvalidSelectionMessage =
            "Invalid selection for location, type, level, board or subscription status. Please re-check and try again.";

        private readonly ISchoolRegistrationRepository _repo;
        private readonly ILogger<SchoolRegistrationService> _logger;
        private readonly IFileStorageService _storage;

        public SchoolRegistrationService( ISchoolRegistrationRepository repo, IFileStorageService storage, ILogger<SchoolRegistrationService> logger)
        {
            _repo = repo;
            _storage = storage;
            _logger = logger;
        }

        // ---------------- GET ----------------
        public async Task<ApiResponse<object>> GetSchoolsAsync()
        {
            try
            {
                var schools = await _repo.GetSchoolsAsync();
                if (schools.Count == 0) return Fail(404, "No schools found");
                return Ok(200, "Successfully fetched", schools.Select(SchoolMapper.ToResponse).ToList());
            }
            catch (Exception ex) { return Error(ex, "fetching schools"); }
        }

        public async Task<ApiResponse<object>> GetSchoolByIdAsync(string schoolId)
        {
            try
            {
                var school = await _repo.GetSchoolByIdAsync(schoolId);
                if (school == null) return Fail(404, "School not found");
                return Ok(200, "Successfully fetched", SchoolMapper.ToResponse(school));
            }
            catch (Exception ex) { return Error(ex, "fetching school"); }
        }

        // ---------------- INSERT ----------------
        public async Task<ApiResponse<object>> RegisterSchoolAsync(CreateSchoolRequest req)
        {
            try
            {
                var code = req.SchoolCode.Trim();
                var gstin = Clean(req.SchoolGstin)?.ToUpperInvariant();
                var pan = Clean(req.SchoolPan)?.ToUpperInvariant();

                var duplicates = await _repo.GetDuplicateFieldsAsync(code, gstin, pan);
                if (duplicates.Count > 0)
                    return Fail(409, $"Already exists: {string.Join(", ", duplicates)}");

                var roleId = await _repo.GetRoleIdByNameAsync(RoleNames.SchoolAdmin);
                if (roleId == null)
                    return ConfigError($"Role '{RoleNames.SchoolAdmin}' is missing in lut_roles");

                // New schools always start Active, looked up by name so no sid is hard-coded
                var activeStatusId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType);
                if (activeStatusId == null)
                    return ConfigError($"Status '{StatusNames.Active}' ({StatusNames.GeneralType}) is missing in lut_status");

                var now = DateTime.UtcNow;
                var schoolId = $"sch{now.Year}{await _repo.GetNextSchoolNumberAsync():D3}";

                var school = new TbSchools
                {
                    SchoolId = schoolId,
                    SchoolCode = code,
                    SchoolStatusId = activeStatusId,     // set here, never taken from the client
                    CreatedAt = now
                };
                ApplySchoolFields(school, req, now);     // does not touch SchoolStatusId
                school.SchoolGstin = gstin;
                school.SchoolPan = pan;

                foreach (var c in req.Contacts)
                {
                    var contact = new TbSchoolContacts { SchoolId = schoolId, CreatedAt = now };
                    ApplyContactFields(contact, c, now);
                    school.Contacts.Add(contact);
                }

                var tempPassword = PasswordHelper.GenerateTemporaryPassword();
                var user = new TbUsers
                {
                    SchoolId = schoolId,
                    UserType = UserTypes.SchoolId,
                    OrgUserId = schoolId,
                    Username = schoolId,
                    Email = school.Email,
                    MobileNumber = school.MobileNumber,
                    PasswordHash = PasswordHelper.Hash(tempPassword),
                    IsFirstLogin = true,
                    StatusId = activeStatusId,           // login starts Active together with the school
                    CreatedAt = now,
                    UpdatedAt = now,
                    UserRoles = { new TbUserRoles { RoleId = roleId.Value, CreatedAt = now } }
                };

                await _repo.AddSchoolWithAdminUserAsync(school, user);

                return Ok(201, "School registered successfully", new SchoolRegistrationResponse
                {
                    School = SchoolMapper.ToResponse(school),
                    Username = user.Username!,
                    TemporaryPassword = tempPassword
                });
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Two requests raced past the duplicate check; the DB constraint caught it
                return Fail(409, "School code, GSTIN or PAN already exists");
            }
            catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
            {
                _logger.LogWarning(ex, "Invalid lookup/location reference while registering school");
                return Fail(400, InvalidSelectionMessage);
            }
            catch (Exception ex) { return Error(ex, "registering school"); }
        }

        // ---------------- UPDATE ----------------
        public async Task<ApiResponse<object>> UpdateSchoolAsync(string schoolId, UpdateSchoolRequest req)
        {
            try
            {
                var school = await _repo.GetSchoolByIdAsync(schoolId, track: true);
                if (school == null) return Fail(404, "School not found");

                var gstin = Clean(req.SchoolGstin)?.ToUpperInvariant();
                var pan = Clean(req.SchoolPan)?.ToUpperInvariant();

                var duplicates = await _repo.GetDuplicateFieldsAsync(null, gstin, pan, excludeSchoolId: schoolId);
                if (duplicates.Count > 0)
                    return Fail(409, $"Already exists: {string.Join(", ", duplicates)}");

                var now = DateTime.UtcNow;
                ApplySchoolFields(school, req, now);
                school.SchoolGstin = gstin;
                school.SchoolPan = pan;

                // Contacts: with ContactId = update, without = add. Nothing is deleted here.
                TbSchoolContacts? primary = null;
                foreach (var c in req.Contacts)
                {
                    TbSchoolContacts entity;
                    if (c.ContactId.HasValue)
                    {
                        var existing = school.Contacts.FirstOrDefault(x => x.ContactId == c.ContactId.Value);
                        if (existing == null)
                            return Fail(400, $"Contact {c.ContactId} does not belong to this school");
                        entity = existing;
                    }
                    else
                    {
                        entity = new TbSchoolContacts { SchoolId = school.SchoolId, CreatedAt = now };
                        school.Contacts.Add(entity);
                    }

                    ApplyContactFields(entity, c, now);
                    if (c.IsPrimary) primary = entity;
                }

                // Keep exactly one primary contact
                if (primary != null)
                    foreach (var other in school.Contacts.Where(x => !ReferenceEquals(x, primary)))
                        other.IsPrimary = false;

                await _repo.SaveChangesAsync();
                return Ok(200, "School updated successfully", SchoolMapper.ToResponse(school));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                return Fail(409, "GSTIN or PAN already exists");
            }
            catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
            {
                _logger.LogWarning(ex, "Invalid lookup/location reference while updating school {SchoolId}", schoolId);
                return Fail(400, InvalidSelectionMessage);
            }
            catch (Exception ex) { return Error(ex, "updating school"); }
        }

        // ---------------- TOGGLE STATUS ----------------
        public async Task<ApiResponse<object>> ToggleSchoolStatusAsync(string schoolId)
        {
            try
            {
                var school = await _repo.GetSchoolByIdAsync(schoolId, track: true);
                if (school == null) return Fail(404, "School not found");

                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType);
                var inactiveId = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType);
                if (activeId == null || inactiveId == null)
                    return ConfigError("'Active'/'Inactive' general status is missing in lut_status");

                var newStatusId = school.SchoolStatusId == activeId ? inactiveId : activeId;
                var now = DateTime.UtcNow;

                school.SchoolStatusId = newStatusId;
                school.UpdatedAt = now;

                // Keep the school's login in lockstep: an inactive school must not be able to log in
                var user = await _repo.GetUserBySchoolIdAsync(schoolId, track: true);
                if (user != null)
                {
                    user.StatusId = newStatusId;
                    user.UpdatedAt = now;
                }

                await _repo.SaveChangesAsync();

                var message = newStatusId == activeId ? "School activated successfully" : "School deactivated successfully";
                return Ok(200, message, SchoolMapper.ToResponse(school));
            }
            catch (Exception ex) { return Error(ex, "toggling school status"); }
        }

        // ---------------- LOGO ----------------
        public async Task<ApiResponse<object>> UploadLogoAsync(
            string schoolId, Stream content, string fileName, long length, CancellationToken ct)
        {
            string? savedUrl = null;
            var committed = false;
            try
            {
                if (length <= 0) return Fail(400, "Logo file is empty.");
                if (length > LogoRules.MaxBytes) return Fail(400, "Logo must be 2 MB or smaller.");

                var extension = Path.GetExtension(fileName).ToLowerInvariant();
                if (!LogoRules.AllowedExtensions.Contains(extension))
                    return Fail(400, "Only PNG, JPG or WEBP images are allowed.");

                var school = await _repo.GetSchoolByIdAsync(schoolId, track: true);
                if (school == null) return Fail(404, "School not found");
                if (!SchoolCodeRules.IsFolderSafe(school.SchoolCode))
                    return Fail(400, "This school's code can't be used as a folder name. Please contact support.");

                // Buffer (max 2 MB) so we check the real size and signature, not just the claimed ones
                using var buffer = new MemoryStream();
                await content.CopyToAsync(buffer, ct);
                if (buffer.Length > LogoRules.MaxBytes) return Fail(400, "Logo must be 2 MB or smaller.");

                var header = buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 12)).ToArray();
                if (!LogoRules.SignatureMatches(extension, header))
                    return Fail(400, "The file is not a valid PNG, JPG or WEBP image.");
                buffer.Position = 0;

                savedUrl = await _storage.SaveSchoolLogoAsync(school.SchoolCode, buffer, extension, ct);

                var oldUrl = school.LogoUrl;
                school.LogoUrl = savedUrl;
                school.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync();
                committed = true;

                if (!string.IsNullOrEmpty(oldUrl)) _storage.Delete(oldUrl);   // replaced: remove the previous file

                return Ok(200, "Logo uploaded successfully", SchoolMapper.ToResponse(school));
            }
            catch (Exception ex)
            {
                if (savedUrl != null && !committed) _storage.Delete(savedUrl);   // no orphan file if the DB save failed
                return Error(ex, "uploading logo");
            }
        }

        public async Task<ApiResponse<object>> RemoveLogoAsync(string schoolId)
        {
            try
            {
                var school = await _repo.GetSchoolByIdAsync(schoolId, track: true);
                if (school == null) return Fail(404, "School not found");

                var oldUrl = school.LogoUrl;
                if (string.IsNullOrEmpty(oldUrl))
                    return Ok(200, "School has no logo", SchoolMapper.ToResponse(school));

                school.LogoUrl = null;
                school.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync();

                _storage.Delete(oldUrl);
                return Ok(200, "Logo removed successfully", SchoolMapper.ToResponse(school));
            }
            catch (Exception ex) { return Error(ex, "removing logo"); }
        }

        // ---------------- helpers ----------------
        private static void ApplySchoolFields(TbSchools s, SchoolBaseRequest r, DateTime now)
        {
            s.SchoolName = r.SchoolName.Trim();
            s.SchoolShortName = Clean(r.SchoolShortName);
            s.SchoolTypeId = r.SchoolTypeId;
            s.SchoolLevelId = r.SchoolLevelId;
            s.BoardTypeId = r.BoardTypeId;
            s.SchoolEstablishYear = r.SchoolEstablishYear;
            s.CountryId = r.CountryId;
            s.StateId = r.StateId;
            s.DistrictId = r.DistrictId;
            s.CityId = r.CityId;
            s.AddressLine1 = Clean(r.AddressLine1);
            s.AddressLine2 = Clean(r.AddressLine2);
            s.Pincode = Clean(r.Pincode);
            s.Email = Clean(r.Email);
            s.MobileNumber = Clean(r.MobileNumber);
            s.Website = Clean(r.Website);
            //s.LogoUrl = Clean(r.LogoUrl);
            s.SubscriptionPlanId = r.SubscriptionPlanId;
            s.SubscriptionStartDate = r.SubscriptionStartDate;
            s.SubscriptionEndDate = r.SubscriptionEndDate;
            s.SubscriptionStatusId = r.SubscriptionStatusId;
            s.UpdatedAt = now;
        }

        private static void ApplyContactFields(TbSchoolContacts c, SchoolContactRequest r, DateTime now)
        {
            c.ContactType = r.ContactType.Trim();
            c.ContactName = r.ContactName.Trim();
            c.Designation = Clean(r.Designation);
            c.Email = Clean(r.Email);
            c.MobileNumber = Clean(r.MobileNumber);
            c.AlternateMobileNumber = Clean(r.AlternateMobileNumber);
            c.IsPrimary = r.IsPrimary;
            c.StatusId = r.StatusId;
            c.UpdatedAt = now;
        }

        // Empty strings become null so blank GSTIN/PAN never collide on the unique constraints
        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static bool IsUniqueViolation(DbUpdateException ex) =>
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

        private static bool IsForeignKeyViolation(DbUpdateException ex) =>
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation };

        private static ApiResponse<object> Ok(int code, string message, object data) =>
            new() { Status = true, StatusCode = code, Message = message, Data = data };

        private static ApiResponse<object> Fail(int code, string message) =>
            new() { Status = false, StatusCode = code, Message = message, Data = null };

        private ApiResponse<object> Error(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Fail(500, "Something went wrong. Please try again later.");
        }

        // Missing seed data: detail goes to the log, the client gets a generic message
        private ApiResponse<object> ConfigError(string detail)
        {
            _logger.LogError("Configuration problem: {Detail}", detail);
            return Fail(500, "The system is not configured correctly. Please contact support.");
        }
    }
}