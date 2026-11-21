using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    public interface IOnboardingService
    {
        /// <summary>The checklist for the caller's own school (schoolId from the token; null/empty answers 400).</summary>
        Task<ApiResponse<OnboardingStatusDto>> GetStatusAsync(string? schoolId, long callerUserId, CancellationToken ct);

        /// <summary>
        /// The gate behind every "add students / employees / transport" endpoint: null when the school may go ahead (complete, grandfathered),
        /// otherwise the 409 SETUP_INCOMPLETE response. Fails CLOSED: if the status cannot be worked out the answer is incomplete, never "go ahead".
        /// </summary>
        Task<ApiResponse<SetupIncompleteDto>?> CheckSetupAsync(string? schoolId, CancellationToken ct);
    }
}
