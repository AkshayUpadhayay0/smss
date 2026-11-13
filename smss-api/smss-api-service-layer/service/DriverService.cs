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
    public class DriverService : TransportServiceBase, IDriverService
    {
        private const string EmailIndex = "uq_tb_drivers_school_email";
        private const string DlIndex = "uq_tb_drivers_active_dl";

        private readonly IDriverRepository _repo;
        private readonly IFileStorageService _storage;
        private readonly INotificationService _notify;

        public DriverService(IDriverRepository repo, IFileStorageService storage, INotificationService notify, ILogger<DriverService> logger) : base(logger)
        {
            _repo = repo;
            _storage = storage;
            _notify = notify;
        }

        private Task<int?> ActiveStatusIdAsync(CancellationToken ct) => _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);

        // ---------------------------------------------------------------- lookups / list / get
        public async Task<ApiResponse<DriverLookupsDto>> GetLookupsAsync(string? schoolId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<DriverLookupsDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await ActiveStatusIdAsync(ct);
                if (activeId == null) return ConfigError<DriverLookupsDto>($"Status '{StatusNames.Active}' ({StatusNames.GeneralType}) is missing in lut_status");
                return Result(200, "Successfully fetched", new DriverLookupsDto
                {
                    Vehicles = (await _repo.GetActiveVehiclesAsync(schoolId, activeId.Value, ct)).Select(v => new LookupItemDto { Id = v.VehicleId, Name = $"{v.Label} ({v.Plate})" }).ToList(),
                    Areas = (await _repo.GetActiveAreasAsync(schoolId, activeId.Value, ct)).Select(a => new LookupItemDto { Id = a.AreaId, Name = a.Name }).ToList()
                });
            }
            catch (Exception ex) { return Error<DriverLookupsDto>(ex, "fetching driver lookups"); }
        }

        public async Task<ApiResponse<DriverPagedDto>> ListAsync(string? schoolId, string? search, string? status, long? vehicleId, int page, int pageSize, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<DriverPagedDto>(400, TenantMessages.NoSchool, null);
            if (PagingError(page, pageSize) is { } paging) return Result<DriverPagedDto>(400, paging, null);
            try
            {
                var (statusId, error, config) = await ParseStatusAsync(status, _repo.GetStatusIdByNameAsync, ct);
                if (error != null) return config ? ConfigError<DriverPagedDto>(error) : Result<DriverPagedDto>(400, error, null);

                var (rows, total) = await _repo.ListAsync(schoolId, search, statusId, vehicleId, page, pageSize, ct);
                var today = SchoolClock.Today();
                return Result(200, "Successfully fetched", new DriverPagedDto
                {
                    Total = total, Page = page, PageSize = pageSize,
                    Items = rows.Select(r =>
                    {
                        var dl = TransportRules.Expiry(r.Driver.DlValidUpto, today, out var days);
                        return new DriverListItemDto
                        {
                            DriverId = r.Driver.DriverId, FullName = r.Driver.FullName, MobileNumber = r.Driver.MobileNumber, DlNumber = r.Driver.DlNumber,
                            DlValidUpto = r.Driver.DlValidUpto, DlStatus = dl, DlDaysLeft = days, VehicleId = r.Driver.VehicleId, VehicleLabel = r.VehicleLabel, VehiclePlate = r.VehiclePlate,
                            AreaNames = r.AreaNames, StatusName = r.StatusName, PhotoUrl = DriverPhotos.ApiPath(r.Driver.DriverId, r.Driver.PhotoPath), HasLogin = r.HasLogin
                        };
                    }).ToList()
                });
            }
            catch (Exception ex) { return Error<DriverPagedDto>(ex, "listing drivers"); }
        }

        public async Task<ApiResponse<DriverDto>> GetAsync(string? schoolId, string driverId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<DriverDto>(400, TenantMessages.NoSchool, null);
            try
            {
                // another school's driver is a 404 too, so ids cannot be probed across schools
                var row = await _repo.GetDetailAsync(schoolId, driverId, ct);
                return row == null ? Result<DriverDto>(404, "Driver not found.", null) : Result(200, "Successfully fetched", ToDto(row, await ActiveStatusIdAsync(ct)));
            }
            catch (Exception ex) { return Error<DriverDto>(ex, "fetching a driver"); }
        }

        // ---------------------------------------------------------------- create / update
        public async Task<ApiResponse<DriverDto>> CreateAsync(string? schoolId, DriverRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<DriverDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await ActiveStatusIdAsync(ct);
                if (activeId == null) return ConfigError<DriverDto>($"Status '{StatusNames.Active}' ({StatusNames.GeneralType}) is missing in lut_status");

                var school = await _repo.GetSchoolAsync(schoolId, ct);
                if (school == null) return Result<DriverDto>(404, "School not found.", null);
                if (string.IsNullOrWhiteSpace(school.SchoolShortCode)) return Result<DriverDto>(409, StudentIdHelper.NoShortCodeMessage, null);

                var (input, error) = Normalize(req);
                if (input == null) return Result<DriverDto>(400, error!, null);
                var refs = await CheckReferencesAsync(schoolId, input, activeId.Value, null, ct);
                if (refs != null) return Result<DriverDto>(refs.Value.Code, refs.Value.Message, null);
                var clash = await CheckUniqueAsync(schoolId, input, activeId.Value, null, true, ct);
                if (clash != null) return Result<DriverDto>(409, clash, null);

                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockDriverIdsAsync(schoolId, ct);   // one new driver at a time per school gets the next number
                var prefix = DriverIdHelper.Prefix(school.SchoolShortCode!);
                var driverId = DriverIdHelper.Build(school.SchoolShortCode!, await _repo.GetMaxIdSequenceAsync(schoolId, prefix, ct) + 1);

                var now = DateTime.UtcNow;
                var driver = new TbDriver { DriverId = driverId, SchoolId = schoolId, StatusId = activeId.Value, CreatedAt = now, UpdatedAt = now };
                Apply(driver, input);
                await _repo.AddAsync(driver, ct);
                await _repo.SaveChangesAsync(ct);   // the driver row first: the link rows reference it
                await _repo.ReplaceAreasAsync(schoolId, driverId, input.AreaIds, ct);
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return await ReplyAsync(schoolId, driverId, 201, "Driver created successfully.", ct);
            }
            catch (DbUpdateException ex) when (ConstraintOf(ex) is EmailIndex or DlIndex)
            {
                return Result<DriverDto>(409, ConstraintMessage(ConstraintOf(ex)), null);
            }
            catch (Exception ex) { return Error<DriverDto>(ex, "creating a driver"); }
        }

        public async Task<ApiResponse<DriverDto>> UpdateAsync(string? schoolId, string driverId, DriverRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<DriverDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await ActiveStatusIdAsync(ct);
                if (activeId == null) return ConfigError<DriverDto>($"Status '{StatusNames.Active}' ({StatusNames.GeneralType}) is missing in lut_status");

                var driver = await _repo.GetForUpdateAsync(schoolId, driverId, ct);
                if (driver == null) return Result<DriverDto>(404, "Driver not found.", null);

                var (input, error) = Normalize(req);
                if (input == null) return Result<DriverDto>(400, error!, null);
                var refs = await CheckReferencesAsync(schoolId, input, activeId.Value, driver, ct);
                if (refs != null) return Result<DriverDto>(refs.Value.Code, refs.Value.Message, null);
                var clash = await CheckUniqueAsync(schoolId, input, activeId.Value, driverId, driver.StatusId == activeId, ct);
                if (clash != null) return Result<DriverDto>(409, clash, null);

                await using var tx = await _repo.BeginTransactionAsync(ct);
                Apply(driver, input);
                driver.UpdatedAt = DateTime.UtcNow;
                await _repo.ReplaceAreasAsync(schoolId, driverId, input.AreaIds, ct);   // the new set replaces the old one; unlisted link rows are deleted
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return await ReplyAsync(schoolId, driverId, 200, "Driver updated successfully.", ct);
            }
            catch (DbUpdateException ex) when (ConstraintOf(ex) is EmailIndex or DlIndex)
            {
                return Result<DriverDto>(409, ConstraintMessage(ConstraintOf(ex)), null);
            }
            catch (Exception ex) { return Error<DriverDto>(ex, "updating a driver"); }
        }

        /// <summary>
        /// Flips Active / Inactive (the hide flag). The driver's login, if any, follows it, so a hidden driver cannot still sign in, and deactivating also
        /// revokes the login's refresh tokens (the session cannot be renewed). Reactivating is refused while the driver's vehicle or an assigned area is Inactive.
        /// </summary>
        public async Task<ApiResponse<DriverDto>> ToggleStatusAsync(string? schoolId, string driverId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<DriverDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await ActiveStatusIdAsync(ct);
                var inactiveId = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType, ct);
                if (activeId == null || inactiveId == null) return ConfigError<DriverDto>("Active / Inactive statuses are missing in lut_status");

                var driver = await _repo.GetForUpdateAsync(schoolId, driverId, ct);
                if (driver == null) return Result<DriverDto>(404, "Driver not found.", null);

                var deactivating = driver.StatusId == activeId;
                if (!deactivating)
                {
                    var problems = new List<string>();
                    if (driver.VehicleId != null)
                    {
                        var vehicle = await _repo.GetVehicleAsync(schoolId, driver.VehicleId.Value, ct);
                        if (vehicle != null && vehicle.StatusId != activeId) problems.Add($"vehicle '{vehicle.Label}' is inactive");
                    }
                    foreach (var area in await _repo.GetAssignedAreasAsync(schoolId, driverId, ct))
                        if (area.StatusId != activeId) problems.Add($"pickup area '{area.Name}' is inactive");
                    if (problems.Count > 0)
                        return Result<DriverDto>(409, $"This driver can't be activated while {string.Join(" and ", problems)}. Activate it, or change the driver's assignment, first.", null);
                    if (await _repo.ActiveDlTakenAsync(schoolId, driver.DlNumber, activeId.Value, driverId, ct))
                        return Result<DriverDto>(409, "This driver can't be activated: another active driver already has this licence number.", null);
                }

                driver.StatusId = deactivating ? inactiveId.Value : activeId.Value;
                driver.UpdatedAt = DateTime.UtcNow;
                var login = await SyncLoginAsync(schoolId, driver, activeId.Value, inactiveId.Value, ct);
                await _repo.SaveChangesAsync(ct);
                // a deactivated driver's session cannot be renewed: every refresh token of the login is revoked
                if (deactivating && login != null) await _repo.RevokeRefreshTokensAsync(login.UserId, DateTime.UtcNow, ct);

                return await ReplyAsync(schoolId, driverId, 200, deactivating ? "Driver deactivated." : "Driver activated.", ct);
            }
            catch (DbUpdateException ex) when (ConstraintOf(ex) is DlIndex)
            {
                return Result<DriverDto>(409, ConstraintMessage(DlIndex), null);
            }
            catch (Exception ex) { return Error<DriverDto>(ex, "toggling a driver's status"); }
        }

        // ---------------------------------------------------------------- photo
        public async Task<ApiResponse<DriverPhotoDto>> UploadPhotoAsync(string? schoolId, string driverId, Stream content, string fileName, long length, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<DriverPhotoDto>(400, TenantMessages.NoSchool, null);
            string? savedKey = null;
            var committed = false;
            try
            {
                var (image, error) = await ImageUploadValidator.ValidateAsync(content, fileName, length, "Photo", ct);   // 2 MB, PNG / JPG / WEBP, signature checked
                if (image == null) return Result<DriverPhotoDto>(400, error!, null);
                using var _ = image;

                var driver = await _repo.GetForUpdateAsync(schoolId, driverId, ct);
                if (driver == null) return Result<DriverPhotoDto>(404, "Driver not found.", null);

                var school = await _repo.GetSchoolAsync(schoolId, ct);
                if (!SchoolCodeRules.IsFolderSafe(school?.SchoolCode))
                    return Result<DriverPhotoDto>(400, "This school's code can't be used as a folder name. Please contact support.", null);

                savedKey = await _storage.SaveDriverPhotoAsync(school!.SchoolCode, driverId, image.Content, image.Extension, ct);
                var oldKey = driver.PhotoPath;
                driver.PhotoPath = savedKey;
                driver.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                committed = true;

                if (!string.IsNullOrEmpty(oldKey)) _storage.DeletePhoto(oldKey);   // replaced: remove the previous file
                return Result(200, "Photo uploaded successfully.", new DriverPhotoDto { DriverId = driverId, PhotoUrl = DriverPhotos.ApiPath(driverId, savedKey) });
            }
            catch (Exception ex)
            {
                if (savedKey != null && !committed) _storage.DeletePhoto(savedKey);   // no orphan file if the DB save failed
                return Error<DriverPhotoDto>(ex, "uploading a driver photo");
            }
        }

        public async Task<ApiResponse<DriverPhotoDto>> RemovePhotoAsync(string? schoolId, string driverId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<DriverPhotoDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var driver = await _repo.GetForUpdateAsync(schoolId, driverId, ct);
                if (driver == null) return Result<DriverPhotoDto>(404, "Driver not found.", null);

                var oldKey = driver.PhotoPath;
                driver.PhotoPath = null;
                driver.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                if (!string.IsNullOrEmpty(oldKey)) _storage.DeletePhoto(oldKey);
                return Result(200, "Photo removed.", new DriverPhotoDto { DriverId = driverId, PhotoUrl = null });
            }
            catch (Exception ex) { return Error<DriverPhotoDto>(ex, "removing a driver photo"); }
        }

        // ---------------------------------------------------------------- login
        /// <summary>
        /// Same shape as the employee create-login: the login id is the driver's own id, a random temporary password is generated and returned ONCE (only its hash is
        /// stored) with is_first_login = true, and it is emailed when there is an address. Unlike the employee version nobody chooses the role: it is always DRIVER.
        /// </summary>
        public async Task<ApiResponse<DriverLoginCreatedDto>> CreateLoginAsync(string? schoolId, string driverId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<DriverLoginCreatedDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var driver = await _repo.GetForUpdateAsync(schoolId, driverId, ct);
                if (driver == null) return Result<DriverLoginCreatedDto>(404, "Driver not found.", null);

                var school = await _repo.GetSchoolAsync(schoolId, ct);
                if (school == null) return Result<DriverLoginCreatedDto>(404, "School not found.", null);

                var activeId = await ActiveStatusIdAsync(ct);
                if (activeId == null) return ConfigError<DriverLoginCreatedDto>($"Status '{StatusNames.Active}' ({StatusNames.GeneralType}) is missing in lut_status");
                if (driver.StatusId != activeId) return Result<DriverLoginCreatedDto>(409, "This driver record is inactive, so a login cannot be created.", null);

                if (await _repo.HasLoginAsync(schoolId, driverId, ct)) return Result<DriverLoginCreatedDto>(409, "This driver already has a login.", null);

                // the role is read from lut_roles by code; it must exist, be active and be on the driver allow-list (never Super Admin, Student or Guardian)
                var role = await _repo.GetRoleByCodeAsync(RoleCodes.Driver, ct);
                if (role == null || role.StatusId != activeId || !RoleCodes.AssignableToDrivers.Contains(role.RoleCode))
                    return ConfigError<DriverLoginCreatedDto>("Role DRIVER is missing or inactive in lut_roles");

                var tempPassword = PasswordHelper.GenerateTemporaryPassword();
                var now = DateTime.UtcNow;
                var displayName = driver.FullName.Length > 100 ? driver.FullName[..100] : driver.FullName;   // tb_users.display_name is 100 characters
                var user = new TbUsers
                {
                    SchoolId = schoolId, UserType = UserTypes.Driver, OrgUserId = driver.DriverId, DisplayName = displayName,
                    Email = driver.Email, MobileNumber = driver.MobileNumber,
                    PasswordHash = PasswordHelper.Hash(tempPassword), IsFirstLogin = true, StatusId = activeId, CreatedAt = now, UpdatedAt = now
                };
                user.UserRoles.Add(new TbUserRoles { RoleId = role.RoleId, CreatedAt = now });
                await _repo.AddUserAsync(user, ct);

                // a failed email never fails the login creation (the password is shown on screen once anyway)
                var sentTo = await _notify.SendAsync(user.Email, null,
                    EmailTemplates.DriverCredentials(driver.FullName, school.SchoolName, driver.DriverId, tempPassword, _notify.LoginUrl),
                    $"driver login credentials for {driver.DriverId}", ct);

                return Result(201, "Driver login created successfully.", new DriverLoginCreatedDto
                {
                    DriverId = driver.DriverId, DisplayName = displayName, OrgUserId = driver.DriverId, TemporaryPassword = tempPassword,
                    Role = role.RoleName, EmailSent = sentTo != null, EmailSentTo = sentTo
                });
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Two clicks at once: the unique (school_id, org_user_id) index let only one through
                return Result<DriverLoginCreatedDto>(409, "This driver already has a login.", null);
            }
            catch (Exception ex) { return Error<DriverLoginCreatedDto>(ex, "creating a driver login"); }
        }

        // ---------------------------------------------------------------- helpers
        /// <summary>
        /// A login works only while the driver row is Active. The login's status is recomputed from the driver's every time it changes. Returns the login
        /// (null when the driver has none). The caller saves.
        /// </summary>
        private async Task<TbUsers?> SyncLoginAsync(string schoolId, TbDriver driver, int activeStatusId, int inactiveStatusId, CancellationToken ct)
        {
            var login = await _repo.GetLoginUserAsync(schoolId, driver.DriverId, ct);
            if (login == null) return null;
            var wanted = driver.StatusId == activeStatusId ? activeStatusId : inactiveStatusId;
            if (login.StatusId != wanted)
            {
                login.StatusId = wanted;
                login.UpdatedAt = DateTime.UtcNow;
            }
            return login;
        }

        private sealed record Input(string FullName, string Mobile, string? Email, string Address, string Dl, DateOnly DlValidUpto, long? VehicleId, List<long> AreaIds);

        /// <summary>Trims and normalises the request. Blank optional text becomes null; the licence number is stored without spaces, upper-cased.</summary>
        private static (Input? Input, string? Error) Normalize(DriverRequest r)
        {
            var name = r.FullName?.Trim() ?? string.Empty;
            if (name.Length == 0) return (null, "Full name is required.");
            var address = r.Address?.Trim() ?? string.Empty;
            if (address.Length == 0) return (null, "Address is required.");
            var dl = TransportRules.NormalizeDl(r.DlNumber);
            if (!TransportRules.IsValidDl(dl)) return (null, "Enter a valid driving licence number (5 to 30 letters, digits, - or /).");
            var today = SchoolClock.Today();
            if (r.DlValidUpto!.Value < new DateOnly(2000, 1, 1) || r.DlValidUpto.Value > today.AddYears(50)) return (null, "Enter a valid licence valid-upto date.");

            var areaIds = (r.AreaIds ?? new List<long>()).Distinct().ToList();
            if (areaIds.Any(id => id <= 0)) return (null, "One or more of the selected areas are not valid.");
            return (new Input(name, r.MobileNumber!.Trim(), Clean(r.Email), address, dl, r.DlValidUpto.Value, r.VehicleId, areaIds), null);
        }

        private static void Apply(TbDriver d, Input i)
        {
            d.FullName = i.FullName; d.MobileNumber = i.Mobile; d.Email = i.Email; d.Address = i.Address;
            d.DlNumber = i.Dl; d.DlValidUpto = i.DlValidUpto; d.VehicleId = i.VehicleId;
        }

        /// <summary>
        /// The vehicle and every area must exist IN THIS SCHOOL (another school's id is a 404, the same as an id that does not exist) and be Active. The one
        /// exception: an Inactive driver may keep what it already has, so editing a hidden record does not force its history to change.
        /// </summary>
        private async Task<(int Code, string Message)?> CheckReferencesAsync(string schoolId, Input input, int activeId, TbDriver? existing, CancellationToken ct)
        {
            var driverActive = existing == null || existing.StatusId == activeId;
            if (input.VehicleId is { } vehicleId)
            {
                var vehicle = await _repo.GetVehicleAsync(schoolId, vehicleId, ct);
                if (vehicle == null) return (404, "The selected vehicle was not found.");
                var kept = !driverActive && existing!.VehicleId == vehicleId;
                if (vehicle.StatusId != activeId && !kept) return (400, $"Vehicle '{vehicle.Label}' is inactive. Choose an active vehicle.");
            }
            if (input.AreaIds.Count > 0)
            {
                var areas = await _repo.GetAreasAsync(schoolId, input.AreaIds, ct);
                if (areas.Count != input.AreaIds.Count) return (404, "One or more of the selected areas were not found.");
                var current = existing == null ? new HashSet<long>() : (await _repo.GetAssignedAreasAsync(schoolId, existing.DriverId, ct)).Select(a => a.AreaId).ToHashSet();
                var inactive = areas.Where(a => a.StatusId != activeId && !(!driverActive && current.Contains(a.AreaId))).ToList();
                if (inactive.Count > 0) return (400, $"Area {string.Join(", ", inactive.Select(a => $"'{a.Name}'"))} is inactive. Choose active areas.");
            }
            return null;
        }

        /// <summary>The 409 message when the email (per school) or, for an Active driver, the licence number (among Active drivers) is already used; null when free.</summary>
        private async Task<string?> CheckUniqueAsync(string schoolId, Input i, int activeId, string? exceptDriverId, bool driverIsActive, CancellationToken ct)
        {
            if (i.Email != null && await _repo.EmailTakenAsync(schoolId, i.Email, exceptDriverId, ct)) return ConstraintMessage(EmailIndex);
            if (driverIsActive && await _repo.ActiveDlTakenAsync(schoolId, i.Dl, activeId, exceptDriverId, ct)) return ConstraintMessage(DlIndex);
            return null;
        }

        private static string ConstraintMessage(string? constraint) => constraint == EmailIndex
            ? "Another driver in this school already uses this email."
            : "Another active driver in this school already has this licence number.";

        private async Task<ApiResponse<DriverDto>> ReplyAsync(string schoolId, string driverId, int code, string message, CancellationToken ct)
        {
            var row = await _repo.GetDetailAsync(schoolId, driverId, ct);
            return row == null ? Result<DriverDto>(500, "Something went wrong. Please try again later.", null) : Result(code, message, ToDto(row, await ActiveStatusIdAsync(ct)));
        }

        private static DriverDto ToDto(DriverDetailRow r, int? activeStatusId)
        {
            var d = r.Driver;
            var dl = TransportRules.Expiry(d.DlValidUpto, SchoolClock.Today(), out var days);
            return new DriverDto
            {
                DriverId = d.DriverId, FullName = d.FullName, MobileNumber = d.MobileNumber, Email = d.Email, Address = d.Address, DlNumber = d.DlNumber,
                DlValidUpto = d.DlValidUpto, DlStatus = dl, DlDaysLeft = days, VehicleId = d.VehicleId, VehicleLabel = r.VehicleLabel, VehiclePlate = r.VehiclePlate,
                Areas = r.Areas.Select(a => new DriverAreaDto { AreaId = a.AreaId, AreaName = a.Name, IsActive = a.StatusId == activeStatusId }).ToList(),
                StatusName = r.StatusName, PhotoUrl = DriverPhotos.ApiPath(d.DriverId, d.PhotoPath), HasLogin = r.LoginId != null, LoginId = r.LoginId,
                CreatedAt = d.CreatedAt, UpdatedAt = d.UpdatedAt
            };
        }
    }
}
