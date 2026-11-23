using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId always comes from the caller's token; null/empty means the account has no school (400).
    public interface IPeriodScheduleService
    {
        Task<ApiResponse<List<ScheduleDto>>> ListAsync(string? schoolId, CancellationToken ct);
        Task<ApiResponse<ScheduleDto>> CreateAsync(string? schoolId, long userId, CreateScheduleRequest request, CancellationToken ct);
        Task<ApiResponse<ScheduleDto>> RenameAsync(string? schoolId, long userId, long scheduleId, RenameScheduleRequest request, CancellationToken ct);
        Task<ApiResponse<ScheduleDto>> SetDefaultAsync(string? schoolId, long userId, long scheduleId, CancellationToken ct);
        /// <summary>A schedule that is the default, assigned to a year, or has timetable entries on its periods cannot be deleted (409).</summary>
        Task<ApiResponse<object>> DeleteAsync(string? schoolId, long scheduleId, CancellationToken ct);
        Task<ApiResponse<ScheduleDto>> AddSlotAsync(string? schoolId, long scheduleId, ScheduleSlotRequest request, CancellationToken ct);
        Task<ApiResponse<ScheduleDto>> UpdateSlotAsync(string? schoolId, long scheduleId, long periodId, ScheduleSlotRequest request, CancellationToken ct);
        Task<ApiResponse<ScheduleDto>> ToggleSlotAsync(string? schoolId, long scheduleId, long periodId, CancellationToken ct);
        Task<ApiResponse<ScheduleDto>> DeleteSlotAsync(string? schoolId, long scheduleId, long periodId, CancellationToken ct);

        Task<ApiResponse<YearSchedulesDto>> GetYearAsync(string? schoolId, long academicYearId, CancellationToken ct);
        /// <summary>Replaces the year's class/day schedule assignments. Closed year: 409. A change that would strand a timetable slot: 409 with counts.</summary>
        Task<ApiResponse<YearSchedulesDto>> SaveYearAsync(string? schoolId, long userId, long academicYearId, SaveYearSchedulesRequest request, CancellationToken ct);
    }
}
