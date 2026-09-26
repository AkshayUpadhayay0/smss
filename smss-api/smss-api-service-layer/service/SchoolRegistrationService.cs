using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;
using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_service_layer.service
{
    public class SchoolRegistrationService: ISchoolRegistrationService
    {
        private readonly ISchoolRegistrationRepository _repo;
        private readonly ILogger<SchoolRegistrationService> _logger;

        public SchoolRegistrationService(ISchoolRegistrationRepository repo, ILogger<SchoolRegistrationService> logger)
        {
            _repo = repo;
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
                    return Fail(500, "'School Admin' role is not configured in lut_roles");

                var now = DateTime.UtcNow;
                var schoolId = $"sch{now.Year}{await _repo.GetNextSchoolNumberAsync():D3}";

                var school = new TbSchools
                {
                    SchoolId = schoolId,
                    SchoolCode = code,
                    CreatedAt = now
                };
                ApplySchoolFields(school, req, now);
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
                    StatusId = school.SchoolStatusId,
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
            catch (Exception ex) { return Error(ex, "updating school"); }
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
            s.LogoUrl = Clean(r.LogoUrl);
            s.SubscriptionPlanId = r.SubscriptionPlanId;
            s.SubscriptionStartDate = r.SubscriptionStartDate;
            s.SubscriptionEndDate = r.SubscriptionEndDate;
            s.SubscriptionStatusId = r.SubscriptionStatusId;
            s.SchoolStatusId = r.SchoolStatusId;
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

        private static ApiResponse<object> Ok(int code, string message, object data) =>
            new() { Status = true, StatusCode = code, Message = message, Data = data };

        private static ApiResponse<object> Fail(int code, string message) =>
            new() { Status = false, StatusCode = code, Message = message, Data = null };

        private ApiResponse<object> Error(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Fail(500, "Something went wrong. Please try again later.");
        }

        public async Task<ApiResponse<object>> ToggleSchoolStatusAsync(string schoolId)
        {
            try
            {
                var school = await _repo.GetSchoolByIdAsync(schoolId, track: true);
                if (school == null) return Fail(404, "School not found");

                var activeId = await _repo.GetStatusIdByNameAsync("Active", "general status");
                var inactiveId = await _repo.GetStatusIdByNameAsync("Inactive", "general status");
                if (activeId == null || inactiveId == null)
                    return Fail(500, "'Active'/'Inactive' status is not configured in lut_status");

                var newStatusId = school.SchoolStatusId == activeId ? inactiveId : activeId;
                var now = DateTime.UtcNow;

                school.SchoolStatusId = newStatusId;
                school.UpdatedAt = now;

                // Keep the school's login in lockstep — an inactive school shouldn't still be able to log in
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
    }
}
