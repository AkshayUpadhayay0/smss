using smss_api_service_layer.dto;
using smss_api_service_layer.helper.idcard;

namespace smss_api_service_layer.@interface
{
    /// <summary>A school's ID-card template and branding. The school always comes from the caller's token.</summary>
    public interface IIdCardSettingsService
    {
        /// <summary>The saved settings, or the defaults when the school never saved any (the response says which).</summary>
        Task<ApiResponse<IdCardSettingsDto>> GetAsync(string? schoolId, CancellationToken ct);
        Task<ApiResponse<IdCardSettingsDto>> SaveAsync(string? schoolId, long userId, IdCardSettingsRequest request, CancellationToken ct);
        Task<ApiResponse<IdCardSignatureDto>> UploadSignatureAsync(string? schoolId, long userId, Stream content, string fileName, long length, CancellationToken ct);
        Task<ApiResponse<IdCardSignatureDto>> RemoveSignatureAsync(string? schoolId, long userId, CancellationToken ct);

        /// <summary>The branding to print with: the saved settings (or defaults) plus the signature read from private storage.</summary>
        Task<IdCardBrand> GetBrandAsync(string schoolId, CancellationToken ct);
        /// <summary>The saved principal signature, decoded, or null when there is none.</summary>
        Task<IdCardPicture?> GetSignatureAsync(string schoolId, CancellationToken ct);
    }
}
