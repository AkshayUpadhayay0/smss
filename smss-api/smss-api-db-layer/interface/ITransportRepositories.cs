using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record TransportLookupRow(long Id, string Code, string Name);
    /// <summary>A driver as named in "still assigned to ..." messages.</summary>
    public record DriverRef(string DriverId, string FullName);
    public record VehicleRef(long VehicleId, string Label, string Plate, int StatusId);
    public record AreaRef(long AreaId, string Name, int StatusId);

    public record AreaRow(TbTransportArea Area, string StatusName, int ActiveDriverCount);
    public record VehicleRow(TbVehicle Vehicle, string TypeName, string OwnershipName, string OwnershipCode, string StatusName, int ActiveDriverCount);
    public record DriverListRow(TbDriver Driver, string StatusName, string? VehicleLabel, string? VehiclePlate, bool HasLogin, List<string> AreaNames);
    public record DriverDetailRow(TbDriver Driver, string StatusName, string? VehicleLabel, string? VehiclePlate, string? LoginId, List<AreaRef> Areas);

    // Every method takes the school id: there is no way to read another school's transport data through these repositories.
    public interface ITransportAreaRepository
    {
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct);
        Task<(List<AreaRow> Rows, int Total)> ListAsync(string schoolId, string? search, int? statusId, int page, int pageSize, CancellationToken ct);
        Task<AreaRow?> GetDetailAsync(string schoolId, long areaId, CancellationToken ct);
        Task<TbTransportArea?> GetForUpdateAsync(string schoolId, long areaId, CancellationToken ct);
        Task AddAsync(TbTransportArea area, CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);
        /// <summary>Whether another ACTIVE area of this school already has this name (case-insensitive).</summary>
        Task<bool> ActiveNameTakenAsync(string schoolId, string name, int activeStatusId, long? exceptAreaId, CancellationToken ct);
        Task<List<DriverRef>> GetActiveDriversOfAreaAsync(string schoolId, long areaId, int activeStatusId, CancellationToken ct);
    }

    public interface IVehicleRepository
    {
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct);
        Task<(List<VehicleRow> Rows, int Total)> ListAsync(string schoolId, string? search, int? statusId, long? vehicleTypeId, int page, int pageSize, CancellationToken ct);
        Task<VehicleRow?> GetDetailAsync(string schoolId, long vehicleId, CancellationToken ct);
        Task<TbVehicle?> GetForUpdateAsync(string schoolId, long vehicleId, CancellationToken ct);
        Task AddAsync(TbVehicle vehicle, CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);
        Task<bool> ActiveLabelTakenAsync(string schoolId, string label, int activeStatusId, long? exceptVehicleId, CancellationToken ct);
        Task<bool> ActivePlateTakenAsync(string schoolId, string plate, int activeStatusId, long? exceptVehicleId, CancellationToken ct);
        Task<List<TransportLookupRow>> GetTypesAsync(CancellationToken ct);
        Task<List<TransportLookupRow>> GetOwnershipsAsync(CancellationToken ct);
        /// <summary>An active vehicle type / ownership, or null.</summary>
        Task<TransportLookupRow?> GetActiveTypeAsync(long id, CancellationToken ct);
        Task<TransportLookupRow?> GetActiveOwnershipAsync(long id, CancellationToken ct);
        Task<List<DriverRef>> GetActiveDriversOfVehicleAsync(string schoolId, long vehicleId, int activeStatusId, CancellationToken ct);
    }

    public interface IDriverRepository
    {
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        /// <summary>Transaction-scoped lock so two drivers created at once cannot be given the same id.</summary>
        Task LockDriverIdsAsync(string schoolId, CancellationToken ct);
        Task<int> GetMaxIdSequenceAsync(string schoolId, string prefix, CancellationToken ct);
        Task<TbSchools?> GetSchoolAsync(string schoolId, CancellationToken ct);
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct);

        Task<(List<DriverListRow> Rows, int Total)> ListAsync(string schoolId, string? search, int? statusId, long? vehicleId, int page, int pageSize, CancellationToken ct);
        Task<DriverDetailRow?> GetDetailAsync(string schoolId, string driverId, CancellationToken ct);
        Task<TbDriver?> GetForUpdateAsync(string schoolId, string driverId, CancellationToken ct);
        Task AddAsync(TbDriver driver, CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);

        Task<bool> EmailTakenAsync(string schoolId, string email, string? exceptDriverId, CancellationToken ct);
        /// <summary>Whether another ACTIVE driver of this school already has this licence number (case-insensitive).</summary>
        Task<bool> ActiveDlTakenAsync(string schoolId, string dlNumber, int activeStatusId, string? exceptDriverId, CancellationToken ct);

        // references (always limited to the school)
        Task<VehicleRef?> GetVehicleAsync(string schoolId, long vehicleId, CancellationToken ct);
        Task<List<AreaRef>> GetAreasAsync(string schoolId, IReadOnlyCollection<long> areaIds, CancellationToken ct);
        Task<List<AreaRef>> GetAssignedAreasAsync(string schoolId, string driverId, CancellationToken ct);
        /// <summary>Makes the driver's pickup areas exactly <paramref name="areaIds"/>: link rows not listed are DELETED, new ones added. The caller saves.</summary>
        Task ReplaceAreasAsync(string schoolId, string driverId, IReadOnlyCollection<long> areaIds, CancellationToken ct);
        Task<List<VehicleRef>> GetActiveVehiclesAsync(string schoolId, int activeStatusId, CancellationToken ct);
        Task<List<AreaRef>> GetActiveAreasAsync(string schoolId, int activeStatusId, CancellationToken ct);

        // login
        Task<bool> HasLoginAsync(string schoolId, string driverId, CancellationToken ct);
        Task<TbUsers?> GetLoginUserAsync(string schoolId, string driverId, CancellationToken ct);
        Task<LutRole?> GetRoleByCodeAsync(string roleCode, CancellationToken ct);
        Task AddUserAsync(TbUsers user, CancellationToken ct);
        Task RevokeRefreshTokensAsync(long userId, DateTime revokedAt, CancellationToken ct);

        /// <summary>Whether the driver exists in THIS school, and the stored photo key (null = no photo).</summary>
        Task<(bool Exists, string? Stored)> GetPhotoKeyAsync(string schoolId, string driverId, CancellationToken ct);
    }
}
