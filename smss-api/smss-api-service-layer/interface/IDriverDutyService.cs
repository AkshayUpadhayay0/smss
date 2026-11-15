using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // Stage 2: driver duty and live location.
    // schoolId / userId come from the caller's TOKEN (never from a body). The tracking token (header X-Duty-Token) is passed in as a plain string and is
    // never logged, stored or put in a message: only its SHA-256 is used.
    public interface IDriverDutyService
    {
        /// <summary>GET api/Drivers/me: profile, vehicle, areas and today's duty state of the signed-in driver.</summary>
        Task<ApiResponse<DriverMeDto>> GetMeAsync(string? schoolId, long userId, CancellationToken ct);

        /// <summary>POST api/DriverDuty/start: 201 new session, 200 resumed (a new token is issued either way).</summary>
        Task<ApiResponse<DutyStartedDto>> StartAsync(string? schoolId, long userId, CancellationToken ct);

        /// <summary>POST api/DriverDuty/location, authenticated by the tracking token only.</summary>
        Task<ApiResponse<DutyLocationResultDto>> RecordLocationAsync(string? trackingToken, DutyLocationRequest? request, CancellationToken ct);

        /// <summary>POST api/DriverDuty/end with the tracking token. Idempotent.</summary>
        Task<ApiResponse<DutyEndedDto>> EndByTokenAsync(string? trackingToken, CancellationToken ct);

        /// <summary>POST api/DriverDuty/end with the driver's login (no token needed). Idempotent.</summary>
        Task<ApiResponse<DutyEndedDto>> EndForDriverAsync(string? schoolId, long userId, CancellationToken ct);

        /// <summary>GET api/Transport/live: the open sessions of the school with their latest fix.</summary>
        Task<ApiResponse<LiveBoardDto>> GetLiveAsync(string? schoolId, long? areaId, long? vehicleId, CancellationToken ct);
    }
}
