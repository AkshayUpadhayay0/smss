using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    public interface IPartnerSchoolService
    {
        /// <summary>Anonymous. Active + opted-in schools only, cached for a short while.</summary>
        Task<ApiResponse<List<PartnerSchoolDto>>> GetPublicListAsync(CancellationToken ct);

        /// <summary>Super Admin. Sets the website flag of one school and drops the cached public list.</summary>
        Task<ApiResponse<WebsiteVisibilityDto>> SetVisibilityAsync(string schoolId, SetWebsiteVisibilityRequest request, CancellationToken ct);
    }
}
