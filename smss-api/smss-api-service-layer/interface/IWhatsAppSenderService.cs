using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // No sending and no WhatsApp API call lives here: this only keeps the school's sender status (NOT_REGISTERED, PENDING, ACTIVE). A school with no row counts as NOT_REGISTERED and the row is made on first use.
    public interface IWhatsAppSenderService
    {
        /// <summary>
        /// A school's sender status. A caller who is not the Super Admin can only reach the school named in their token: any other school is a 404, as is one that does not exist.
        /// The row is created (NOT_REGISTERED, the school's own number) if it is missing.
        /// </summary>
        Task<ApiResponse<SenderStatusDto>> GetAsync(string? callerSchoolId, string requestedSchoolId, bool isSuperAdmin, CancellationToken ct);
        /// <summary>Super Admin only (the controller checks): set the number, the Meta phone-number id and the status. ACTIVE needs the phone-number id and records when it was verified. Records who and when.</summary>
        Task<ApiResponse<SenderStatusDto>> SaveAsync(string requestedSchoolId, SaveSenderRequestDto request, long userId, CancellationToken ct);
    }
}
