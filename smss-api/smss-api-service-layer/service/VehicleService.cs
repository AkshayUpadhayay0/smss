using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class VehicleService : TransportServiceBase, IVehicleService
    {
        private const string LabelIndex = "uq_tb_vehicles_active_label";
        private const string PlateIndex = "uq_tb_vehicles_active_plate";
        private readonly IVehicleRepository _repo;

        public VehicleService(IVehicleRepository repo, ILogger<VehicleService> logger) : base(logger) => _repo = repo;

        private Task<int?> ActiveStatusIdAsync(CancellationToken ct) => _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);

        public async Task<ApiResponse<VehicleLookupsDto>> GetLookupsAsync(string? schoolId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<VehicleLookupsDto>(400, TenantMessages.NoSchool, null);
            try
            {
                return Result(200, "Successfully fetched", new VehicleLookupsDto
                {
                    VehicleTypes = (await _repo.GetTypesAsync(ct)).Select(t => new LookupItemDto { Id = t.Id, Name = t.Name }).ToList(),
                    Ownerships = (await _repo.GetOwnershipsAsync(ct)).Select(o => new VehicleOwnershipDto { Id = o.Id, Name = o.Name, IsRented = IsRented(o.Code) }).ToList()
                });
            }
            catch (Exception ex) { return Error<VehicleLookupsDto>(ex, "fetching vehicle lookups"); }
        }

        public async Task<ApiResponse<VehiclePagedDto>> ListAsync(string? schoolId, string? search, string? status, long? vehicleTypeId, int page, int pageSize, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<VehiclePagedDto>(400, TenantMessages.NoSchool, null);
            if (PagingError(page, pageSize) is { } paging) return Result<VehiclePagedDto>(400, paging, null);
            try
            {
                var (statusId, error, config) = await ParseStatusAsync(status, _repo.GetStatusIdByNameAsync, ct);
                if (error != null) return config ? ConfigError<VehiclePagedDto>(error) : Result<VehiclePagedDto>(400, error, null);

                var (rows, total) = await _repo.ListAsync(schoolId, search, statusId, vehicleTypeId, page, pageSize, ct);
                var today = SchoolClock.Today();
                return Result(200, "Successfully fetched", new VehiclePagedDto { Total = total, Page = page, PageSize = pageSize, Items = rows.Select(r => ToDto(r, today)).ToList() });
            }
            catch (Exception ex) { return Error<VehiclePagedDto>(ex, "listing vehicles"); }
        }

        public async Task<ApiResponse<VehicleDto>> GetAsync(string? schoolId, long vehicleId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<VehicleDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var row = await _repo.GetDetailAsync(schoolId, vehicleId, ct);
                return row == null ? Result<VehicleDto>(404, "Vehicle not found.", null) : Result(200, "Successfully fetched", ToDto(row, SchoolClock.Today()));
            }
            catch (Exception ex) { return Error<VehicleDto>(ex, "fetching a vehicle"); }
        }

        public async Task<ApiResponse<VehicleDto>> CreateAsync(string? schoolId, VehicleRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<VehicleDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await ActiveStatusIdAsync(ct);
                if (activeId == null) return ConfigError<VehicleDto>($"Status '{StatusNames.Active}' ({StatusNames.GeneralType}) is missing in lut_status");

                var (input, error) = await ValidateAsync(req, ct);
                if (input == null) return Result<VehicleDto>(error!.Value.Code, error.Value.Message, null);
                var clash = await ActiveClashAsync(schoolId, input, activeId.Value, null, ct);
                if (clash != null) return Result<VehicleDto>(409, clash, null);

                var now = DateTime.UtcNow;
                var vehicle = new TbVehicle { SchoolId = schoolId, StatusId = activeId.Value, CreatedAt = now, UpdatedAt = now };
                Apply(vehicle, input);
                await _repo.AddAsync(vehicle, ct);
                await _repo.SaveChangesAsync(ct);
                return await ReplyAsync(schoolId, vehicle.VehicleId, 201, "Vehicle created successfully.", ct);
            }
            catch (DbUpdateException ex) when (ConstraintOf(ex) is LabelIndex or PlateIndex)
            {
                return Result<VehicleDto>(409, ConstraintOf(ex) == LabelIndex ? "An active vehicle with this number / label already exists." : "An active vehicle with this registration number already exists.", null);
            }
            catch (Exception ex) { return Error<VehicleDto>(ex, "creating a vehicle"); }
        }

        public async Task<ApiResponse<VehicleDto>> UpdateAsync(string? schoolId, long vehicleId, VehicleRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<VehicleDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await ActiveStatusIdAsync(ct);
                if (activeId == null) return ConfigError<VehicleDto>($"Status '{StatusNames.Active}' ({StatusNames.GeneralType}) is missing in lut_status");

                var vehicle = await _repo.GetForUpdateAsync(schoolId, vehicleId, ct);
                if (vehicle == null) return Result<VehicleDto>(404, "Vehicle not found.", null);

                var (input, error) = await ValidateAsync(req, ct);
                if (input == null) return Result<VehicleDto>(error!.Value.Code, error.Value.Message, null);
                // label and plate only have to be unique among ACTIVE vehicles; an inactive vehicle is checked when it is reactivated
                if (vehicle.StatusId == activeId)
                {
                    var clash = await ActiveClashAsync(schoolId, input, activeId.Value, vehicleId, ct);
                    if (clash != null) return Result<VehicleDto>(409, clash, null);
                }

                Apply(vehicle, input);
                vehicle.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                return await ReplyAsync(schoolId, vehicleId, 200, "Vehicle updated successfully.", ct);
            }
            catch (DbUpdateException ex) when (ConstraintOf(ex) is LabelIndex or PlateIndex)
            {
                return Result<VehicleDto>(409, ConstraintOf(ex) == LabelIndex ? "An active vehicle with this number / label already exists." : "An active vehicle with this registration number already exists.", null);
            }
            catch (Exception ex) { return Error<VehicleDto>(ex, "updating a vehicle"); }
        }

        public async Task<ApiResponse<VehicleDto>> ToggleStatusAsync(string? schoolId, long vehicleId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<VehicleDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await ActiveStatusIdAsync(ct);
                var inactiveId = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType, ct);
                if (activeId == null || inactiveId == null) return ConfigError<VehicleDto>("Active / Inactive statuses are missing in lut_status");

                var vehicle = await _repo.GetForUpdateAsync(schoolId, vehicleId, ct);
                if (vehicle == null) return Result<VehicleDto>(404, "Vehicle not found.", null);

                if (vehicle.StatusId == activeId)
                {
                    var drivers = await _repo.GetActiveDriversOfVehicleAsync(schoolId, vehicleId, activeId.Value, ct);
                    if (drivers.Count > 0)
                        return Result<VehicleDto>(409, $"'{vehicle.VehicleLabel}' is assigned to active driver(s): {NameDrivers(drivers)}. Reassign them first.", null);
                    vehicle.StatusId = inactiveId.Value;
                }
                else
                {
                    var clash = await ActiveClashAsync(schoolId, new VehicleInput(vehicle.VehicleLabel, vehicle.RegistrationNumber, 0, 0, false, null, null, null, null, null, null, null), activeId.Value, vehicleId, ct);
                    if (clash != null) return Result<VehicleDto>(409, clash, null);
                    vehicle.StatusId = activeId.Value;
                }
                vehicle.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                return await ReplyAsync(schoolId, vehicleId, 200, vehicle.StatusId == activeId ? "Vehicle activated." : "Vehicle deactivated.", ct);
            }
            catch (DbUpdateException ex) when (ConstraintOf(ex) is LabelIndex or PlateIndex)
            {
                return Result<VehicleDto>(409, "Another active vehicle already has this number / label or registration number.", null);
            }
            catch (Exception ex) { return Error<VehicleDto>(ex, "toggling a vehicle's status"); }
        }

        // ---------------------------------------------------------------- validation
        private sealed record VehicleInput(string Label, string Plate, long TypeId, long OwnershipId, bool Rented, short? ModelYear, short? Seats,
            string? OwnerName, string? OwnerContact, DateOnly? LastService, DateOnly? Puc, DateOnly? Insurance);

        private static bool IsRented(string ownershipCode) => string.Equals(ownershipCode, TransportRules.RentedOwnershipCode, StringComparison.OrdinalIgnoreCase);

        private async Task<(VehicleInput? Input, (int Code, string Message)? Error)> ValidateAsync(VehicleRequest r, CancellationToken ct)
        {
            var label = r.VehicleLabel?.Trim() ?? string.Empty;
            if (label.Length == 0) return (null, (400, "Vehicle number / label is required."));
            var plate = TransportRules.NormalizePlate(r.RegistrationNumber);
            if (!TransportRules.IsValidPlate(plate)) return (null, (400, "Enter a valid registration number (4 to 20 letters, digits or hyphens)."));

            var type = await _repo.GetActiveTypeAsync(r.VehicleTypeId!.Value, ct);
            if (type == null) return (null, (400, "The selected vehicle type was not found."));
            var ownership = await _repo.GetActiveOwnershipAsync(r.OwnershipId!.Value, ct);
            if (ownership == null) return (null, (400, "The selected ownership was not found."));

            var today = SchoolClock.Today();
            if (r.ModelYear != null && (r.ModelYear < 1950 || r.ModelYear > today.Year + 1)) return (null, (400, $"Model year must be between 1950 and {today.Year + 1}."));
            if (r.LastServiceDate != null && r.LastServiceDate > today) return (null, (400, "Last service date cannot be in the future."));

            var rented = IsRented(ownership.Code);
            var ownerName = Clean(r.OwnerName);
            var ownerContact = Clean(r.OwnerContact);
            if (!rented && (ownerName != null || ownerContact != null)) return (null, (400, "Owner / vendor details apply to rented vehicles only."));
            if (ownerContact != null && !TransportRules.IsValidContact(ownerContact)) return (null, (400, "Enter a valid owner / vendor contact number."));

            return (new VehicleInput(label, plate, type.Id, ownership.Id, rented, r.ModelYear, r.SeatingCapacity, ownerName, ownerContact, r.LastServiceDate, r.PucExpiryDate, r.InsuranceExpiryDate), null);
        }

        /// <summary>The 409 message when another ACTIVE vehicle of the school already has this label or plate; null when there is no clash.</summary>
        private async Task<string?> ActiveClashAsync(string schoolId, VehicleInput i, int activeId, long? exceptVehicleId, CancellationToken ct)
        {
            if (await _repo.ActiveLabelTakenAsync(schoolId, i.Label, activeId, exceptVehicleId, ct)) return $"An active vehicle named '{i.Label}' already exists.";
            if (await _repo.ActivePlateTakenAsync(schoolId, i.Plate, activeId, exceptVehicleId, ct)) return $"An active vehicle with registration number {i.Plate} already exists.";
            return null;
        }

        private static void Apply(TbVehicle v, VehicleInput i)
        {
            v.VehicleLabel = i.Label; v.RegistrationNumber = i.Plate; v.VehicleTypeId = i.TypeId; v.OwnershipId = i.OwnershipId;
            v.ModelYear = i.ModelYear; v.SeatingCapacity = i.Seats; v.OwnerName = i.OwnerName; v.OwnerContact = i.OwnerContact;
            v.LastServiceDate = i.LastService; v.PucExpiryDate = i.Puc; v.InsuranceExpiryDate = i.Insurance;
        }

        private async Task<ApiResponse<VehicleDto>> ReplyAsync(string schoolId, long vehicleId, int code, string message, CancellationToken ct)
        {
            var row = await _repo.GetDetailAsync(schoolId, vehicleId, ct);
            return row == null ? Result<VehicleDto>(500, "Something went wrong. Please try again later.", null) : Result(code, message, ToDto(row, SchoolClock.Today()));
        }

        private static VehicleDto ToDto(VehicleRow r, DateOnly today)
        {
            var v = r.Vehicle;
            var puc = TransportRules.Expiry(v.PucExpiryDate, today, out var pucDays);
            var ins = TransportRules.Expiry(v.InsuranceExpiryDate, today, out var insDays);
            return new VehicleDto
            {
                VehicleId = v.VehicleId, VehicleLabel = v.VehicleLabel, RegistrationNumber = v.RegistrationNumber,
                VehicleTypeId = v.VehicleTypeId, VehicleTypeName = r.TypeName, OwnershipId = v.OwnershipId, OwnershipName = r.OwnershipName, IsRented = IsRented(r.OwnershipCode),
                ModelYear = v.ModelYear, SeatingCapacity = v.SeatingCapacity, OwnerName = v.OwnerName, OwnerContact = v.OwnerContact, LastServiceDate = v.LastServiceDate,
                PucExpiryDate = v.PucExpiryDate, PucStatus = puc, PucDaysLeft = pucDays, InsuranceExpiryDate = v.InsuranceExpiryDate, InsuranceStatus = ins, InsuranceDaysLeft = insDays,
                StatusName = r.StatusName, ActiveDriverCount = r.ActiveDriverCount, CreatedAt = v.CreatedAt, UpdatedAt = v.UpdatedAt
            };
        }
    }
}
