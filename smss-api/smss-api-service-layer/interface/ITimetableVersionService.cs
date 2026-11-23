using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId always comes from the caller's token; null/empty means the account has no school (400). Every write is refused (409) in a closed academic year.
    public interface ITimetableVersionService
    {
        Task<ApiResponse<List<TimetableSummaryDto>>> ListAsync(string? schoolId, long? academicYearId, long sectionId, CancellationToken ct);
        /// <summary>A new DRAFT: blank, or a copy of another timetable of the same section and year.</summary>
        Task<ApiResponse<TimetableSummaryDto>> CreateAsync(string? schoolId, long userId, CreateTimetableRequest request, CancellationToken ct);
        Task<ApiResponse<TimetableSummaryDto>> RenameAsync(string? schoolId, long userId, long timetableId, RenameTimetableRequest request, CancellationToken ct);
        /// <summary>Put a lesson (or a lesson without a teacher) into a slot of this timetable. Only an ACTIVE timetable is checked for teacher clashes while it is edited.</summary>
        Task<ApiResponse<TimetableEntryDto>> AssignAsync(string? schoolId, long userId, long timetableId, AssignSlotRequest request, CancellationToken ct);
        Task<ApiResponse<TimetableEntryDto>> UnassignAsync(string? schoolId, long userId, long timetableId, UnassignSlotRequest request, CancellationToken ct);
        /// <summary>Dry run: lists every problem without saving anything.</summary>
        Task<ApiResponse<TimetableValidationDto>> ValidateAsync(string? schoolId, long timetableId, CancellationToken ct);
        /// <summary>Makes this the section's ACTIVE timetable (the previous one is archived in the same transaction). 409 with the problems when it can't.</summary>
        Task<ApiResponse<TimetableActivationDto>> ActivateAsync(string? schoolId, long userId, long timetableId, CancellationToken ct);
        Task<ApiResponse<TimetableSummaryDto>> ArchiveAsync(string? schoolId, long userId, long timetableId, CancellationToken ct);
        Task<ApiResponse<TimetableSummaryDto>> RestoreAsync(string? schoolId, long userId, long timetableId, CancellationToken ct);
        /// <summary>Only a draft that was never active.</summary>
        Task<ApiResponse<object>> DeleteAsync(string? schoolId, long timetableId, CancellationToken ct);
        /// <summary>What "apply to other sections" WOULD create. Nothing is saved.</summary>
        Task<ApiResponse<ApplyResultDto>> PreviewApplyAsync(string? schoolId, long timetableId, ApplyToSectionsRequest request, CancellationToken ct);
        /// <summary>Creates a new independent DRAFT for each chosen section of the class; never overwrites one.</summary>
        Task<ApiResponse<ApplyResultDto>> ApplyAsync(string? schoolId, long userId, long timetableId, ApplyToSectionsRequest request, CancellationToken ct);
    }
}
