using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId is the caller's own school from the token; null / empty answers 400. The controller decides who may write.
    public interface ISchoolEventService
    {
        Task<ApiResponse<SchoolEventDto>> CreateAsync(string? schoolId, long createdByUserId, SchoolEventRequest req, CancellationToken ct);
        Task<ApiResponse<SchoolEventDto>> UpdateAsync(string? schoolId, long eventId, SchoolEventRequest req, CancellationToken ct);
        /// <summary>Soft delete: the event is hidden (status Inactive), never removed.</summary>
        Task<ApiResponse<SchoolEventDto>> DeleteAsync(string? schoolId, long eventId, CancellationToken ct);
        Task<ApiResponse<List<SchoolEventDto>>> GetRangeAsync(string? schoolId, long? academicYearId, DateOnly? from, DateOnly? to, CancellationToken ct);
        Task<ApiResponse<List<SchoolEventDto>>> GetUpcomingAsync(string? schoolId, int take, CancellationToken ct);
        Task<ApiResponse<List<EventCategoryDto>>> GetCategoriesAsync(CancellationToken ct);
    }
}
