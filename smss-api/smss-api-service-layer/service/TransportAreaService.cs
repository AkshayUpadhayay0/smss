using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class TransportAreaService : TransportServiceBase, ITransportAreaService
    {
        private const string NameIndex = "uq_tb_transport_areas_active_name";
        private readonly ITransportAreaRepository _repo;

        public TransportAreaService(ITransportAreaRepository repo, ILogger<TransportAreaService> logger) : base(logger) => _repo = repo;

        private Task<int?> ActiveStatusIdAsync(CancellationToken ct) => _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);

        public async Task<ApiResponse<TransportAreaPagedDto>> ListAsync(string? schoolId, string? search, string? status, int page, int pageSize, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TransportAreaPagedDto>(400, TenantMessages.NoSchool, null);
            if (PagingError(page, pageSize) is { } paging) return Result<TransportAreaPagedDto>(400, paging, null);
            try
            {
                var (statusId, error, config) = await ParseStatusAsync(status, _repo.GetStatusIdByNameAsync, ct);
                if (error != null) return config ? ConfigError<TransportAreaPagedDto>(error) : Result<TransportAreaPagedDto>(400, error, null);

                var (rows, total) = await _repo.ListAsync(schoolId, search, statusId, page, pageSize, ct);
                return Result(200, "Successfully fetched", new TransportAreaPagedDto { Total = total, Page = page, PageSize = pageSize, Items = rows.Select(ToDto).ToList() });
            }
            catch (Exception ex) { return Error<TransportAreaPagedDto>(ex, "listing transport areas"); }
        }

        public async Task<ApiResponse<TransportAreaDto>> GetAsync(string? schoolId, long areaId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TransportAreaDto>(400, TenantMessages.NoSchool, null);
            try
            {
                // another school's area is a 404 too, so ids cannot be probed across schools
                var row = await _repo.GetDetailAsync(schoolId, areaId, ct);
                return row == null ? Result<TransportAreaDto>(404, "Area not found.", null) : Result(200, "Successfully fetched", ToDto(row));
            }
            catch (Exception ex) { return Error<TransportAreaDto>(ex, "fetching a transport area"); }
        }

        public async Task<ApiResponse<TransportAreaDto>> CreateAsync(string? schoolId, TransportAreaRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TransportAreaDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await ActiveStatusIdAsync(ct);
                if (activeId == null) return ConfigError<TransportAreaDto>($"Status '{StatusNames.Active}' ({StatusNames.GeneralType}) is missing in lut_status");

                var name = req.AreaName?.Trim() ?? string.Empty;
                if (name.Length == 0) return Result<TransportAreaDto>(400, "Area name is required.", null);
                if (await _repo.ActiveNameTakenAsync(schoolId, name, activeId.Value, null, ct))
                    return Result<TransportAreaDto>(409, $"An active area named '{name}' already exists.", null);

                var now = DateTime.UtcNow;
                var area = new TbTransportArea { SchoolId = schoolId, StatusId = activeId.Value, CreatedAt = now, UpdatedAt = now };
                Apply(area, req);
                await _repo.AddAsync(area, ct);
                await _repo.SaveChangesAsync(ct);
                return await ReplyAsync(schoolId, area.AreaId, 201, "Area created successfully.", ct);
            }
            catch (DbUpdateException ex) when (ConstraintOf(ex) == NameIndex)
            {
                return Result<TransportAreaDto>(409, "An active area with this name already exists.", null);
            }
            catch (Exception ex) { return Error<TransportAreaDto>(ex, "creating a transport area"); }
        }

        public async Task<ApiResponse<TransportAreaDto>> UpdateAsync(string? schoolId, long areaId, TransportAreaRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TransportAreaDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await ActiveStatusIdAsync(ct);
                if (activeId == null) return ConfigError<TransportAreaDto>($"Status '{StatusNames.Active}' ({StatusNames.GeneralType}) is missing in lut_status");

                var area = await _repo.GetForUpdateAsync(schoolId, areaId, ct);
                if (area == null) return Result<TransportAreaDto>(404, "Area not found.", null);

                var name = req.AreaName?.Trim() ?? string.Empty;
                if (name.Length == 0) return Result<TransportAreaDto>(400, "Area name is required.", null);
                // the name only has to be unique among ACTIVE areas, so an inactive area's name is checked when it is reactivated
                if (area.StatusId == activeId && await _repo.ActiveNameTakenAsync(schoolId, name, activeId.Value, areaId, ct))
                    return Result<TransportAreaDto>(409, $"An active area named '{name}' already exists.", null);

                Apply(area, req);
                area.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                return await ReplyAsync(schoolId, areaId, 200, "Area updated successfully.", ct);
            }
            catch (DbUpdateException ex) when (ConstraintOf(ex) == NameIndex)
            {
                return Result<TransportAreaDto>(409, "An active area with this name already exists.", null);
            }
            catch (Exception ex) { return Error<TransportAreaDto>(ex, "updating a transport area"); }
        }

        public async Task<ApiResponse<TransportAreaDto>> ToggleStatusAsync(string? schoolId, long areaId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<TransportAreaDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await ActiveStatusIdAsync(ct);
                var inactiveId = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType, ct);
                if (activeId == null || inactiveId == null) return ConfigError<TransportAreaDto>("Active / Inactive statuses are missing in lut_status");

                var area = await _repo.GetForUpdateAsync(schoolId, areaId, ct);
                if (area == null) return Result<TransportAreaDto>(404, "Area not found.", null);

                if (area.StatusId == activeId)
                {
                    var drivers = await _repo.GetActiveDriversOfAreaAsync(schoolId, areaId, activeId.Value, ct);
                    if (drivers.Count > 0)
                        return Result<TransportAreaDto>(409, $"'{area.AreaName}' is a pickup area of active driver(s): {NameDrivers(drivers)}. Change their pickup areas first.", null);
                    area.StatusId = inactiveId.Value;
                }
                else
                {
                    if (await _repo.ActiveNameTakenAsync(schoolId, area.AreaName, activeId.Value, areaId, ct))
                        return Result<TransportAreaDto>(409, $"Another active area is already named '{area.AreaName}'. Rename one of them first.", null);
                    area.StatusId = activeId.Value;
                }
                area.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                return await ReplyAsync(schoolId, areaId, 200, area.StatusId == activeId ? "Area activated." : "Area deactivated.", ct);
            }
            catch (DbUpdateException ex) when (ConstraintOf(ex) == NameIndex)
            {
                return Result<TransportAreaDto>(409, "An active area with this name already exists.", null);
            }
            catch (Exception ex) { return Error<TransportAreaDto>(ex, "toggling a transport area's status"); }
        }

        // ---------------------------------------------------------------- helpers
        private static void Apply(TbTransportArea a, TransportAreaRequest r)
        {
            a.AreaName = r.AreaName!.Trim();
            a.DistanceKm = r.DistanceKm == null ? null : Math.Round(r.DistanceKm.Value, 2, MidpointRounding.AwayFromZero);
            a.TransportFee = Math.Round(r.TransportFee!.Value, 2, MidpointRounding.AwayFromZero);
            a.Remarks = Clean(r.Remarks);
        }

        private async Task<ApiResponse<TransportAreaDto>> ReplyAsync(string schoolId, long areaId, int code, string message, CancellationToken ct)
        {
            var row = await _repo.GetDetailAsync(schoolId, areaId, ct);
            return row == null ? Result<TransportAreaDto>(500, "Something went wrong. Please try again later.", null) : Result(code, message, ToDto(row));
        }

        private static TransportAreaDto ToDto(AreaRow r) => new()
        {
            AreaId = r.Area.AreaId, AreaName = r.Area.AreaName, DistanceKm = r.Area.DistanceKm, TransportFee = r.Area.TransportFee, Remarks = r.Area.Remarks,
            StatusName = r.StatusName, ActiveDriverCount = r.ActiveDriverCount, CreatedAt = r.Area.CreatedAt, UpdatedAt = r.Area.UpdatedAt
        };
    }
}
