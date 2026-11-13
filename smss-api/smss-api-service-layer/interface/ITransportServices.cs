using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId is the caller's own school from the token; null / empty answers 400. Another school's ids answer 404.
    public interface ITransportAreaService
    {
        Task<ApiResponse<TransportAreaPagedDto>> ListAsync(string? schoolId, string? search, string? status, int page, int pageSize, CancellationToken ct);
        Task<ApiResponse<TransportAreaDto>> GetAsync(string? schoolId, long areaId, CancellationToken ct);
        Task<ApiResponse<TransportAreaDto>> CreateAsync(string? schoolId, TransportAreaRequest request, CancellationToken ct);
        Task<ApiResponse<TransportAreaDto>> UpdateAsync(string? schoolId, long areaId, TransportAreaRequest request, CancellationToken ct);
        /// <summary>Deactivating is refused while an Active driver still has the area; activating is refused when another Active area has the same name.</summary>
        Task<ApiResponse<TransportAreaDto>> ToggleStatusAsync(string? schoolId, long areaId, CancellationToken ct);
    }

    public interface IVehicleService
    {
        Task<ApiResponse<VehicleLookupsDto>> GetLookupsAsync(string? schoolId, CancellationToken ct);
        Task<ApiResponse<VehiclePagedDto>> ListAsync(string? schoolId, string? search, string? status, long? vehicleTypeId, int page, int pageSize, CancellationToken ct);
        Task<ApiResponse<VehicleDto>> GetAsync(string? schoolId, long vehicleId, CancellationToken ct);
        Task<ApiResponse<VehicleDto>> CreateAsync(string? schoolId, VehicleRequest request, CancellationToken ct);
        Task<ApiResponse<VehicleDto>> UpdateAsync(string? schoolId, long vehicleId, VehicleRequest request, CancellationToken ct);
        /// <summary>Deactivating is refused while an Active driver is assigned; activating is refused when another Active vehicle has the same label or plate.</summary>
        Task<ApiResponse<VehicleDto>> ToggleStatusAsync(string? schoolId, long vehicleId, CancellationToken ct);
    }

    public interface IDriverService
    {
        Task<ApiResponse<DriverLookupsDto>> GetLookupsAsync(string? schoolId, CancellationToken ct);
        Task<ApiResponse<DriverPagedDto>> ListAsync(string? schoolId, string? search, string? status, long? vehicleId, int page, int pageSize, CancellationToken ct);
        Task<ApiResponse<DriverDto>> GetAsync(string? schoolId, string driverId, CancellationToken ct);
        Task<ApiResponse<DriverDto>> CreateAsync(string? schoolId, DriverRequest request, CancellationToken ct);
        Task<ApiResponse<DriverDto>> UpdateAsync(string? schoolId, string driverId, DriverRequest request, CancellationToken ct);
        /// <summary>Flips Active / Inactive. The login follows it; deactivating also revokes the driver's refresh tokens. Reactivating is refused while the vehicle or an area is Inactive.</summary>
        Task<ApiResponse<DriverDto>> ToggleStatusAsync(string? schoolId, string driverId, CancellationToken ct);
        Task<ApiResponse<DriverPhotoDto>> UploadPhotoAsync(string? schoolId, string driverId, Stream content, string fileName, long length, CancellationToken ct);
        Task<ApiResponse<DriverPhotoDto>> RemovePhotoAsync(string? schoolId, string driverId, CancellationToken ct);
        /// <summary>A separate action after registration (like Employee). The role is fixed by the server (DRIVER); a temporary password is returned ONCE.</summary>
        Task<ApiResponse<DriverLoginCreatedDto>> CreateLoginAsync(string? schoolId, string driverId, CancellationToken ct);
    }

    /// <summary>Authenticated read of a driver's photo from private storage. Another school's driver is a 404.</summary>
    public interface IDriverPhotoService
    {
        Task<StudentPhotoResult> GetAsync(string? schoolId, string driverId, CancellationToken ct);
    }
}
